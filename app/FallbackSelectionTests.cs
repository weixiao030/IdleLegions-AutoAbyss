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
            var glacier = Card("es_glaciar", "escarcha", "Tus autos CONGELAN, y cada 5 tu golpe salta a todos");
            var confirmed = Run(glacier);
            foreach (int strategy in new[] {1,3,4})
            {
                cfg.StrategyId = strategy; pilot.Save(cfg);
                foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
                {
                    var options = new[] {shield, shock, attack};
                    var before = new JsonArray(options.Select(c=>c.DeepClone()).ToArray()).ToJsonString();
                    var choices = pilot.CardChoices(options, method, confirmed);
                    check(choices.Length == 1 && choices[0].Card.At("id").S() == "attack", $"策略{strategy} {method}正常牌优先于护盾和关闭状态");
                    var fallback = pilot.CardChoices([shield],method,confirmed);
                    check(method == "sima.trade" ? fallback.Length == 0 : fallback.Length == 1 && fallback[0].Reason.Contains("兜底"), $"策略{strategy} {method}仅盾强制兜底，商店不盲买");
                    var unavailable = attack.DeepClone(); unavailable["available"] = false;
                    check(pilot.CardChoices([unavailable,shield],method,confirmed).All(c=>c.Card.At("id").S() != "attack"), $"策略{strategy} {method}兜底不恢复不可用项");
                    foreach(var (key,value) in new[]{("available",false),("disponible",false),("disabled",true)})
                    {
                        var bad=attack.DeepClone();bad[key]=value;bool rejected=false;
                        try {var result=pilot.CardChoices([bad],method,confirmed);rejected=method=="sima.trade"&&result.Length==0;}catch(Exception e){rejected=e.Message.Contains("没有");}
                        check(rejected,$"策略{strategy} {method} {key}真实不可用不发送");
                    }
                    check(before == new JsonArray(options.Select(c=>c.DeepClone()).ToArray()).ToJsonString(), $"策略{strategy} {method}不修改原始菜单");
                    var capped=attack.DeepClone();capped["llevas"]=3;
                    if(method=="sima.guarda")
                    {
                        var phaseCappedRun=Run(glacier,attack,attack,attack);
                        var legal=pilot.CardChoices([capped,fire],method,phaseCappedRun);
                        check(legal.All(c=>c.Card.At("id").S()!="attack"),$"策略{strategy}满继承上限牌让位合法牌");
                        var only=pilot.CardChoices([capped],method,phaseCappedRun).Single();
                        check(Cards.Project(phaseCappedRun,only).Count(c=>c.At("id").S()=="attack")==3,$"策略{strategy}强制上限过渡不虚构第四张");
                    }
                }
            }
            cfg.StrategyId=1;pilot.Save(cfg);
            // Screenshot regression: shock + 10% shield + burn with +4% damage.
            cfg.AllowBasicBurn = true; cfg.ShieldSkipFloors = 20; pilot.Save(cfg);
            var flame = Card("br_incendio", "brasa", "Tus autoataques dejan al objetivo ARDIENDO, y +4% de daño");
            var iron = Card("au_alba", "aurora", "Empiezas cada sala con un escudo del 10% de tu vida durante 3 s");
            var screenshotRun = Run(glacier, burn); screenshotRun["profundidad"] = 3;
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
            {
                var picks = pilot.CardChoices([shock, iron, flame], method, screenshotRun);
                check(picks.Single().Card.At("id").S() == "br_incendio" && picks[0].Benefit > 0,
                    method + " 截图三选一：已有燃烧也选第三张烈焰，附带4%伤害真实有收益，不要求手动处理");
                var heldFlame = Run(glacier, flame);
                check(Pilot.Rank(pilot.CardChoices([shock, iron, flame], method, heldFlame))[0].Card.At("id").S() == "br_incendio",
                    method + " 已有同ID烈焰一份时附带数值仍可比较，不能整张视为无效");
                check(Pilot.Rank(pilot.CardChoices([flame, attack], method, Run(glacier, burn, attack)))[0].Card.At("id").S() == "attack",
                    method + " 重复燃烧不获独立状态加分，正常攻击优于附带小额伤害");
                var beforeFreeze = Run(burn); beforeFreeze["llegada"] = null;
                check(pilot.CardChoices([slow, flame], method, beforeFreeze).Single().Card.At("id").S() == "es_helada",
                    method + " 烈焰附带数值不能绕过继承前有冰就选冰的优先策略");
                var openingChoices=pilot.CardChoices([shock, iron, flame], method, beforeFreeze);
                check(method=="sima.trade"?openingChoices.Length==0:Pilot.Rank(openingChoices)[0].Card.At("id").S()=="br_incendio",
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
            var shopRun = Run(glacier); shopRun["id"] = "shop-run"; shopRun["nodo"] = "shop";
            shopRun["mapa"] = JsonNode.Parse("""{"nodos":{"shop":{"tipo":"chatarrero"},"next":{"tipo":"santuario"}}}""");
            shopRun["destinos"] = new JsonArray("next"); shopRun["botin"] = new JsonObject { ["obolos"] = 10 };
            shopRun["tienda"] = new JsonObject { ["precio"] = 1, ["buffs"] = new JsonArray(shield.DeepClone()) };
            var snapshot = new JsonObject { ["sima"] = new JsonObject { ["run"] = shopRun } };
            check(pilot.Build(snapshot).All(c => c.Method != "sima.trade"), "生产商店只有软避让护盾时不盲买");
            shopRun["compras"] = new JsonArray("shop:buff:au_alba");
            check(pilot.Build(snapshot).All(c => c.Method != "sima.trade"), "已买商品不会因策略兜底重复购买");
            shopRun["compras"] = new JsonArray(); shopRun["botin"]!["obolos"] = 0;
            check(pilot.Build(snapshot).All(c => c.Method != "sima.trade"), "余额不足不放宽为可支付，正常离店");
        }
        finally { Paths.Data = original; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
