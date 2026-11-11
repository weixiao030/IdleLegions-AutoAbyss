using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FrostAbyss;

sealed record CombatState(double[] Hp, double[] Mana);
sealed record Forecast(double Win, double Low, double High, double Hp, double Score, int Trials,
    double Frozen = 0, double Duration = 0, int KnownEnemies = 0, string[]? UnknownEffects = null, double[]? RemainingHp = null, CombatState? WinState = null, CombatState? LossState = null);
sealed class Choice(string label, string method, JsonObject args, double benefit = 0, double risk = 0, bool core = false, int priority = 0, JsonNode? card = null)
{
    public string Label { get; } = label;
    public string Method { get; } = method;
    public JsonObject Args { get; } = args;
    public double Benefit { get; } = benefit;
    public double Risk { get; } = risk;
    public bool Core { get; } = core;
    public int Priority { get; } = priority;
    public JsonNode? Card { get; } = card;
    public string Reason { get; set; } = "";
    public Forecast? Forecast { get; set; }
    public SearchEstimate? Search { get; set; }
}

// Hybrid event model. The server engine is unavailable. Only observed damage,
// displayed descriptions and explicitly uncertain timing/scaling are used here.
sealed class Simulation
{
    record Profile(string Key, string Kind, double Hp, double Dps, double Interval, double Aoe, int Row, int Depth);
    record HeroProfile(double Hit, double Interval, double HealRate, double FireRate, double LightningRate, double AccountAttack = 0);
    record Skill(double Mult, double Cooldown, string Element, bool Aoe, string Focus, string Cond, double Value, double Cost, bool Heal, bool Freezes);
    readonly object modelGate = new();
    readonly List<Profile> enemies = [];
    readonly Dictionary<(string Key, string Kind, int Depth, int Row), (Profile[] Matching, Profile[] Pool, Profile[] Attacks)> profileCache = [];
    (Profile[] Matching, Profile[] Pool, Profile[] Attacks) Profiles(string key, string kind, int depth, int row)
    {
        var cacheKey = (key, kind, depth, row);
        if (profileCache.TryGetValue(cacheKey, out var cached)) return cached;
        var matching = enemies.Where(p => p.Key == key).OrderBy(p => Math.Abs(p.Depth-depth)*8 + Math.Abs(p.Row-row)).Take(6).ToArray();
        var pool = matching.Length > 0 ? matching : enemies.Where(p => p.Kind == kind).OrderBy(p => Math.Abs(p.Depth-depth)*8 + Math.Abs(p.Row-row)).Take(12).ToArray();
        var attacks = pool.Where(p => p.Dps > 0).ToArray();
        if (attacks.Length == 0) attacks = enemies.Where(p => p.Dps > 0 && p.Kind == kind).OrderBy(p => Math.Abs(p.Depth-depth)).Take(20).ToArray();
        if (profileCache.Count > 4096) profileCache.Clear();
        return profileCache[cacheKey] = (matching, pool, attacks);
    }
    readonly Dictionary<string, HeroProfile> heroes = [];
    readonly HashSet<string> observed = [];
    public int Battles { get; private set; }
    public double LastLoss { get; private set; } = .18;
    public Simulation(bool load = true)
    {
        var dir = Path.Combine(Paths.Data, "battles");
        if (load && Directory.Exists(dir)) foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(x => J.Read(x).At("time").N()).TakeLast(250))
            Learn(new JsonObject { ["lastBattle"] = J.Read(file) }, false);
    }
    static string Key(JsonNode? e) => e.At("char").S() + ":" + e.At("variant").S("0");
    static double Median(IEnumerable<double> x, double fallback = 0)
    {
        var a = x.Where(double.IsFinite).Order().ToArray(); return a.Length == 0 ? fallback : a[a.Length / 2];
    }
    internal static double Health(JsonNode[] events, string name)
    {
        double sum = 0; var samples = new List<double>();
        foreach (var e in events.Where(x => x.At("side").S() == "ally" && x.At("dst").S() == name))
        {
            sum += e.At("dmg").N(); var p = e.At("hpPct").N(100);
            if (p >= 10 && p <= 85) samples.Add(sum / (1 - p / 100));
        }
        return Median(samples);
    }
    public void Learn(JsonNode? s, bool persist = true)
    {
        lock (modelGate) LearnCore(s, persist);
    }
    void LearnCore(JsonNode? s, bool persist)
    {
        var b = s.At("lastBattle"); var id = b.At("time").S();
        if (id == "" || observed.Contains(id)) return;
        var ev = b.At("result", "events").A().OrderBy(x => x.At("t").N()).ToArray();
        var foe = b.At("vista", "enemigos").A().ToArray();
        if (ev.Length == 0 || foe.Length == 0) return;
        observed.Add(id); profileCache.Clear();
        if (persist) J.Write(Path.Combine(Paths.Data, "battles", id.Replace('.', '-') + ".json"), b);
        var before = b.At("before")!; var cfg = new Settings(); var fx = Effects.Parse(Cards.Current(before), cfg);
        double duration = Math.Max(1, b.At("result", "durationSec").N());
        int row = (int)before.At("mapa", "nodos", before.At("nodo").S(), "fila").N(1), depth = (int)before.At("profundidad").N();
        string kind = before.At("mapa", "nodos", before.At("nodo").S(), "tipo").S("combate");
        foreach (var h in before.At("heroes").A())
        {
            var name = h.At("nombre").S();
            var events = ev.Where(e => e.At("side").S() == "ally" && e.At("src").S() == name).ToArray();
            var basic = events.Where(e => e.At("dmg").N() > 0 && e.At("skill") == null && !e.At("tick").B() && e.At("rel") == null && !e.At("castOnly").B()).ToArray();
            if (basic.Length < 4) continue;
            // Remove critical, reaction and parsed relic multipliers before fitting.
            var account = BattleContext.Hero(b.At("context"), h.At("id").S());
            double criticalMultiplier = account.At("stats", "critdmg").N(250) / 100 + fx.CritDamage;
            var raw = basic.Select(e => e.At("dmg").N() / (e.At("crit").B() ? Math.Max(1, criticalMultiplier) : 1) /
                (e.At("reaction").S() == "vaporizacion" ? 2 : e.At("reaction").S() == "superconductor" ? 1.3 : 1) /
                fx.Outgoing(e.At("est").A().Any(x => x.S() is "lento" or "congelado"), e.At("est").A().Any(), e.At("hpPct").N(100) / 100, kind is "elite" or "jefe"));
            var ts = basic.Select(e => e.At("t").N()).Distinct().Order().ToArray();
            var gap = Median(ts.Zip(ts.Skip(1), (a, b) => b - a).Where(x => x >= .2 && x < 2.5), .55);
            var outgoing = events.Sum(e => Math.Max(0, e.At("dmg").N()));
            var healing = events.Where(e => e.At("dst").S() == name).Sum(e => Math.Max(0, -e.At("dmg").N()));
            var next = new HeroProfile(Median(raw), Math.Clamp(gap * (1 + fx.Speed), .25, 2),
                Math.Max(0, healing - outgoing * fx.Leech) / duration,
                events.Count(e => e.At("reaction").S() == "vaporizacion") / duration,
                events.Count(e => e.At("reaction").S().Contains("superconduc")) / duration,
                Math.Max(account.At("stats", "atk").N(), account.At("stats", "pmag").N()));
            if (heroes.TryGetValue(h.At("id").S(), out var old))
                next = next with { Hit = (old.AccountAttack > 0 && next.AccountAttack > 0 ? old.Hit * next.AccountAttack / old.AccountAttack : old.Hit) * .7 + next.Hit * .3, Interval = old.Interval * .7 + next.Interval * .3, HealRate = old.HealRate * .7 + next.HealRate * .3 };
            heroes[h.At("id").S()] = next;
        }
        foreach (var e in foe)
        {
            var name = e.At("name").S(); var hp = Health(ev, name); if (hp <= 0) continue;
            var outgoing = ev.Where(x => x.At("side").S() == "enemy" && x.At("src").S() == name && x.At("dmg").N() > 0).ToArray();
            var died = ev.FirstOrDefault(x => x.At("dst").S() == name && x.At("kill").B()).At("t").N(duration);
            // Union observed frozen intervals, rather than dividing damage by the
            // entire fight and applying another arbitrary freezing multiplier.
            double frozen = 0, end = 0;
            foreach (var v in ev.Where(x => x.At("side").S() == "ally" && x.At("dst").S() == name && x.At("est").A().Any(y => y.S() == "congelado")))
            {
                var t = v.At("t").N(); var until = Math.Min(died, t + .7);
                frozen += Math.Max(0, until - Math.Max(t, end)); end = Math.Max(end, until);
            }
            double exposure = Math.Max(.5, died - frozen);
            double dps = outgoing.Sum(x => x.At("dmg").N()) / exposure * (1 + fx.Defense);
            var times = outgoing.Select(x => x.At("t").N()).Distinct().Order().ToArray();
            double interval = Median(times.Zip(times.Skip(1), (a, b) => b - a).Where(x => x >= .3 && x < 4), 1);
            if (fx.Freeze && outgoing.Length > 0)
            {
                var volleys = outgoing.GroupBy(x=>Math.Round(x.At("t").N(),1)).Select(g=>g.Sum(x=>x.At("dmg").N()));
                double burst = Median(volleys) / interval * (1+fx.Defense);
                dps = Math.Min(dps,burst);
            }
            double aoe = outgoing.GroupBy(x => Math.Round(x.At("t").N(), 1)).Count(g => g.Select(x => x.At("dst").S()).Distinct().Count() > 1) / (double)Math.Max(1, times.Length);
            // Zero damage under a permanent freeze is a censored sample, not zero
            // attack power. Keep it only as a health observation.
            enemies.Add(new(Key(e), kind, hp, dps, interval, aoe, row, depth));
        }
        var max = before.At("heroes").A().Sum(h => h.At("hpMax").N());
        LastLoss = b.At("result", "sima", "dano") is JsonObject damage ? damage.Sum(x => x.Value.N()) / Math.Max(1, max) : LastLoss;
        Battles++;
    }
    static double Number(string desc) => Regex.Match(desc, @"([0-9]+(?:[.,][0-9]+)?)%").Value.TrimEnd('%').Replace(',', '.') is { } v && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n / 100 : 1;
    internal static bool DamagingSkill(JsonNode k)
    {
        if (k.At("damages") is JsonValue flag && flag.TryGetValue<bool>(out var explicitValue)) return explicitValue;
        string target = k.At("target").S();
        if (target.StartsWith("ally") || target.StartsWith("self")) return false;
        return (target.StartsWith("enemy") || target == "all_enemies")
            && Regex.IsMatch(Effects.Normalize(k.At("desc").S()).ToLowerInvariant(), @"dano|damage|伤害");
    }
    internal static bool HealingSkill(JsonNode k) => !k.At("damages").B() && k.At("target").S().StartsWith("ally")
        && Regex.IsMatch(Effects.Normalize(k.At("desc").S()).ToLowerInvariant(), @"cura|vida|heal");
    static Skill[] Skills(JsonNode? h) => h.At("skills").A().Where(k => k.At("equipped").B() && (DamagingSkill(k) || HealingSkill(k))).Select(k =>
        new Skill(Math.Clamp(Number(k.At("desc").S()), .1, 20), Math.Max(2, k.At("cooldown").N(10)), k.At("element").S("physical"),
            k.At("desc").S().Contains("TODOS") || k.At("target").S().Contains("all"), k.At("rule", "focus").S(), k.At("rule", "cond").S("always"), k.At("rule", "value").N(), k.At("animaCoste").N(), HealingSkill(k), Regex.IsMatch(k.At("desc").S().ToLowerInvariant(), @"congela|freeze|冻结|冰冻"))).ToArray();
    internal static double SharpenPotential(JsonNode? hero) => Skills(hero).Where(k => !k.Heal).Sum(k => k.Mult * (k.Aoe ? 2 : 1) / k.Cooldown);
    public void Evaluate(Choice[] choices, JsonNode s, Settings cfg, CancellationToken ct)
    {
        // Zero/negative trials explicitly disable sampling, never invent a win
        // probability or silently substitute a different configured count.
        if (cfg.Simulations <= 0) { foreach (var c in choices) c.Forecast = null; return; }
        ct.ThrowIfCancellationRequested();
        // Read JSON and learned collections only in the serial preparation phase.
        // JsonNode may lazily materialize objects on reads. Workers must not share
        // those nodes, even when application code regards the snapshot as read-only.
        var work = new List<Action>();
        lock (modelGate)
        {
        var run = s.At("sima", "run")!;
        var members = run.At("heroes").A().Where(h => !h.At("caido").B()).ToArray();
        if (members.Length == 0) members = s.At("heroes").A().Take(4).ToArray();
        if (members.Length == 0) throw new Exception("没有可用于推演的英雄。");
        var all = s.At("heroes").A().ToArray(); int n = members.Length;
        var live = members.Select(h => all.FirstOrDefault(x => x.At("id").S() == h.At("id").S())).ToArray();
        var maxHp = members.Select((h,i) => h.At("hpMax").N(live[i].At("stats", "hp").N(1000))).ToArray();
        var startHp = members.Select((h,i) => h.At("hp").N(maxHp[i])).ToArray();
        var names = members.Select(h => h.At("nombre").S(h.At("name").S())).ToArray();
        var skills = live.Select(Skills).ToArray();
        var statAtk = live.Select(h => Math.Max(h.At("stats", "atk").N(1000), h.At("stats", "pmag").N())).ToArray();
        var hit = members.Select((h,i) => heroes.GetValueOrDefault(h.At("id").S()) is { } p
            ? p.Hit * (p.AccountAttack > 0 ? statAtk[i] / p.AccountAttack : 1) : statAtk[i] * .6).ToArray();
        var interval = members.Select((h,i) => heroes.GetValueOrDefault(h.At("id").S())?.Interval ?? 1 / Math.Max(.4, live[i].At("stats", "spd").N(1))).ToArray();
        var healRate = members.Select(h => heroes.GetValueOrDefault(h.At("id").S())?.HealRate ?? 0).ToArray();
        var current = Effects.Parse(Cards.Current(run), cfg);
        string node = run.At("nodo").S(); int depth = (int)run.At("profundidad").N();
        var baseMana = members.Select(h => h.At("anima").N(50)).ToArray();
        var critBase = live.Select(h => h.At("stats", "crit").N(40)/100).ToArray();
        var critDamage = live.Select(h => h.At("stats", "critdmg").N(200)/100).ToArray();
        var sharpen = members.Select(h => h.At("afilado").N()).ToArray();
        var heroIds = members.Select(h => h.At("id").S()).ToArray();
        double healPct = run.At("santuario", "reposarPct").N()/100;
        int trials = cfg.Simulations, seed = cfg.Seed;
        double uncertainty = cfg.ModelUncertainty, freezeDuration = cfg.FreezeDuration, riskAversion = cfg.RiskAversion, freezeWeight = cfg.FreezeWeight;
        bool consumeFreeze = cfg.LightningConsumesFreeze, avoidConflict = cfg.AvoidFireLightning;
        foreach (var c in choices)
        {
            ct.ThrowIfCancellationRequested();
            var targetNode = c.Method == "sima.move" ? c.Args.At("nodoId").S() : node;
            var nd = run.At("mapa", "nodos", targetNode); int row = (int)nd.At("fila").N(1);
            string kind = nd.At("tipo").S("combate");
            var foes = s.At("peeks", targetNode, "enemigos").A().ToArray();
            int scenarioDepth = depth;
            // Inheritance is used next floor. Replaying the already defeated
            // boss with only inherited cards made every option look unwinnable.
            // Next enemies are not visible: use an uncertain ordinary-room prior.
            if (c.Method == "sima.guarda")
            {
                foes = []; row = 1; kind = "combate"; scenarioDepth++;
            }
            // A confirmed chapter uses actual retained cards at its current depth.
            // Do not replay the defeated boss or advance the depth twice.
            if (c.Method == "local.chapterReview") { foes = []; row = 1; kind = "combate"; }
            // Selection rooms are evaluated against the visible next fights.
            // All cards share the same scenario, never random per-card enemies.
            if ((c.Card != null || c.Method == "sima.rest") && c.Method is not ("sima.guarda" or "local.chapterReview") && foes.Length == 0)
            {
                var dest = run.At("destinos").A().Select(x => x.S()).FirstOrDefault(id => s.At("peeks", id, "enemigos").A().Any());
                if (dest != null) { foes = s.At("peeks", dest, "enemigos").A().ToArray(); nd = run.At("mapa", "nodos", dest); row = (int)nd.At("fila").N(row); kind = nd.At("tipo").S(kind); }
            }
            bool rest = c.Method == "sima.rest" && c.Args.At("opcion").S() is "reposar" or "meditar" or "afilar";
            if (c.Card == null && !rest && c.Method != "sima.fight" && c.Method != "sima.open" && !(c.Method == "sima.move" && (kind is "combate" or "elite" or "jefe" || nd.At("emboscada").B())))
            {
                double avgHp = startHp.Sum() / Math.Max(1,maxHp.Sum());
                if(c.Method=="sima.rest" && c.Args.At("opcion").S()=="reposar") avgHp=Math.Min(1,avgHp+run.At("santuario","reposarPct").N()/100);
                // Safe moves have no combat outcome to sample. Each of the configured
                // trials uses the same deterministic state transition.
                c.Forecast=new(1,1,1,avgHp,100+avgHp*cfg.RiskAversion*25+c.Benefit*6-c.Risk*3,cfg.Simulations);
                continue;
            }
            int m = Math.Clamp(foes.Length > 0 ? foes.Length : kind == "jefe" ? 1 : 3, 1, 8);
            var fx = Effects.Parse(Cards.Project(run, c), cfg);
            var eHp = new double[m]; var eDps = new double[m]; var eInterval = new double[m]; var aoe = new double[m]; var focus = new string[m];
            int known = 0;
            for (int e = 0; e < m; e++)
            {
                var f = e < foes.Length ? foes[e] : null; var key = Key(f);
                var (matching, pool, attacks) = Profiles(key, kind, scenarioDepth, row);
                if (matching.Length > 0) known++;
                double Scale(Profile p) => Math.Pow(1.06, row-p.Row) * Math.Pow(1.23, scenarioDepth-p.Depth) * cfg.EnemyStrength;
                double mult = kind == "jefe" ? 7 : kind == "elite" ? 2 : 1;
                eHp[e] = Median(pool.Select(p => p.Hp * Scale(p)), hit.Sum()*18*mult);
                // Cached pools are invalidated by every newly observed battle.
                // If frozen samples contain no attacks, use other observed enemies
                // at this depth as a broad uncertain prior, not a hardcoded 15 DPS.
                eDps[e] = Median(attacks.Select(p => p.Dps * Scale(p)), maxHp.Sum()*.04*mult);
                eInterval[e] = Math.Clamp(Median(attacks.Select(p => p.Interval), 1), .4, 3);
                aoe[e] = Median(attacks.Select(p => p.Aoe), .1);
                focus[e] = f.At("foco").S("front");
            }
            var objective = c.Args.At("objetivos");
            var targets = names.Select(name => Array.FindIndex(foes, e => e.At("name").S() == objective.At(name).S())).ToArray();
            string restOption = c.Args.At("opcion").S(), restHero = c.Args.At("heroId").S();
            bool iceSearch = !current.Freeze && c.Card != null && Cards.Family(c.Card) == "escarcha";
            bool conflictAllowed = CardRules.BoostDetonator(fx, cfg);
            var sharp = sharpen.Select((value,i) => value + (rest && restOption == "afilar" && restHero == heroIds[i] ? 1 : 0)).ToArray();
            var unknown = fx.Unknown.Concat(fx.CleavePeriods.Count > 1 ? new[] { "多个弹跳周期按触发并集近似，独立伤害叠加待服务端验证" } : [])
                .Concat(run.At("pacto", "sellado").B() ? new[] { "已签契约的独立影响尚未完整建模，经验敌人样本可能混合契约状态" } : []).Distinct().ToArray();
            var bouncePeriods = fx.CleavePeriods.ToArray(); var criticalPeriods = fx.CriticalPeriods.ToArray();
            work.Add(() =>
            {
            var rng = new Random(seed); int wins = 0; double rem = 0, frozenSum = 0, totalTime = 0;
            var remaining = new double[n]; CombatState? winState = null, lossState = null;
            var projectedMax = maxHp.Select(v => v * (1 + fx.Life) / (1 + current.Life)).ToArray();
            var hp = new double[n]; var immune = new double[n]; var shield = new double[n]; var eh = new double[m]; var frozenTo = new double[m]; var slowTo = new double[m]; var statusTo = new double[m]; var debuffTo = new double[m]; var tauntTo = new double[m]; var tauntBy = new int[m];
            var marks = new int[m]; var markTo = new double[m];
            var next = new double[n]; var enext = new double[m]; var attackCount = new int[n]; var received = new int[n]; var mana = new double[n];
            var skillNext = skills.Select(x => new double[x.Length]).ToArray();
            for (int trial = 0; trial < trials; trial++)
            {
                if ((trial & 127) == 0) ct.ThrowIfCancellationRequested();
                double strength = Math.Exp((rng.NextDouble()*2-1)*uncertainty*(known < m ? 2.5 : 1));
                double freezeSecs = freezeDuration * (.65+rng.NextDouble()*.7); // Unidentified server duration, exposed to sensitivity.
                for (int h=0;h<n;h++)
                {
                    immune[h]=0; hp[h]=startHp[h]*(1+fx.Life)/(1+current.Life); shield[h]=projectedMax[h]*fx.Shield;
                    if (rest && restOption == "reposar") hp[h]=Math.Min(maxHp[h],hp[h]+maxHp[h]*healPct);
                    next[h]=.12+h*.05+rng.NextDouble()*.12; attackCount[h]=received[h]=0; mana[h]=baseMana[h];
                    if (rest && restOption == "meditar") mana[h]=Math.Min(100,mana[h]+60);
                    for(int k=0;k<skills[h].Length;k++) skillNext[h][k]=1+k*.8;
                }
                for(int e=0;e<m;e++) { marks[e]=0; markTo[e]=0; eh[e]=eHp[e]*strength; frozenTo[e]=slowTo[e]=statusTo[e]=debuffTo[e]=tauntTo[e]=0; enext[e]=.5+rng.NextDouble()*eInterval[e]; }
                double now=0, freezeTime=0; bool win=false;
                for(int turn=0;turn<20000;turn++)
                {
                    double at=180.2; int who=-1, selectedSkill=-1; bool enemy=false;
                    for(int h=0;h<n;h++) if(hp[h]>0)
                    {
                        if(next[h]<at) { at=next[h]; who=h; enemy=false; selectedSkill=-1; }
                        for(int k=0;k<skills[h].Length;k++)
                        {
                            // A cooled-down skill can wait for mana. Once mana
                            // arrives it becomes ready now, never in the past.
                            double ready=Math.Max(now,skillNext[h][k]);
                            if(mana[h]>=skills[h][k].Cost && ready<at)
                            { at=ready; who=h; enemy=false; selectedSkill=k; }
                        }
                    }
                    for(int e=0;e<m;e++) if(eh[e]>0 && enext[e]<at) { at=enext[e]; who=e; enemy=true; selectedSkill=-1; }
                    if(who<0||at>=180.2) break;
                    int aliveE=0,fz=0;for(int e=0;e<m;e++)if(eh[e]>0){aliveE++;if(frozenTo[e]>now)fz++;}
                    freezeTime+=(at-now)*(aliveE==0?0:fz/(double)aliveE); now=at;
                    if(enemy)
                    {
                        int e=who;
                        if(frozenTo[e]>now) { enext[e]=frozenTo[e]+.02;continue; }
                        int h=focus[e]=="back"?n-1:focus[e]=="random"?rng.Next(n):0;
                        if(focus[e]=="lowest") { double ratio=double.MaxValue;for(int k=0;k<n;k++)if(hp[k]>0&&hp[k]/maxHp[k]<ratio){ratio=hp[k]/maxHp[k];h=k;} }
                        if(tauntTo[e]>now && hp[tauntBy[e]]>0)h=tauntBy[e];
                        if(hp[h]<=0){h=Array.FindIndex(hp,v=>v>0);if(h<0)break;}
                        double attack=eDps[e]*eInterval[e]*strength*(.8+rng.NextDouble()*.4);
                        bool area=rng.NextDouble()<aoe[e];
                        for(int k=0;k<n;k++)
                        {
                            if(hp[k]<=0 || immune[k]>now || !area && k!=h)continue;
                            var defense=fx.Defense+Math.Min(fx.RampCap,received[k]*fx.RampDefense)+(hp[k]/maxHp[k]<fx.LowAt?fx.LowDefense:0);
                            double d=attack/(area?n:1)/(1+defense);received[k]++;
                            if(now>3)shield[k]=0;double blocked=Math.Min(d,shield[k]);shield[k]-=blocked;hp[k]-=d-blocked;
                        }
                        enext[e]=now+eInterval[e]/(1-(slowTo[e]>now?Math.Min(.75,fx.Slow):0));
                        continue;
                    }
                    int hero=who, target=targets[hero];
                    if(target<0||target>=m||eh[target]<=0){target=-1;for(int e=0;e<m;e++)if(eh[e]>0&&(target<0||eh[e]<eh[target]))target=e;}
                    if(target<0){win=true;break;}
                    Skill? skill=null;
                    if(selectedSkill>=0)
                    {
                        var sk=skills[hero][selectedSkill];
                        bool condition=sk.Cond switch { "self_below"=>hp[hero]/maxHp[hero]*100<sk.Value,"enemies_at_least"=>aliveE>=sk.Value,"target_hp"=>sk.Value>=0?eh[target]/eHp[target]*100>sk.Value:eh[target]/eHp[target]*100 < -sk.Value,"target_status"=>sk.Value==2?frozenTo[target]>now:statusTo[target]>now,"enemy_casting"=>false,_=>true };
                        if(!condition) { skillNext[hero][selectedSkill]=now+.25; continue; }
                        skill=sk;skillNext[hero][selectedSkill]=now+sk.Cooldown;mana[hero]-=sk.Cost;
                    }
                    if(skill?.Focus=="front")target=Array.FindIndex(eh,v=>v>0);
                    else if(skill?.Focus=="back"){for(int e=m-1;e>=0;e--)if(eh[e]>0){target=e;break;}}
                    bool basic=skill==null; if(basic){attackCount[hero]++;mana[hero]=Math.Min(100,mana[hero]+10);}
                    if (skill?.Heal == true)
                    {
                        int lowest = -1;
                        for (int h=0;h<n;h++) if (hp[h]>0 && (lowest<0 || hp[h]/projectedMax[h]<hp[lowest]/projectedMax[lowest])) lowest=h;
                        for (int h=0;h<n;h++) if (hp[h]>0 && (skill.Aoe || h==lowest)) hp[h]=Math.Min(projectedMax[h], hp[h]+projectedMax[h]*skill.Mult);
                        continue;
                    }
                    bool cleave=basic&&bouncePeriods.Any(period=>attackCount[hero]%period==0) || skill?.Aoe==true;
                    string element=skill?.Element??"physical";
                    if(basic&&fx.Burn)element="fire";else if(basic&&fx.Lightning)element="lightning";
                    for(int e=0;e<m;e++)
                    {
                        if(eh[e]<=0 || !cleave&&e!=target)continue;
                        double critChance=Math.Clamp(critBase[hero]+fx.Crit,0,1);
                        bool guaranteedCrit = basic && criticalPeriods.Any(period => attackCount[hero] % period == 0);
                        double crit=guaranteedCrit || rng.NextDouble()<critChance?Math.Max(1,critDamage[hero]+fx.CritDamage):1;
                        // The client describes Afilar as +8% skill damage per stack.
                        double d=hit[hero]*(skill==null?1:skill.Mult*(1+.08*sharp[hero]))*crit*fx.Outgoing(slowTo[e]>now||frozenTo[e]>now,statusTo[e]>now||frozenTo[e]>now,eh[e]/Math.Max(1,eHp[e]*strength),kind is "elite" or "jefe")*(.9+rng.NextDouble()*.2);
                        if(debuffTo[e]>now)d*=1.4;
                        if(markTo[e]>now && marks[e]>0) d*=1+marks[e]*(fx.Mark+fx.MarkDeepen)+fx.VsMarked;
                        // Resolve an existing elemental reaction before a basic
                        // detonator consumes its status; skills do not trigger it.
                        // Direct fire skills and burn damage over time are different.
                        // Fire-family passives never imply fire attacks; lightning
                        // consumption remains an exposed uncertain assumption.
                        if(frozenTo[e]>now && (element=="fire" || element=="lightning" && consumeFreeze)) {d*=(element=="fire"?2:1.3)*(1+fx.ReactionDamage);frozenTo[e]=0;if(element=="lightning")debuffTo[e]=now+6;}
                        if(basic && fx.ConsumesStatus) { frozenTo[e]=0; statusTo[e]=0; slowTo[e]=0; }
                        eh[e]-=d;hp[hero]=Math.Min(maxHp[hero]*(1+fx.Life)/(1+current.Life),hp[hero]+d*fx.Leech+healRate[hero]*interval[hero]/(1+fx.Speed));
                        if(basic&&fx.Freeze||skill?.Freezes==true)frozenTo[e]=Math.Max(frozenTo[e],now+freezeSecs);
                        if(basic && fx.Mark>0) { marks[e]=Math.Min(Math.Max(1,fx.MarkCap),markTo[e]>now?marks[e]+1:1); markTo[e]=now+10; }
                        if(fx.Slow>0)slowTo[e]=now+Math.Max(2,fx.SlowSeconds);
                        if(fx.Poison||element is "fire" or "poison" or "holy" or "shadow")statusTo[e]=now+4;
                        if(fx.Taunt&&basic){tauntBy[e]=hero;tauntTo[e]=now+2;}
                        if(eh[e]<=0){hp[hero]=Math.Min(projectedMax[hero],hp[hero]+fx.KillHeal*projectedMax[hero]);immune[hero]=now+fx.KillImmune;}
                    }
                    if(basic) next[hero]=now+interval[hero]/(1+fx.Speed);
                    if(eh.All(v=>v<=0)){win=true;break;}
                }
                var outcomeState = new CombatState(hp.Select(v => Math.Max(0,v)).ToArray(), mana.ToArray());
                if (win) winState = outcomeState; else lossState = outcomeState;
                if(win)wins++;rem+=hp.Sum(x=>Math.Max(0,x))/Math.Max(1,projectedMax.Sum());
                for (int h=0;h<n;h++) remaining[h]+=Math.Max(0,hp[h])/trials;
                frozenSum+=freezeTime/Math.Max(.1,now);totalTime+=now;
            }
            double p=wins/(double)trials,z=1.96,denom=1+z*z/trials;
            double center=(p+z*z/(2*trials))/denom,margin=z*Math.Sqrt((p*(1-p)+z*z/(4*trials))/trials)/denom;
            double avg=rem/trials,coverage=frozenSum/trials;
            double searchBonus=iceSearch?freezeWeight:0;
            double conflict=avoidConflict&&fx.ConsumesStatus&&!conflictAllowed?freezeWeight*(1-coverage):0;
            c.Forecast=new(p,Math.Min(p,Math.Max(0,center-margin)),Math.Max(p,Math.Min(1,center+margin)),avg,
                p*100+avg*riskAversion*25+Math.Min(4,30/Math.Max(1,totalTime/trials))+coverage*freezeWeight*.15+searchBonus+c.Benefit*6-c.Risk*3-conflict,trials,coverage,totalTime/trials,known,unknown,remaining,winState,lossState);
            });
        }
        }
        Parallel.ForEach(work, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount/2, 1, 8) }, action => action());
        if (choices.Any(c => c.Forecast is { } f && new[] { f.Win, f.Low, f.High, f.Hp, f.Score, f.Frozen, f.Duration }.Any(x => !double.IsFinite(x))))
            throw new Exception("当前参数导致模型无法产生有限结果，已暂停；请调整参数后继续。配置未被截断。");
    }
}
