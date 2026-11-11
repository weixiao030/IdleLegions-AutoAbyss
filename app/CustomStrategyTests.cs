using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class CustomStrategyTests
{
    static JsonNode Card(string id, string family, string desc) => new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    static string Signature(Choice[] choices) => JsonSerializer.Serialize(Pilot.Rank(choices).Select(c => new { id = c.Card.At("id").S(), c.Method, c.Priority, c.Reason, c.Args }), J.Options);
    public static void Run(Action<bool, string> check)
    {
        var oldData = Paths.Data;
        var root = Path.Combine(oldData, "strategy4-isolated");
        try
        {
            Paths.Data = root;
            var cfg = new Settings { StrategyId = 4 };
            var pilot = new Pilot(); pilot.Save(cfg);
            var core = Card("es_carambano", "escarcha", "Tus autoataques CONGELAN al objetivo");
            var ice = Card("es_helada", "escarcha", "Tus golpes ralentizan un 15% durante 4 s");
            var bolt = Card("fu_custom_damage", "fulgor", "+10% de daño");
            var fire = Card("br_custom_damage", "brasa", "+30% de daño");
            var ready = new JsonObject { ["profundidad"] = 8, ["llegada"] = new JsonObject { ["reliquias"] = 1 }, ["reliquias"] = new JsonArray(core.DeepClone()), ["blasones"] = new JsonArray(), ["buffs"] = new JsonArray() };
            var opening = ready.DeepClone(); opening["llegada"]!["reliquias"] = 0;
            string Pick(JsonNode run, string method, params JsonNode[] cards) => Pilot.Rank(pilot.CardChoices(cards, method, run))[0].Card.At("id").S();
            bool Unavailable(Action action) { try { action(); return false; } catch (Exception e) { return e.Message.Contains("没有可执行的合法卡牌"); } }
            check(!CustomStrategy.Inherited(opening, cfg), "策略四本章新获得冻结遗物不能冒充已继承");
            var badgeOnly = ready.DeepClone(); badgeOnly["reliquias"] = new JsonArray(); badgeOnly["blasones"] = new JsonArray(core.DeepClone());
            check(!CustomStrategy.Inherited(badgeOnly, cfg), "策略四临时冻结徽章不能代替遗物继承");
            var incomplete = ready.DeepClone(); incomplete.AsObject().Remove("llegada");
            check(!CustomStrategy.Inherited(incomplete, cfg) && CustomStrategy.Inherited(ready, cfg), "策略四只认可到达回执中的遗物，缺少到达字段继续固定前置");
            cfg.CustomStrategy.Cards["es_carambano"] = new() { Mode = "block" };
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
            {
                check(Pick(opening, method, fire, ice, core) == "es_carambano", $"策略四{method}固定冰冻前置不受自定义拉黑覆盖");
                if (method != "sima.guarda") check(Pick(opening, method, bolt, ice) == "es_helada", $"策略四{method}尚未继承时寒冰优先");
                check(Pick(ready, method, fire, bolt) == "fu_custom_damage", $"策略四{method}确认继承后按系列排序");
            }
            var transitions = pilot.CardChoices([fire, ice], "sima.guarda", opening);
            check(transitions.Length == 2 && transitions.All(c => c.Reason.Contains("冰冻继承前过渡")), "策略四首次继承无普攻冰冻时择优继承其他遗物，正常继续");
            var transitionNext = opening.DeepClone(); transitionNext["profundidad"] = 9; transitionNext["llegada"]!["reliquias"] = 1; transitionNext["reliquias"] = new JsonArray(ice.DeepClone());
            check(Cards.InheritanceError(opening, transitionNext, ice, cfg) == null && !CustomStrategy.Inherited(transitionNext, cfg), "策略四普通冰过渡通过继承核验，但不冒充冰冻核心");
            check(Pick(transitionNext, "sima.reliquia", fire, core) == "es_carambano", "策略四过渡后下一层仍继续寻找普攻冰冻");
            var openingBefore = Signature(pilot.CardChoices([fire, ice], "sima.guarda", opening));
            cfg.CustomStrategy.Cards["es_helada"] = new() { Mode = "block" };
            cfg.CustomStrategy.Cards["br_custom_damage"] = new() { Mode = "avoid" };
            check(Signature(pilot.CardChoices([fire, ice], "sima.guarda", opening)) == openingBefore && Pick(opening, "sima.reliquia", fire, ice) == "es_helada", "策略四继承确认前固定规则及过渡不受自定义拉黑软避让影响");
            cfg.CustomStrategy.Cards.Remove("es_helada"); cfg.CustomStrategy.Cards.Remove("br_custom_damage");
            check(pilot.CardChoices([fire], "sima.trade", opening).Length == 0, "策略四前置阶段商店无冰牌不花钱购买");
            cfg.CustomStrategy.FamilyOrder = ["brasa", "fulgor", "ponzona", "umbra", "acero", "aurora", "escarcha"];
            check(Pick(ready, "sima.reliquia", bolt, fire) == "br_custom_damage", "策略四修改系列顺序立即改变自定义阶段选择");
            cfg.CustomStrategy.FamilyOrder = new CustomStrategySettings().FamilyOrder;
            var bounce = Card("fu_custom_bounce", "fulgor", "Cada 4 autoataques, tu golpe salta a todos los enemigos");
            cfg.CustomStrategy.Cards["fu_custom_damage"] = new() { Priority = 1 };
            cfg.CustomStrategy.Cards["fu_custom_bounce"] = new() { Priority = 999 };
            check(Pick(ready, "sima.reliquia", bolt, bounce) == "fu_custom_bounce", "策略四细分类优先级高于单卡优先级，不会跨类抢位");
            var categories = CardCatalog.Categories.ToList(); categories.Remove("attack"); categories.Insert(0, "attack"); cfg.CustomStrategy.CategoryOrder["fulgor"] = categories.ToArray();
            check(Pick(ready, "sima.reliquia", bounce, bolt) == "fu_custom_damage", "策略四系列内细分类顺序可独立调整");
            var bolt2 = Card("fu_custom_damage2", "fulgor", "+15% de daño");
            cfg.CustomStrategy.Cards["fu_custom_damage2"] = new() { Priority = 2 };
            check(Pick(ready, "sima.reliquia", bolt2, bolt) == "fu_custom_damage", "策略四同系列同细类内单卡数字越小越先选");
            cfg.CustomStrategy.Cards["fu_custom_damage"] = new() { Mode = "block" };
            check(Pick(ready, "sima.reliquia", bolt, fire) == "br_custom_damage", "策略四有正常候选时排除拉黑卡");
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda" })
                check(Pick(ready, method, bolt) == "fu_custom_damage" && pilot.CardChoices([bolt], method, ready)[0].Reason.Contains("拉黑末位兜底"), $"策略四{method}全拉黑时择优兜底自动继续");
            check(pilot.CardChoices([bolt], "sima.trade", ready).Length == 0, "策略四商店全拉黑时允许离店");
            cfg.CustomStrategy.Cards["fu_custom_damage"] = new() { Mode = "avoid" };
            check(Pick(ready, "sima.reliquia", bolt, fire) == "br_custom_damage" && pilot.CardChoices([bolt], "sima.reliquia", ready)[0].Reason.Contains("软避让兜底"), "策略四软避让只在没有正常候选的强制菜单兜底");
            check(pilot.CardChoices([bolt], "sima.trade", ready).Length == 0, "策略四商店不会花钱买软避让牌");
            cfg.CustomStrategy.Cards["br_custom_damage"] = new() { Mode = "block" };
            check(Pick(ready, "sima.reliquia", fire, bolt) == "fu_custom_damage", "策略四软避让候选优先于拉黑末位兜底");
            cfg.CustomStrategy.Cards.Remove("br_custom_damage");
            cfg.CustomStrategy.Cards["fu_custom_damage"] = new() { TargetCopies = 1 };
            ready["reliquias"]!.AsArray().Add(bolt.DeepClone());
            check(Pick(ready, "sima.reliquia", bolt, fire) == "br_custom_damage", "策略四达到目标张数软避让该遗物");
            check(Pick(ready, "sima.blason", bolt, fire) == "fu_custom_damage", "策略四临时徽章目标张数不被遗物张数占用");
            check(Pick(ready, "sima.guarda", bolt, fire) == "fu_custom_damage", "策略四继承目标只计算旧遗物，不把本章新牌误算已带走");
            cfg.CustomStrategy.Cards["fu_custom_damage"]!.TargetCopies = 0;
            check(Pick(ready, "sima.reliquia", bolt, fire) == "fu_custom_damage", "策略四目标零表示不限，同种遗物可继续获取");
            cfg.CustomStrategy.Cards.Clear();
            ready["reliquias"]!.AsArray().Add(bounce.DeepClone());
            check(Pick(ready, "sima.reliquia", bounce, fire) == "br_custom_damage", "策略四已有同档或更快弹跳时软避让重复效果");
            cfg.CustomStrategy.AvoidOwnedEffects = false;
            check(Pick(ready, "sima.reliquia", bounce, fire) == "fu_custom_bounce", "策略四关闭重复效果避让后按自定义排序");
            var capped = ready.DeepClone(); capped["llegada"]!["reliquias"] = 4; capped["reliquias"] = new JsonArray(core.DeepClone(), bolt.DeepClone(), bolt.DeepClone(), bolt.DeepClone());
            check(Pick(capped, "sima.guarda", bolt, fire) == "br_custom_damage", "策略四保留同种遗物跨章最多三张的游戏继承限制");
            var cappedOnly = pilot.CardChoices([bolt], "sima.guarda", capped);
            check(cappedOnly.Length == 1 && cappedOnly[0].Reason.Contains("继承上限过渡"), "策略四全部三张上限仍按游戏允许的选项正常推进");
            var cappedNext = capped.DeepClone(); cappedNext["profundidad"] = 9;
            check(Cards.InheritanceError(capped, cappedNext, bolt, cfg) == null && Cards.Project(capped, cappedOnly[0]).Length == 4, "策略四上限过渡不虚构新增继承张数，保留回执核验");
            var unavailable = bolt.DeepClone(); unavailable["available"] = false;
            check(Pick(ready, "sima.reliquia", unavailable, fire) == "br_custom_damage", "策略四不会为了兜底选择游戏声明不可用卡牌");
            check(Unavailable(() => pilot.CardChoices([unavailable], "sima.reliquia", ready)) && pilot.CardChoices([unavailable], "sima.trade", ready).Length == 0, "策略四真实不可用菜单保留状态保护，不能靠兜底选非法卡");
            var next = ready.DeepClone(); next["profundidad"] = 9;
            check(Cards.InheritanceError(opening, next, core, cfg) == null, "策略四冰冻继承可通过下一深度真实遗物回执核对");
            next["reliquias"] = new JsonArray(); next["blasones"] = new JsonArray(core.DeepClone());
            check(Cards.InheritanceError(opening, next, core, cfg) != null, "策略四下一深度缺少继承核心时保持回执保护");
            pilot.Save(cfg);
            check(new Pilot().Config.StrategyId == 4 && new Pilot().Config.CustomStrategy.AvoidOwnedEffects == false, "策略四嵌套配置保存和重开恢复");
            foreach (int strategy in new[] { 1, 2, 3 })
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
            foreach (bool hasInherited in new[] { false, true })
            {
                var existing = new Settings { StrategyId = strategy }; pilot.Save(existing);
                var run = hasInherited ? ready : opening;
                var offered = new[] { core, ice, fire, bolt, bounce };
                var before = Signature(pilot.CardChoices(offered, method, run));
                existing.CustomStrategy = new() { FamilyOrder = CardCatalog.Families.Reverse().ToArray(), AvoidOwnedEffects = false, CategoryOrder = CardCatalog.Families.ToDictionary(f => f, f => CardCatalog.Categories.Reverse().ToArray()), Cards = offered.ToDictionary(c => c.At("id").S(), c => new CardPreference { Priority = 999, TargetCopies = 1, Mode = "block" }) };
                pilot.Save(existing);
                check(Signature(pilot.CardChoices(offered, method, run)) == before, $"策略{strategy}隔离回归{method}继承{hasInherited}：自定义全拉黑及排序不改变候选、分数、原因和参数");
                existing.CustomStrategy = null!; existing.Validate();
                check(Signature(pilot.CardChoices(offered, method, run)) == before, $"策略{strategy}空自定义配置不影响原逻辑{method}继承{hasInherited}");
            }
            check(CardCatalog.Data.At("cards").A().All(c => GameText.IsChinese(c.At("name").S()) && GameText.IsChinese(c.At("description").S())), "卡图清单所有卡项含中文名称与效果");
            check(CardCatalog.Data.At("cards").A().Count() == 151 && CardCatalog.Data.At("cards").A().All(c => c.At("verified").B()) && CardCatalog.Data.At("coverage", "pending").N() == 0, "151项本地卡图清单中文与效果全部经真实菜单和游戏中文资源核实");
            foreach (var family in CardCatalog.Families)
            {
                var priorities = CardCatalog.Data.At("cards").A().Where(c => c.At("family").S() == family).Select(c => CardCatalog.DefaultPreference(c.At("id").S()).Priority).Order().ToArray();
                check(priorities.SequenceEqual(Enumerable.Range(1, priorities.Length)), $"策略四{family}默认单卡从1起连续编号");
            }
            check(CardCatalog.DefaultPreference("br_yesca").Mode == "block" && CardCatalog.DefaultPreference("fu_descarga").Mode == "block", "策略四默认读取1.3.1禁选参考，自定义仍可解除");
        }
        finally { Paths.Data = oldData; }
    }
}
