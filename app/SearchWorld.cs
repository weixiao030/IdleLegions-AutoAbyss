using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

// A local stochastic forward model, never a transport. Hidden menus and maps
// are drawn from observed examples; their probabilities are not server rules.
sealed class SearchWorld
{
    internal JsonNode Snapshot { get; }
    readonly Settings cfg;
    readonly Settings single;
    readonly Simulation simulation;
    readonly SearchMemory memory;
    readonly Random rng;
    readonly CancellationToken ct;
    readonly double initialDepth;
    int rooms;
    bool failed;
    internal bool Terminal { get; private set; }
    JsonNode Run => Snapshot.At("sima", "run")!;
    internal SearchWorld(JsonNode snapshot, Settings config, Simulation simulation, SearchMemory memory, Random rng, CancellationToken ct, Settings? workerSettings = null)
    {
        Snapshot = snapshot; cfg = config; this.simulation = simulation; this.memory = memory; this.rng = rng; this.ct = ct;
        initialDepth = Run.At("profundidad").N();
        single = workerSettings ?? JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(config, J.Options), J.Options)!;
        single.Simulations = config.StrategyId >= 6 ? 8 : 1;
    }
    internal double Value
    {
        get
        {
            var heroes = Run.At("heroes").A().ToArray();
            double hp = heroes.Sum(h => Math.Max(0, h.At("hp").N())) / Math.Max(1, heroes.Sum(h => h.At("hpMax").N()));
            return Math.Clamp(.5 + .4 * Math.Tanh((Run.At("profundidad").N() - initialDepth + rooms / 12d - (failed ? 2 : 0)) / 2) + .05 * hp, 0, 1);
        }
    }
    internal static Choice[] CardsFor(JsonNode[] offered, string method, JsonNode run, Settings cfg)
    {
        if (!OpeningRules.Complete(run, cfg)) return OpeningRules.Choices(offered, method, run, cfg);
        var legal = offered.Where(CardRules.Available).ToArray();
        if (method == "sima.guarda")
        {
            var uncapped = legal.Where(c => c.At("llevas").N() < 3 && Cards.Inherited(run).Count(x => x.At("id").S() == c.At("id").S()) < 3).ToArray();
            if (uncapped.Length > 0) legal = uncapped;
        }
        // After the shared inherited glacier/four-ice opening, strategies 5–7
        // compare all legal builds without family or custom card priorities.
        return legal.Select(c => new Choice((method == "sima.guarda" ? "继承：" : method == "sima.blason" ? "徽章：" : "选牌：") + CardCatalog.Name(c),
            method, new() { ["id"] = c.At("id").S() }, core: Cards.IsCore(c, cfg), card: c)).ToArray();
    }
    internal static Choice[] LegalActions(JsonNode snapshot, Settings cfg)
    {
        var run = snapshot.At("sima", "run"); if (run == null) return [];
        foreach (var (path, key, method) in new[] { ("blasonPendiente", "opciones", "sima.blason"), ("guardaPendiente", "cartas", "sima.guarda") })
        {
            var cards = run.At(path, key).A().ToArray(); if (cards.Length > 0) return CardsFor(cards, method, run, cfg);
        }
        var relics = run.At("reliquiaOferta").A().ToArray();
        if (relics.Length > 0) return CardsFor(relics, "sima.reliquia", run, cfg);
        var revival = RevivalRules.Choice(run); if (revival != null) return [SearchPlanner.Copy(revival)];
        string id = run.At("nodo").S(); var node = run.At("mapa", "nodos", id); string type = node.At("tipo").S();
        bool done = run.At("hechos").A().Any(x => x.S() == id);
        if (!done && type is "combate" or "elite" or "jefe")
        {
            var fights = TargetRules.Choices(snapshot, run, id, cfg.SeparateTargets).Select(SearchPlanner.Copy).ToList();
            if (Pilot.ShouldPact(run, cfg)) fights.Add(new("缔结契约", "sima.pacto", new()));
            return fights.ToArray();
        }
        if (!done && type == "santuario") return Pilot.SanctuaryChoices(snapshot, run).Select(SearchPlanner.Copy).ToArray();
        if (!done && type == "presagio") return EventRules.Choices(snapshot).Select(SearchPlanner.Copy).ToArray();
        if (!done && type == "osario") return [new("打开骨库", "sima.open", new())];
        var options = new List<Choice>();
        if (type == "chatarrero")
        {
            double money = run.At("botin", "obolos").N();
            bool Bought(string offer) => run.At("compras").A().Any(x => x.S() == id + ":" + offer);
            foreach (var service in run.At("servicios").A().Where(CardRules.Available))
            {
                string offer = service.At("id").S();
                if (service.At("obolos").N(double.MaxValue) <= money && !Bought(offer))
                    options.Add(new("商店：" + J.Name(service), "sima.trade", new() { ["oferta"] = offer }));
            }
            if (run.At("tienda", "precio").N(double.MaxValue) <= money)
                foreach (var choice in CardsFor(run.At("tienda", "buffs").A().Where(c => !Bought("buff:" + c.At("id").S())).ToArray(), "sima.trade", run, cfg))
                    options.Add(new("商店：" + choice.Label, "sima.trade", new() { ["oferta"] = "buff:" + choice.Card.At("id").S() },
                        choice.Benefit, choice.Risk, choice.Core, choice.Priority, choice.Card) { Reason = choice.Reason });
        }
        var destinations = run.At("destinos").A().Select(x => x.S()).Where(x => x != "").ToArray();
        var preferred = destinations.Where(dest => (cfg.AllowElite || run.At("mapa", "nodos", dest, "tipo").S() != "elite")
            && (cfg.AllowAmbush || !run.At("mapa", "nodos", dest, "emboscada").B())).ToArray();
        foreach (var dest in preferred.Length > 0 ? preferred : destinations)
            options.Add(new("前往房间 " + dest, "sima.move", new() { ["nodoId"] = dest }));
        return options.ToArray();
    }
    internal Choice[] Actions() { try { return LegalActions(Snapshot, cfg); } catch (Exception e) when (e is not OperationCanceledException) { return []; } }
    static JsonArray ArrayOf(IEnumerable<JsonNode> nodes) => new(nodes.Select(n => n.DeepClone()).ToArray());
    void Done()
    {
        var run = Run; string id = run.At("nodo").S();
        var done = run.At("hechos").A().Select(x => x.S()).ToHashSet(); done.Add(id);
        run["hechos"] = new JsonArray(done.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()); rooms++;
        run["destinos"] = run.At("mapa", "nodos", id, "salidas")?.DeepClone() ?? new JsonArray();
    }
    void AddCard(JsonNode card, string list)
    {
        var old = Effects.Parse(Cards.Current(Run), cfg); var values = Run.At(list) as JsonArray ?? new JsonArray();
        if (Run.At(list) == null) Run[list] = values; values.Add(card.DeepClone());
        var next = Effects.Parse(Cards.Current(Run), cfg);
        double scale = (1 + next.Life) / Math.Max(.001, 1 + old.Life);
        foreach (var h in Run.At("heroes").A()) { h["hpMax"] = h.At("hpMax").N() * scale; h["hp"] = h.At("hp").N() * scale; }
    }
    void BadgeMenu()
    {
        foreach (var family in CardCatalog.Families)
        foreach (int threshold in memory.BadgeThresholds(family))
        {
            if (Run.At("reliquias").A().Count(c => Cards.Family(c) == family) < threshold
                || Run.At("blasones").A().Any(c => Cards.Family(c) == family && c.At("umbral").N() == threshold)) continue;
            var menu = memory.SampleCards(rng, "badge", family, threshold);
            if (menu.Length == 0) continue;
            Run["blasonPendiente"] = new JsonObject { ["familia"] = family, ["umbral"] = threshold, ["opciones"] = ArrayOf(menu) }; return;
        }
    }
    internal void Apply(Choice choice)
    {
        ct.ThrowIfCancellationRequested(); if (Terminal) return;
        var run = Run; string id = run.At("nodo").S(); string kind = run.At("mapa", "nodos", id, "tipo").S();
        switch (choice.Method)
        {
            case "sima.move":
                string dest = choice.Args.At("nodoId").S(); run["nodo"] = dest;
                run["destinos"] = run.At("mapa", "nodos", dest, "salidas")?.DeepClone() ?? new JsonArray();
                if (run.At("mapa", "nodos", dest, "tipo").S() == "chatarrero")
                {
                    run["compras"] = new JsonArray(); run["tienda"] = memory.SampleShop(rng);
                }
                break;
            case "sima.reliquia":
                if (choice.Card != null) AddCard(choice.Card, "reliquias"); run["reliquiaOferta"] = new JsonArray(); BadgeMenu(); break;
            case "sima.blason":
                if (choice.Card != null)
                {
                    var badge = choice.Card.DeepClone(); badge["familia"] = run.At("blasonPendiente", "familia")?.DeepClone() ?? badge.At("familia")?.DeepClone();
                    badge["umbral"] = run.At("blasonPendiente", "umbral")?.DeepClone() ?? badge.At("umbral")?.DeepClone(); AddCard(badge, "blasones");
                }
                run["blasonPendiente"] = null; BadgeMenu(); break;
            case "sima.guarda":
                if (choice.Card == null) break;
                var retained = Cards.Inherited(run).Select(c => c.DeepClone()).ToList();
                if (retained.Count(c => c.At("id").S() == choice.Card.At("id").S()) < 3) retained.Add(choice.Card.DeepClone());
                var nextBadges = Cards.RetainedBadges(run, retained);
                var oldEffects = Effects.Parse(Cards.Current(run), cfg);
                run["reliquias"] = ArrayOf(retained); run["blasones"] = ArrayOf(nextBadges);
                run["buffs"] = new JsonArray();
                var nextEffects = Effects.Parse(Cards.Current(run), cfg);
                double lifeScale = (1 + nextEffects.Life) / Math.Max(.001, 1 + oldEffects.Life);
                foreach (var h in run.At("heroes").A()) { h["hpMax"] = h.At("hpMax").N() * lifeScale; h["hp"] = h.At("hp").N() * lifeScale; }
                run["profundidad"] = run.At("profundidad").N() + 1; run["guardaPendiente"] = null;
                run["llegada"] = new JsonObject { ["reliquias"] = retained.Count, ["blasones"] = run.At("blasones").A().Count(), ["buffs"] = run.At("buffs").A().Count() };
                var map = memory.SampleMap(rng, (int)run.At("profundidad").N());
                if (map == null) { Terminal = true; break; }
                run["mapa"] = map; run["nodo"] = ""; run["hechos"] = new JsonArray();
                run["santuario"] = map.At("santuario")?.DeepClone() ?? new JsonObject();
                run["servicios"] = map.At("servicios")?.DeepClone() ?? new JsonArray();
                run["pacto"] = map.At("pacto")?.DeepClone();
                if (run.At("pacto") is JsonObject nextPact) nextPact["sellado"] = false;
                run["tienda"] = null; run["compras"] = new JsonArray();
                var nodes = map.At("nodos") as JsonObject;
                double first = nodes?.Select(p => p.Value.At("fila").N()).DefaultIfEmpty().Min() ?? 0;
                run["destinos"] = new JsonArray(nodes?.Where(p => p.Value.At("fila").N() == first).Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray() ?? []);
                Snapshot["peeks"] = new JsonObject(); BadgeMenu(); break;
            case "sima.fight":
            case "sima.open":
                bool combat = choice.Method == "sima.fight" || run.At("mapa", "nodos", id, "emboscada").B();
                if (combat)
                {
                    var action = SearchPlanner.Copy(choice); single.Seed = rng.Next();
                    simulation.Evaluate([action], Snapshot, single, ct);
                    var forecast = action.Forecast;
                    if (forecast == null) { Terminal = true; break; }
                    double p = cfg.StrategyId >= 6 ? memory.CalibrateWin(Snapshot, forecast) : forecast.Win;
                    // Calibration cannot manufacture a surviving party when
                    // this batch contains no successful combat state (or vice versa).
                    bool won = forecast.WinState != null && (forecast.LossState == null || rng.NextDouble() < p);
                    var heroes = run.At("heroes").A().Where(h => !h.At("caido").B()).ToArray();
                    var combatState = won ? forecast.WinState : forecast.LossState;
                    // Carry one actual sampled outcome, including casualties and
                    // soul resource. Averaging HP made every survivor immortal
                    // and replenished spent soul between consecutive rooms.
                    if (combatState == null || combatState.Hp.Length != heroes.Length)
                    { failed = !won; Terminal = true; break; }
                    for (int h=0;h<heroes.Length;h++)
                    {
                        double hp = Math.Clamp(combatState.Hp[h], 0, heroes[h].At("hpMax").N());
                        heroes[h]["hp"] = hp; heroes[h]["caido"] = hp <= 0;
                        heroes[h]["anima"] = Math.Clamp(combatState.Mana[h], 0, 100);
                    }
                    if (!won) { failed = true; Terminal = true; break; }
                }
                Done();
                if (kind == "jefe")
                {
                    var inherited = Cards.Inherited(run); var added = run.At("reliquias").A().Skip(inherited.Length).ToArray();
                    if (added.Length == 0) added = run.At("reliquias").A().ToArray();
                    run["guardaPendiente"] = new JsonObject { ["cartas"] = ArrayOf(added) };
                }
                else
                {
                    var menu = memory.SampleCards(rng, "relic");
                    if (menu.Length > 0) run["reliquiaOferta"] = ArrayOf(menu);
                }
                break;
            case "sima.rest":
                string option = choice.Args.At("opcion").S();
                foreach (var hero in run.At("heroes").A())
                {
                    if (option == "reposar" && !hero.At("caido").B()) hero["hp"] = Math.Min(hero.At("hpMax").N(), hero.At("hp").N() + hero.At("hpMax").N() * run.At("santuario", "reposarPct").N() / 100);
                    if (option == "meditar") hero["anima"] = Math.Min(100, hero.At("anima").N() + 60);
                    if (option == "afilar" && hero.At("id").S() == choice.Args.At("heroId").S()) hero["afilado"] = hero.At("afilado").N() + 1;
                    if (option == "levantar" && hero.At("caido").B()) { hero["caido"] = false; hero["hp"] = hero.At("hpMax").N() * run.At("santuario", "levantaPct").N() / 100; hero["anima"] = 0; }
                }
                Done(); break;
            case "sima.tear":
                var revived = run.At("heroes").A().FirstOrDefault(h => h.At("id").S() == choice.Args.At("heroId").S());
                if (revived != null) { revived["caido"] = false; revived["hp"] = revived.At("hpMax").N() * .5; revived["anima"] = 0; run["lagrimas"] = run.At("lagrimas").N() - 1; }
                break;
            case "sima.trade":
                string offer = choice.Args.At("oferta").S();
                double price = choice.Card != null ? run.At("tienda", "precio").N() : run.At("servicios").A().FirstOrDefault(s => s.At("id").S() == offer).At("obolos").N();
                run["botin"] ??= new JsonObject(); run["botin"]!["obolos"] = Math.Max(0, run.At("botin", "obolos").N() - price);
                run["compras"] ??= new JsonArray(); run["compras"]!.AsArray().Add(id + ":" + offer);
                if (choice.Card != null) AddCard(choice.Card, "buffs");
                else foreach (var hero in run.At("heroes").A().Where(h => !h.At("caido").B()))
                {
                    if (offer == "vida") hero["hp"] = Math.Min(hero.At("hpMax").N(), hero.At("hp").N() + hero.At("hpMax").N() * .3);
                    if (offer == "anima") hero["anima"] = Math.Min(100, hero.At("anima").N() + 60);
                }
                break;
            case "sima.pacto":
                if (run.At("pacto") is JsonObject pact) pact["sellado"] = true;
                // The client has no complete contract engine. Stop this rollout
                // at the known state instead of inventing a strength multiplier.
                Terminal = true; break;
            case "sima.omen":
                // Unknown event consequences terminate this rollout at its
                // current state instead of inventing resources or a fake room.
                Terminal = true; break;
            default: Terminal = true; break;
        }
    }
}
