using System.Text.Json.Nodes;

namespace FrostAbyss;

static class FallbackSelectionTests
{
    static JsonNode Card(string id, string family, string desc) => new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    static JsonObject Run(params JsonNode[] cards) => new() { ["profundidad"] = 0, ["reliquias"] = new JsonArray(cards.Select(c => c.DeepClone()).ToArray()) };
    public static void Run(Action<bool, string> check)
    {
        var original = Paths.Data;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-Fallback-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = temp;
            var cfg = new Settings { Simulations = 0, SkipShieldCardsEarly = true, ShieldSkipFloors = 7, AllowBasicBurn = false, AllowBasicLightning = false };
            var pilot = new Pilot(); pilot.Save(cfg);
            var freeze = Card("es_carambano", "escarcha", "Tus autoataques CONGELAN al objetivo");
            var slow = Card("es_helada", "escarcha", "Tus golpes ralentizan un 15% durante 4 s");
            var iceShield = Card("fixture_ice_shield", "escarcha", "Empiezas cada sala con un escudo del 10% de tu vida durante 3 s");
            var shield = Card("au_alba", "aurora", "Empiezas cada sala con un escudo del 25% de tu vida durante 3 s");
            var fire = Card("br_fragua", "brasa", "+50% de daño de tus reacciones elementales, y +5% de daño");
            var attack = Card("attack", "acero", "+20% de daño");
            var bounce = Card("fu_cadena", "fulgor", "Cada 5 autoataques, tu golpe salta a todos, y +2% de daño");
            var shock = Card("fu_descarga", "fulgor", "Tus autoataques ELECTRIFICAN al objetivo");
            var detonate = Card("br_detonador", "brasa", "Tus autoataques CONSUMEN el estado del objetivo y fuerzan su reacción");
            var burn = Card("br_yesca", "brasa", "Tus autoataques dejan al objetivo ARDIENDO");
            var confirmed = Run(freeze);
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
            {
                Choice Pick(JsonNode run, params JsonNode[] cards) => Pilot.Rank(pilot.CardChoices(cards, method, run))[0];
                var picked = Pick(confirmed, shield, attack);
                check(picked.Card.At("id").S() == "attack" && !picked.Label.Contains("兜底"), method + " 有正常攻击时不选前X层盾，也不进入兜底");
                check(Pick(confirmed, shield).Label.Contains("兜底"), method + " 仅剩护盾时兜底继续");
                check(Pick(Run(), iceShield, fire).Card.At("id").S() == "fixture_ice_shield", method + " 没有正常牌时仍优先冰系盾而非转火，保持系列积累");
                check(Pick(confirmed, slow, shock).Card.At("id").S() == "es_helada", method + " 冰伤软避让不会在兜底时误删较好的冰减速，强迫拿禁用带电");
                var repeated = Run(freeze, detonate, burn);
                check(Pick(repeated, detonate).Label.Contains("兜底") && Pick(repeated, burn).Label.Contains("兜底"), method + " 仅剩重复引爆器或关闭普攻状态时均可兜底");
                check(Pick(repeated, detonate, fire).Card.At("id").S() == "br_fragua", method + " 有正常火增伤时仍不重复拿引爆器");
                var capped = Run(freeze, attack, attack, attack);
                check(Pick(capped, attack).Label.Contains("兜底"), method + " 同牌三份仅作策略避让，强制菜单没有其他选择仍继续");
                var unavailable = bounce.DeepClone(); unavailable["available"] = false;
                var noId = new JsonObject { ["desc"] = "+999% de daño" };
                check(Pick(confirmed, noId, unavailable, shield).Card.At("id").S() == "au_alba", method + " 兜底只恢复游戏实际可选项，不恢复无ID或不可用项");
                foreach (var (key, value) in new[] { ("available", false), ("disponible", false), ("disabled", true) })
                {
                    var blocked = bounce.DeepClone(); blocked[key] = value;
                    bool rejected = false;
                    try { pilot.CardChoices([blocked], method, confirmed); } catch (Exception e) { rejected = e.Message.Contains("没有实际可选"); }
                    check(rejected, method + " " + key + " 的游戏不可用状态不被策略兜底覆盖");
                }
                var offered = new[] { slow, shock, shield }; var before = new JsonArray(offered.Select(c => c.DeepClone()).ToArray()).ToJsonString();
                var fallback = pilot.CardChoices(offered, method, confirmed);
                check(fallback.All(c => c.Label.Contains("兜底") && c.Reason.Contains("兜底扣分"))
                    && before == new JsonArray(offered.Select(c => c.DeepClone()).ToArray()).ToJsonString(), method + " 所有兜底记录扣分与原因，原始卡牌不改写");
            }
            // Screenshot regression: shock + 10% shield + burn with +4% damage.
            cfg.AllowBasicBurn = true; cfg.ShieldSkipFloors = 20; pilot.Save(cfg);
            var flame = Card("br_incendio", "brasa", "Tus autoataques dejan al objetivo ARDIENDO, y +4% de daño");
            var iron = Card("au_alba", "aurora", "Empiezas cada sala con un escudo del 10% de tu vida durante 3 s");
            var screenshotRun = Run(freeze, burn); screenshotRun["profundidad"] = 3;
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
            {
                var picks = pilot.CardChoices([shock, iron, flame], method, screenshotRun);
                check(picks.Single().Card.At("id").S() == "br_incendio" && picks[0].Priority == 1200,
                    method + " 截图三选一：已有燃烧也选第三张烈焰，附带4%伤害真实有收益，不要求手动处理");
                var heldFlame = Run(freeze, flame);
                check(Pilot.Rank(pilot.CardChoices([shock, iron, flame], method, heldFlame))[0].Card.At("id").S() == "br_incendio",
                    method + " 已有同ID烈焰一份时附带数值仍可比较，不能整张视为无效");
                check(Pilot.Rank(pilot.CardChoices([flame, attack], method, screenshotRun))[0].Card.At("id").S() == "attack",
                    method + " 重复燃烧不获独立状态加分，正常攻击优于附带小额伤害");
                var beforeFreeze = Run(burn); beforeFreeze["llegada"] = null;
                check(pilot.CardChoices([slow, flame], method, beforeFreeze).Single().Card.At("id").S() == "es_helada",
                    method + " 烈焰附带数值不能绕过继承前有冰就选冰的优先策略");
                check(pilot.CardChoices([shock, iron, flame], method, beforeFreeze).All(c => c.Label.Contains("兜底"))
                    && Pilot.Rank(pilot.CardChoices([shock, iron, flame], method, beforeFreeze))[0].Card.At("id").S() == "br_incendio",
                    method + " 继承前无冰时截图菜单同样兜底烈焰，不因仅冰策略暂停");
            }
            bool emptyRejected = false;
            try { pilot.CardChoices([], "sima.reliquia", confirmed); } catch (Exception e) { emptyRejected = e.Message.Contains("没有实际可选"); }
            check(emptyRejected, "真正空菜单不杜撰选择，保留等待状态保护");
            var cappedRun = Run(freeze, attack, attack, attack);
            var capChoice = pilot.CardChoices([attack], "sima.guarda", cappedRun).Single();
            check(Cards.Project(cappedRun, capChoice).Count(c => c.At("id").S() == "attack") == 3,
                "兜底继承达到上限的牌不会在本地投影中伪造第四份收益");
            var exit = new Choice("离店", "sima.move", new());
            var poor = new Choice("兜底商品", "sima.trade", new(), priority: -100, card: shield) { Forecast = new(.4, .3, .5, .5, 50, 64) };
            var baseline = new Forecast(.5, .4, .6, .5, 60, 64);
            check(Pilot.BeforeShopExit([poor, exit], baseline).Single() == exit, "非强制商店兜底商品弱于不买基线时离店，不为继续而乱买");
            poor.Forecast = new(.6, .5, .7, .5, 70, 64);
            check(Pilot.BeforeShopExit([poor, exit], baseline).Single() == poor, "商店兜底商品确有模拟收益时仍可购买");
            check(Pilot.BeforeShopExit([poor], baseline).Length == 1, "商店无离店候选时不因策略比较清空所有选择");
            var shopRun = Run(freeze); shopRun["id"] = "shop-run"; shopRun["nodo"] = "shop";
            shopRun["mapa"] = JsonNode.Parse("""{"nodos":{"shop":{"tipo":"chatarrero"},"next":{"tipo":"santuario"}}}""");
            shopRun["destinos"] = new JsonArray("next"); shopRun["botin"] = new JsonObject { ["obolos"] = 10 };
            shopRun["tienda"] = new JsonObject { ["precio"] = 1, ["buffs"] = new JsonArray(shield.DeepClone()) };
            var snapshot = new JsonObject { ["sima"] = new JsonObject { ["run"] = shopRun } };
            check(pilot.Build(snapshot).Any(c => c.Method == "sima.trade" && c.Label.Contains("兜底")), "生产商店护盾同样启用统一兜底");
            shopRun["compras"] = new JsonArray("shop:buff:au_alba");
            check(pilot.Build(snapshot).All(c => c.Method != "sima.trade"), "已买商品不会因策略兜底重复购买");
            shopRun["compras"] = new JsonArray(); shopRun["botin"]!["obolos"] = 0;
            check(pilot.Build(snapshot).All(c => c.Method != "sima.trade"), "余额不足不放宽为可支付，正常离店");
        }
        finally { Paths.Data = original; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
