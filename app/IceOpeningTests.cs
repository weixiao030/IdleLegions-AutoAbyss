using System.Text.Json.Nodes;

namespace FrostAbyss;

static class IceOpeningTests
{
    static JsonNode Card(string id, string family, string desc) => new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    static JsonObject Run(params JsonNode[] cards) => new() { ["profundidad"] = 0, ["llegada"] = null, ["reliquias"] = new JsonArray(cards.Select(c => c.DeepClone()).ToArray()) };
    public static void Run(Action<bool, string> check)
    {
        var original = Paths.Data;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-IceOpening-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = temp;
            var cfg = new Settings { Simulations = 0, SkipShieldCardsEarly = false, AllowBasicBurn = true, AllowBasicLightning = true };
            var pilot = new Pilot(); pilot.Save(cfg);
            var freeze = Card("es_carambano", "escarcha", "Tus autoataques CONGELAN al objetivo");
            var slow = Card("es_helada", "escarcha", "Tus golpes ralentizan un 15% durante 4 s");
            var iceDamage = Card("es_ventisca", "escarcha", "+22% de daño contra enemigos LENTOS");
            var iceBounce = Card("es_esquirla", "escarcha", "Cada 16 autoataques, tu golpe salta a todos, y +5% de defensa");
            var iceShield = Card("fixture_ice_shield", "escarcha", "Empiezas cada sala con un escudo del 10% de tu vida durante 3 s");
            var fire = Card("br_fragua", "brasa", "+50% de daño de tus reacciones elementales, y +5% de daño");
            var burn = Card("br_yesca", "brasa", "Tus autoataques dejan al objetivo ARDIENDO");
            var electricBounce = Card("fu_cadena", "fulgor", "Cada 5 autoataques, tu golpe salta a todos, y +2% de daño");
            var start = Run();
            var acquired = Run(freeze);
            var badge = Run(); badge["blasones"] = new JsonArray(freeze.DeepClone());
            var oldFireNewFreeze = Run(fire, freeze); oldFireNewFreeze["llegada"] = new JsonObject { ["reliquias"] = 1 };
            var confirmed = Run(freeze); confirmed["profundidad"] = 1; confirmed["llegada"] = new JsonObject { ["reliquias"] = 1 };
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
            {
                foreach (var (stage, run) in new[] { ("空开局", start), ("本章已取得冻结", acquired), ("仅冻结徽章", badge), ("旧火牌加本章冻结", oldFireNewFreeze) })
                {
                    var options = new[] { slow, iceDamage, iceBounce, fire, burn, electricBounce };
                    var before = new JsonArray(options.Select(c => c.DeepClone()).ToArray()).ToJsonString();
                    var choices = pilot.CardChoices(options, method, run);
                    check(choices.Length == 3 && choices.All(c => Cards.Family(c.Card) == "escarcha")
                        && CardRules.SeekingInheritedFreeze(run, cfg), method + " " + stage + " 全部只保留冰牌，火增伤和5次电弹跳不能抢走");
                    check(new JsonArray(options.Select(c => c.DeepClone()).ToArray()).ToJsonString() == before,
                        method + " " + stage + " 仅过滤自动候选，不改游戏卡牌或效果");
                }
                var fallback = pilot.CardChoices([fire, burn, electricBounce], method, acquired);
                check(fallback.Length == 3 && fallback.All(c => c.Label.Contains("兜底"))
                    && Pilot.Rank(fallback)[0].Card.At("id").S() == "fu_cadena", method + " 无冰牌时择优兜底选更快弹跳，不因策略暂停");
                var unlocked = pilot.CardChoices([fire, burn, electricBounce, slow, iceBounce], method, confirmed);
                check(!CardRules.SeekingInheritedFreeze(confirmed, cfg) && unlocked.Any(c => c.Card.At("id").S() == "br_fragua")
                    && !unlocked.Any(c => c.Card.At("id").S() == "es_helada")
                    && Pilot.Rank(unlocked)[0].Card.At("id").S() == "fu_cadena",
                    method + " 继承回执确认后才解锁其他家族，并恢复弹跳优先和冰减速正常避让");
                check(Pilot.Rank(pilot.CardChoices([fire, freeze, slow, iceBounce], method, start))[0].Card.At("id").S() == "es_carambano",
                    method + " 仅冰系阶段仍先选首份冻结，不被普通冰牌挤掉");
            }
            cfg.PreferFreezeBuild = false; pilot.Save(cfg);
            check(pilot.CardChoices([slow, fire, electricBounce], "sima.reliquia", acquired).Single().Card.At("id").S() == "es_helada",
                "关闭继承后构筑偏好也不解除继承前冰系优先规则");
            cfg.PreferFreezeBuild = true; cfg.SkipShieldCardsEarly = true; cfg.ShieldSkipFloors = 7; pilot.Save(cfg);
            acquired["profundidad"] = 6;
            check(pilot.CardChoices([iceShield, slow, fire], "sima.reliquia", acquired).Single().Card.At("id").S() == "es_helada",
                "前7层护盾限制仍优先执行，冰盾被过滤时拿冰减速而不是火牌");
            acquired["profundidad"] = 7;
            check(pilot.CardChoices([iceShield, fire], "sima.reliquia", acquired).Single().Card.At("id").S() == "fixture_ice_shield",
                "P7解除护盾限制但未继承冻结时仍只能拿冰盾，不提前切火增伤");
            var storeRun = (JsonObject)acquired.DeepClone(); storeRun["nodo"] = "shop";
            storeRun["mapa"] = JsonNode.Parse("""{"nodos":{"shop":{"tipo":"chatarrero"},"next":{"tipo":"santuario"}}}""");
            storeRun["destinos"] = new JsonArray("next"); storeRun["botin"] = new JsonObject { ["obolos"] = 100 };
            storeRun["tienda"] = new JsonObject { ["precio"] = 1, ["buffs"] = new JsonArray(slow.DeepClone(), fire.DeepClone(), electricBounce.DeepClone()) };
            var store = new JsonObject { ["sima"] = new JsonObject { ["run"] = storeRun } };
            check(pilot.Build(store).Where(c => c.Method == "sima.trade").Single().Card.At("id").S() == "es_helada",
                "生产商店本章有冻结仍只买冰牌，不能靠购买入口继续堆火");
            storeRun["tienda"]!["buffs"] = new JsonArray(fire.DeepClone(), electricBounce.DeepClone());
            var fallbackShop = pilot.Build(store);
            check(fallbackShop.Where(c => c.Method == "sima.trade").All(c => c.Label.Contains("兜底"))
                && Pilot.BeforeShopExit(fallbackShop, null).All(c => c.Method == "sima.move"), "无冰商店可评估兜底商品，关闭抽样时优先正常离店不盲买");
            var repeated = Run(slow, slow);
            check(pilot.CardChoices([slow, fire], "sima.reliquia", repeated).Single().Card.At("id").S() == "es_helada"
                && Cards.Priority(slow, cfg, repeated) == 1250,
                "冰减速重复降权不允许火牌钻空子，继承前仍只冰系，三份上限保留");
        }
        finally { Paths.Data = original; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
