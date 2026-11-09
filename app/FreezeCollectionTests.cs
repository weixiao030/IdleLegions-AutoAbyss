using System.Text.Json.Nodes;

namespace FrostAbyss;

static class FreezeCollectionTests
{
    static JsonNode Card(string id, string family, string desc) => new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    static JsonObject Run(params JsonNode[] cards) => new() { ["profundidad"] = 0, ["llegada"] = null, ["reliquias"] = new JsonArray(cards.Select(c => c.DeepClone()).ToArray()) };
    public static void Run(Action<bool, string> check)
    {
        var original = Paths.Data;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-FreezeCollection-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = temp;
            var cfg = new Settings { Simulations = 0, SkipShieldCardsEarly = false };
            var pilot = new Pilot(); pilot.Save(cfg);
            var freeze = Card("es_carambano", "escarcha", "Tus autoataques CONGELAN al objetivo");
            var alternate = Card("es2_carambano", "escarcha", "Tus golpes básicos congelan");
            var glacier = Card("es_glaciar", "escarcha", "Tus autos CONGELAN, y cada 5 tu golpe salta a todos");
            var mixed = Card("fixture_freeze_damage", "escarcha", "Tus autoataques CONGELAN al objetivo, y +8% de daño");
            var slow = Card("es_helada", "escarcha", "Tus golpes ralentizan un 15% durante 4 s");
            var iceDamage = Card("es_ventisca", "escarcha", "+22% de daño contra enemigos LENTOS");
            var bounce = Card("fu_cadena", "fulgor", "Cada 5 autoataques, tu golpe salta a todos, y +2% de daño");
            var fire = Card("br_fragua", "brasa", "+50% de daño de tus reacciones elementales, y +5% de daño");
            var attack = Card("attack", "acero", "+20% de daño");
            check(Cards.IsCore(mixed, cfg), "混合冻结描述仍被识别为核心，不依赖固定ID");
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
            {
                foreach (int copies in new[] { 1, 2, 3, 4 })
                {
                    var acquired = Run(Enumerable.Repeat(freeze, copies).ToArray());
                    var choices = pilot.CardChoices([freeze, alternate, glacier, mixed, slow, iceDamage, bounce, fire], method, acquired);
                    check(choices.Any(c => c.Card.At("id").S() == "es_carambano" && c.Priority == 2000 && !c.Label.Contains("兜底"))
                        && choices.Any(c => c.Card.At("id").S() == "es_glaciar" && c.Priority == 2200)
                        && choices.All(c => Cards.Family(c.Card) == "escarcha"), method + " 本章冻结" + copies + "份仍正常优先多拿，普通2000/五次弹跳2200不降权、不转火");
                    check(Pilot.Rank(choices.Where(c => c.Card.At("id").S() != "es_glaciar").ToArray())[0].Priority == 2000,
                        method + " 本章冻结" + copies + "份后重复冻结仍高于1950普通冰牌");
                    check(CardRules.FallbackPenalty(freeze, acquired, cfg, method) == 0, method + " 收集阶段不误扣纯冻结重复或三份继承罚分");
                }
                var badge = Run(); badge["blasones"] = new JsonArray(freeze.DeepClone(), freeze.DeepClone());
                check(Pilot.Rank(pilot.CardChoices([freeze, slow, fire], method, badge))[0].Card.At("id").S() == "es_carambano",
                    method + " 临时冻结徽章不阻止继续取得和继承冻结遗物");
                var oldFireNewFreeze = Run(fire, freeze, freeze); oldFireNewFreeze["llegada"] = new JsonObject { ["reliquias"] = 1 };
                check(pilot.CardChoices([freeze, slow], method, oldFireNewFreeze).Single(c => c.Card.At("id").S() == "es_carambano").Priority == 2000,
                    method + " 旧火遗物加本章重复冻结，不误认为冻结已继承");
                var inherited = Run(freeze); inherited["profundidad"] = 1; inherited["llegada"] = new JsonObject { ["reliquias"] = 1 };
                var after = pilot.CardChoices([freeze, alternate, mixed, glacier, bounce, fire], method, inherited);
                check(!after.Any(c => c.Card.At("id").S() is "es_carambano" or "es2_carambano")
                    && after.Any(c => c.Card.At("id").S() == "es_glaciar")
                    && after.Any(c => c.Card.At("id").S() == "fixture_freeze_damage"),
                    method + " 成功继承一份后正常避开纯冻结，但带新增弹跳或有效数值的混合牌仍参加比较");
                check(after.Single(c => c.Card.At("id").S() == "fixture_freeze_damage").Priority == 750
                    && after.Single(c => c.Card.At("id").S() == "es_glaciar").Priority == 1945,
                    method + " 继承后混合牌按实际新增收益750/1945，不再获2000收集奖励");
                check(Pilot.Rank(pilot.CardChoices([mixed, attack], method, inherited))[0].Card.At("id").S() == "attack",
                    method + " 已继承后不为重复冻结抬高小额附带伤害，正常输出优先");
                inherited["reliquias"]!.AsArray().Add(bounce.DeepClone()); inherited["llegada"]!["reliquias"] = 2;
                check(Cards.Priority(glacier, cfg, inherited, method) < Cards.Priority(fire, cfg, inherited, method),
                    method + " 冻结与同档五次弹跳都有后不为重复混合牌加权");
                check(pilot.CardChoices([freeze], method, inherited).Single().Label.Contains("兜底"),
                    method + " 已继承却被强制只给纯冻结时仍兜底继续，不因去重暂停");
                var blocked = freeze.DeepClone(); blocked["available"] = false;
                check(pilot.CardChoices([blocked, slow], method, Run(freeze)).Single().Card.At("id").S() == "es_helada",
                    method + " 多拿策略不会恢复游戏明确不可用的冻结牌");
            }
            cfg.PreferFreezeBuild = false; pilot.Save(cfg);
            check(Pilot.Rank(pilot.CardChoices([freeze, slow], "sima.reliquia", Run(freeze, freeze)))[0].Priority == 2000,
                "关闭继承后构筑排序也不解除继承前重复冻结优先");
            var boolean = Effects.Parse([freeze, freeze, freeze], cfg);
            check(boolean.Freeze && boolean.Damage == 0, "多拿仅改变收集偏好，模型不虚构冻结概率或叠加伤害");
            var pending = Run(freeze, freeze, freeze); pending["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(freeze.DeepClone()) };
            var selected = pilot.CardChoices([freeze], "sima.guarda", pending).Single();
            check(Cards.Project(pending, selected).Count(c => Cards.IsCore(c, cfg)) == 1,
                "本章多拿三份但每章只新增继承一份，不伪造全部带走");
        }
        finally { Paths.Data = original; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
