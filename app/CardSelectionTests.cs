using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class CardSelectionTests
{
    static JsonNode Card(string id, string family, string desc) => new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    static JsonObject Run(params JsonNode[] cards) => new() { ["reliquias"] = new JsonArray(cards.Select(c => c.DeepClone()).ToArray()) };
    public static void Run(Action<bool, string> check)
    {
        var cfg = new Settings { SkipShieldCardsEarly = false, Simulations = 0 };
        var legacy = JsonSerializer.Deserialize<Settings>("{\"avoidFireLightning\":true}", J.Options)!;
        check(cfg.AllowBasicBurn && !cfg.AllowBasicLightning && legacy.AllowBasicBurn && !legacy.AllowBasicLightning,
            "新旧配置均为普攻点燃默认允许、普攻带电默认禁止，不受旧回避开关绑架");
        var freeze = Card("es_carambano", "escarcha", "Tus autoataques CONGELAN al objetivo");
        var glacier = Card("es_glaciar", "escarcha", "Tus autos CONGELAN, y cada 5 tu golpe salta a todos");
        var burn = Card("br_yesca", "brasa", "Tus autoataques dejan al objetivo ARDIENDO");
        var burnDamage = Card("br_incendio", "brasa", "Tus autoataques dejan al objetivo ARDIENDO, y +4% de daño");
        var shock = Card("fu_descarga", "fulgor", "Tus autoataques ELECTRIFICAN al objetivo, y +3% de daño");
        var detonate = Card("br_detonador", "brasa", "Tus autoataques CONSUMEN el estado del objetivo y fuerzan su reacción");
        var slow = Card("es_helada", "escarcha", "Tus golpes ralentizan un 15% durante 4 s");
        var iceDamage = Card("es_ventisca", "escarcha", "+22% de daño contra enemigos LENTOS");
        var iceBounce = Card("es_esquirla", "escarcha", "Cada 16 autoataques, tu golpe salta a todos, y +5% de defensa");
        var fireDamage = Card("br_ceniza", "brasa", "+22% de daño contra lo que lleve un estado encima, y +2% de daño");
        var reactions = Card("br_fragua", "brasa", "+50% de daño de tus reacciones elementales, y +5% de daño");
        var bounce = Card("fu_cadena", "fulgor", "Cada 5 autoataques, tu golpe salta a todos, y +2% de daño");
        var otherDamage = Card("po_miasma", "ponzona", "+40% de daño contra lo que lleve un estado encima");
        var attack = Card("attack", "acero", "+20% de daño");
        var defense = Card("defense", "aurora", "+25% de defensa");
        var shield = Card("au_alba", "aurora", "Empiezas cada sala con un escudo del 25% de tu vida durante 3 s");
        var inherited = Run(freeze);
        int Priority(JsonNode c, JsonNode? run = null) => Cards.Priority(c, cfg, run ?? inherited);
        check(Priority(bounce) > Priority(burn) && Priority(burn) > Priority(detonate) && Priority(detonate) > Priority(fireDamage)
            && Priority(fireDamage) > Priority(otherDamage) && Priority(otherDamage) > Priority(attack)
            && Priority(attack) > Priority(shield) && Priority(shield) > Priority(defense),
            "实际描述排序：冻结后弹跳、一次普攻独立效果、引爆器、火增伤、其他增伤、普通输出、少量防御");
        check(Priority(reactions) > Priority(otherDamage), "火系反应增伤按火增伤优先，未被普攻开关误伤");
        foreach (bool fire in new[] { false, true }) foreach (bool electric in new[] { false, true })
        {
            cfg.AllowBasicBurn = fire; cfg.AllowBasicLightning = electric;
            check(Cards.Rate(burn, cfg).Allowed == fire && Cards.Rate(shock, cfg).Allowed == electric
                && Cards.Rate(fireDamage, cfg).Allowed && Cards.Rate(reactions, cfg).Allowed && Cards.Rate(bounce, cfg).Allowed,
                $"开关组合 火={fire} 电={electric} 只控制普攻状态，火增伤与电弹跳始终保留");
        }
        cfg.AllowBasicBurn = true; cfg.AllowBasicLightning = false;
        var multilingual = new[] {
            Card("future-fire", "brasa", "Basic attacks apply burning"),
            new JsonObject { ["id"] = "future-fire-zh", ["descZh"] = "普攻使敌人燃烧" },
            Card("br_pira", "brasa", ""),
            Card("future-electric", "fulgor", "Basic attacks shock the target"),
            new JsonObject { ["id"] = "future-electric-zh", ["descZh"] = "普通攻击使目标带电" }
        };
        cfg.AllowBasicBurn = false;
        check(multilingual.All(c => !Cards.Rate(c, cfg).Allowed), "真实ID回退及中英效果描述都执行对应普攻开关");
        check(Cards.Rate(Card("conditional-fire", "brasa", "Tus autoataques hacen más daño contra objetivos ARDIENDO"), cfg).Allowed,
            "对燃烧目标的普攻增伤不误识别成普攻施加点燃");
        cfg.AllowBasicBurn = true;
        var sameFloor = Run(freeze); sameFloor["llegada"] = null;
        var afterArrival = Run(freeze); afterArrival["llegada"] = new JsonObject { ["reliquias"] = 1 };
        check(!CardRules.HasInheritedCore(sameFloor, cfg) && CardRules.HasInheritedCore(afterArrival, cfg),
            "利用真实llegada.reliquias区分本层拿到冻结与已经继承冻结");
        check(Priority(slow, Run()) == 1950 && Priority(bounce, Run()) == -10000 && Priority(fireDamage, Run()) == -10000
            && Priority(slow, sameFloor) == 1950 && Priority(bounce, sameFloor) == -10000
            && Priority(slow, Run()) < Priority(freeze, Run()),
            "冻结继承前冰减速高于非冰弹跳火增伤，本章取得冻结仍不解除仅冰阶段");
        check(Priority(detonate, afterArrival) > Priority(detonate, sameFloor), "引爆器在冻结继承后才获得点燃搭配加权");
        check(CardRules.AvoidIceAfterCore(slow, afterArrival, cfg) && CardRules.AvoidIceAfterCore(iceDamage, afterArrival, cfg)
            && !CardRules.AvoidIceAfterCore(iceBounce, afterArrival, cfg), "冻结继承后避开冰减速和冰伤，冰全体弹跳保留");
        check(CardRules.SkipReason(burnDamage, Run(freeze, burn), cfg, "sima.reliquia") == null
            && Priority(burnDamage, Run(freeze, burn)) == 1200
            && Priority(burnDamage, Run(freeze, burn)) < Priority(attack, Run(freeze, burn)),
            "点燃已有时烈焰附带4%伤害仍可选，按1200降权且不压过正常攻击");
        check(CardRules.SkipReason(glacier, inherited, cfg, "sima.reliquia") == null,
            "已有普通冻结仍允许提供新增全体弹跳的传说核心");
        check(CardRules.SkipReason(bounce, Run(freeze, bounce), cfg, "sima.reliquia") == null
            && Priority(bounce, Run(freeze, bounce)) < Priority(fireDamage, Run(freeze, bounce))
            && Priority(attack, Run(freeze, attack)) < Priority(otherDamage, Run(freeze, attack)),
            "弹跳和数值牌允许有收益时重复，但重复优先级低于补不同增伤");
        check(CardRules.SkipReason(bounce, Run(freeze, bounce, bounce, bounce), cfg, "sima.reliquia") == null
            && CardRules.SkipReason(bounce, Run(freeze, bounce, bounce, bounce), cfg, "sima.guarda") != null,
            "三张上限只约束跨层继承，本层获取仍可比较附带收益");
        var pending = Run(freeze, fireDamage); pending["llegada"] = null;
        pending["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(freeze.DeepClone(), fireDamage.DeepClone()) };
        check(!CardRules.HasInheritedCore(pending, cfg) && Cards.Priority(freeze, cfg, pending, "sima.guarda")
            > Cards.Priority(fireDamage, cfg, pending, "sima.guarda"), "本层冻结尚未继承时，唯一继承名额必须先给冻结，不能被火增伤挤走");
        pending["guardaPendiente"]!["cartas"]![0]!["llevas"] = 1;
        pending.AsObject().Remove("llegada"); // Legacy snapshot uses llevas when arrival metadata is absent.
        check(CardRules.HasInheritedCore(pending, cfg) && CardRules.SkipReason(freeze, pending, cfg, "sima.guarda") != null,
            "冻结已继承时通过llevas识别，不再重复占用唯一名额");
        var temporary = Run(); temporary["blasones"] = new JsonArray(freeze.DeepClone());
        check(!CardRules.HasInheritedCore(temporary, cfg) && CardRules.SkipReason(freeze, temporary, cfg, "sima.reliquia") == null,
            "临时冻结徽章不阻止获取可继承冻结遗物");
        var iceShield = Card("fixture_ice_shield", "escarcha", "Empiezas cada sala con un escudo del 10% de tu vida durante 3 s");
        check(!CardRules.AvoidIceAfterCore(iceShield, afterArrival, cfg)
            && Priority(iceShield, afterArrival) < Priority(attack, afterArrival),
            "继承冻结后纯冰盾仍可少量补充，不将整个冰系禁用，也不抢输出");
        check(Priority(bounce, afterArrival) > Priority(iceBounce, afterArrival)
            && Priority(iceBounce, afterArrival) > Priority(fireDamage, afterArrival),
            "全体弹跳同档优先更短触发间隔，5次优于16次，首次弹跳优先于增伤");
        check(Effects.Parse([Card("fixture_deepen", "umbra", "Marcas más hondo: +4% de daño recibido por marca, hasta 3")], cfg).Damage == 0,
            "标记加深的承伤增幅不误算成无条件攻击增伤");
        var omitted = Run(freeze, fireDamage);
        omitted["llegada"] = new JsonObject { ["reliquias"] = 1 };
        omitted["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(bounce.DeepClone()) };
        check(Cards.Inherited(omitted).Select(c => c.At("id").S()).SequenceEqual(["es_carambano"]),
            "入章遗物数量优先于候选缺席推断，本章卡缺席继承菜单也不冒充旧继承");
        foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.trade", "sima.guarda" })
        {
            check(CardRules.SkipReason(detonate, Run(freeze, detonate), cfg, method)?.Contains("引爆器") == true,
                method + " 引爆器只需一份，已持有/继承后正常避让");
            var detonateVariant = Card("fixture_detonator_variant", "brasa", "Tus autoataques CONSUMEN el estado del objetivo y +20% de daño y cada 5 autoataques, tu golpe salta a todos");
            check(CardRules.SkipReason(detonateVariant, Run(freeze, detonate), cfg, method)?.Contains("引爆器") == true,
                method + " 引爆器不同ID或附带数值弹跳也不诱导重复继承");
            check(CardRules.SkipReason(detonate, afterArrival, cfg, method) == null, method + " 尚未持有引爆器仍可选首份");
        }
        var newPoisonBounce = Card("po_plaga", "ponzona", "Tus autos ENVENENAN y cada 5 saltan a todos los enemigos en contacto");
        check(Priority(newPoisonBounce, Run(freeze, bounce)) == 1900,
            "混合牌新增独立普攻状态仍获1900，不因附带已有弹跳被误降权");
        var referenceFlame = Card("fixture_reference_flame", "brasa", "+6% de daño de tus reacciones elementales, +4% de daño contra lo que lleve un estado encima y +10% de daño crítico");
        var referenceBounce20 = Card("fixture_reference_bounce20", "brasa", "Cada 20 autoataques, tu golpe salta a todos, y +4% de daño");
        var reference = Run(freeze, bounce, burn, detonate, referenceBounce20, referenceFlame);
        var parsedReference = Effects.Parse(reference.At("reliquias").A(), cfg);
        check(parsedReference is { Freeze: true, Burn: true, Detonate: true, Cleave: 5 }
            && Math.Abs(parsedReference.ReactionDamage - .06) < 1e-8 && Math.Abs(parsedReference.CritDamage - .1) < 1e-8,
            "截图仅作本地参考：独立冻结点燃引爆布尔化，20次弹跳不覆盖5次，火反应和暴伤分开建模");
        check(Priority(referenceBounce20, reference) == 1275 && Priority(referenceBounce20, reference) < Priority(otherDamage, reference)
            && Priority(referenceFlame, reference) == 1450 && Priority(fireDamage, reference) == 1800,
            "参考构筑不照抄重复牌：已有20次火弹跳减350加175，重复火数值减350，优先补不同增伤");
        check(!Effects.Parse([detonate], cfg).Unknown.Contains("br_detonador")
            && !Effects.Parse([burn], cfg).Unknown.Contains("br_yesca")
            && !Effects.Parse([newPoisonBounce], cfg).Unknown.Contains("po_plaga"),
            "已解析的纯引爆点燃及毒弹跳不误报为未知效果");
        check(Priority(freeze, Run()) == 2000 && Priority(glacier, Run()) == 2200 && Priority(bounce) == 1945
            && Priority(iceBounce) == 1934 && Priority(burn) == 1900 && Priority(detonate) == 1850
            && Priority(fireDamage) == 1800 && Priority(otherDamage) == 1450 && Priority(attack) == 1350
            && Priority(shield) == 1000 && Priority(defense) == 100, "最终权重逐项固定，与文档和对用户说明一致");
        var original = Paths.Data;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-CardSelection-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = temp; var pilot = new Pilot(); pilot.Save(cfg);
            cfg.AllowBasicBurn = false; cfg.AllowBasicLightning = true; pilot.Save(cfg);
            var loaded = new Pilot().Config;
            check(!loaded.AllowBasicBurn && loaded.AllowBasicLightning, "独立开关反向组合保存重载，不捆绑旧开关");
            cfg.PreferFreezeBuild = false; cfg.AvoidFireLightning = false; pilot.Save(cfg);
            check(!pilot.CardChoices([burn, shock, freeze], "sima.reliquia", Run()).Any(c => c.Card.At("id").S() == "br_yesca"),
                "关闭构筑偏好与旧状态回避也不会绕过玩家关闭的普攻点燃");
        }
        finally { Paths.Data = original; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
