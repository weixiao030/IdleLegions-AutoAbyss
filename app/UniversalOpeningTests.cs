using System.Text.Json.Nodes;
namespace FrostAbyss;
static class UniversalOpeningTests
{
    public static void Run(Action<bool, string> check)
    {
        var old = Paths.Data;
        try
        {
            Paths.Data = Path.Combine(old, "universal-opening");
            JsonNode Card(string id, string family, string desc = "+10% de daño") => IceOpeningTests.Card(id, family, desc);
            var glacier = Card("es_glaciar", "escarcha", "Tus autos CONGELAN, y cada 5 tu golpe salta a todos");
            var freeze = Card("es_carambano", "escarcha", "Tus autoataques CONGELAN al objetivo");
            var bounce = Card("fu_bounce_fixture", "fulgor", "Cada 4 autoataques, tu golpe salta a todos los enemigos");
            var ice = Card("es_gain_fixture", "escarcha"); var fire = Card("br_gain_fixture", "brasa", "+100% de daño");
            foreach (int strategy in Enumerable.Range(1, 7))
            {
                var cfg = new Settings { StrategyId = strategy, Simulations = 0, SkipShieldCardsEarly = false };
                cfg.CustomStrategy.Cards["es_glaciar"] = new() { Mode = "block" };
                var pilot = new Pilot(); pilot.Save(cfg);
                foreach (string method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
                {
                    Choice Pick(JsonNode run, params JsonNode[] cards) => Pilot.Rank(pilot.CardChoices(cards, method, run))[0];
                    foreach (var state in new[] { IceOpeningTests.State(), IceOpeningTests.State(fire), IceOpeningTests.State(freeze) })
                    {
                        var before = state.ToJsonString();
                        check(Pick(state, fire, ice, glacier).Card.At("id").S() == "es_glaciar", $"策略{strategy} {method}冰川单卡优先，不受自定义拉黑或搜索覆盖");
                        check(before == state.ToJsonString(), $"策略{strategy} {method}前置不修改实战快照");
                        check(Pick(state, freeze, ice).Card.At("id").S() == "es_gain_fixture", $"策略{strategy} {method}无冰川时补冰4，不单独追普通冻结");
                        check(Pick(state, bounce, ice).Card.At("id").S() == "es_gain_fixture", $"策略{strategy} {method}非冰弹跳不冒充冰川前置");
                    }
                    var capped = glacier.DeepClone(); capped["llevas"] = 3;
                    if (method == "sima.guarda") check(Pick(IceOpeningTests.State(), capped, ice).Card.At("id").S() == "es_gain_fixture", $"策略{strategy}前置尊重继承上限");
                    var bad = glacier.DeepClone(); bad["available"] = false;
                    check(pilot.CardChoices([bad, ice], method, IceOpeningTests.State()).All(c => c.Card.At("id").S() != "es_glaciar"), $"策略{strategy} {method}不可用冰川禁止派发");
                    var only = pilot.CardChoices([fire], method, IceOpeningTests.State());
                    check(method == "sima.trade" ? only.Length == 0 : only.Length == 1, $"策略{strategy} {method}无冰强制菜单合法过渡，商店不乱买");
                    check(Pick(IceOpeningTests.State(), freeze).Card.At("id").S() == "es_carambano", $"策略{strategy} {method}只有普通冻结时可用来补冰系张数");
                }
                var temporary = IceOpeningTests.State(); temporary["reliquias"]!.AsArray().Add(glacier.DeepClone());
                check(!OpeningRules.Complete(temporary, cfg), $"策略{strategy}本层冰川未继承不解除前置");
                var buffs = IceOpeningTests.State(); buffs["buffs"] = new JsonArray(glacier.DeepClone());
                check(!OpeningRules.Complete(buffs, cfg), $"策略{strategy}临时赐福不算继承");
                var four = IceOpeningTests.State(ice, ice, ice, ice);
                check(OpeningRules.Complete(four, cfg), $"策略{strategy}确认继承4张冰系即可执行方案，无需再追普通冻结");
                four["llegada"]!["reliquias"] = 3;
                check(!OpeningRules.Complete(four, cfg), $"策略{strategy}本层第4张冰牌不冒充已继承4卡");
                var separate = IceOpeningTests.State(freeze, bounce); var combined = IceOpeningTests.State(glacier);
                check(!OpeningRules.Complete(separate, cfg) && OpeningRules.Complete(combined, cfg), $"策略{strategy}冰川是一张卡，分开冻结和弹跳不代替冰川或冰4");
                foreach (var completed in new[] { combined, IceOpeningTests.State(ice, ice, ice, ice) })
                {
                    check(!OpeningRules.UsesSearch(cfg, temporary) && OpeningRules.UsesSearch(cfg, completed) == (strategy >= 5), $"策略{strategy}继承冰川或冰4后才启用树搜索");
                    check(OpeningRules.UsesLegacy(cfg, completed) == (strategy == 2), $"策略{strategy}前置完成后才启用经典模型");
                }
                var badge = IceOpeningTests.State(ice, ice, ice); badge["blasones"] = new JsonArray(glacier.DeepClone()); badge["llegada"]!["blasones"] = 1;
                check(!OpeningRules.Complete(badge, cfg), $"策略{strategy}徽章即使保留也不计遗物4卡或冰川继承");
                combined.AsObject().Remove("llegada");
                check(!OpeningRules.Complete(combined, cfg), $"策略{strategy}无到达或继承回执保持前置");
                foreach (string method in new[] { "sima.reliquia", "sima.blason", "sima.guarda" })
                {
                    var run = IceOpeningTests.State();
                    if (method == "sima.reliquia") run["reliquiaOferta"] = new JsonArray(freeze.DeepClone(), ice.DeepClone(), glacier.DeepClone());
                    else run[method == "sima.blason" ? "blasonPendiente" : "guardaPendiente"] = new JsonObject
                        { [method == "sima.blason" ? "opciones" : "cartas"] = new JsonArray(freeze.DeepClone(), ice.DeepClone(), glacier.DeepClone()) };
                    var snapshot = new JsonObject { ["sima"] = new JsonObject { ["run"] = run } };
                    check(Pilot.Rank(pilot.Build(snapshot))[0].Card.At("id").S() == "es_glaciar"
                        && Pilot.Rank(SearchWorld.LegalActions(snapshot, cfg))[0].Card.At("id").S() == "es_glaciar",
                        $"策略{strategy} {method}生产入口与搜索前推都先取冰川");
                }
            }
        }
        finally { Paths.Data = old; }
    }
}
