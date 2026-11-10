using System.Text.Json.Nodes;

namespace FrostAbyss;

static class StrategyTests
{
    static JsonNode Card(string id, string family, string desc) => new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    public static void Run(Action<bool, string> check)
    {
        var cfg = new Settings { SkipShieldCardsEarly = false };
        var core = Card("es_carambano", "escarcha", "Tus autoataques CONGELAN al objetivo");
        var glacier = Card("es_glaciar", "escarcha", "Tus autos CONGELAN, y cada 5 tu golpe salta a todos");
        var slow = Card("es_helada", "escarcha", "Tus golpes ralentizan un 15% durante 4 s");
        var vsSlow = Card("es_ventisca", "escarcha", "+22% de daño contra enemigos LENTOS");
        var poison = Card("po_miasma", "ponzona", "+40% de daño contra lo que lleve un estado encima");
        var fireDamage = Card("br_ceniza", "brasa", "+22% de daño contra lo que lleve un estado encima, y +2% de daño");
        var lightning = Card("fu_cadena", "fulgor", "Cada 5 autoataques, tu golpe salta a todos, y +2% de daño");
        var plague = Card("po_plaga", "ponzona", "Tus autos ENVENENAN y cada 5 saltan a todos los enemigos en contacto");
        var crit = Card("um_filo", "umbra", "+6 puntos de crítico y +20% de daño crítico");
        var shield = Card("au_alba", "aurora", "Empiezas cada sala con un escudo del 25% de tu vida durante 3 s");
        var mark = Card("um_estigma", "umbra", "Tus golpes MARCAN: +3% de daño recibido por marca, hasta 4");
        var deepen = Card("um_marca_honda", "umbra", "Marcas más hondo: +4% de daño recibido por marca, hasta 3");
        var run = new JsonObject { ["reliquias"] = new JsonArray(core.DeepClone()) };
        int P(JsonNode c, string method = "sima.reliquia") => Cards.Priority(c, cfg, run, method);
        check(Cards.Priority(glacier, cfg) > Cards.Priority(core, cfg), "真实传说冻结加五次弹跳优先于普通冻结核心");
        check(P(glacier) > P(shield), "已有普通核心仍优先升级传说冻结弹跳核心");
        run["reliquias"]!.AsArray().Add(lightning.DeepClone());
        check(P(glacier)<P(crit),"冻结和五次弹跳已有，不再优先追传说冻结升级");
        run["reliquias"]!.AsArray().RemoveAt(1);
        check(P(shield) > P(slow) && P(shield) < P(crit), "已有核心后首盾仅作补充，低于输出，高于多余冰减速");
        check(P(lightning) > P(fireDamage) && P(fireDamage) > P(poison) && P(poison) > P(crit) && P(crit) > P(slow), "继承冻结后依次优先全体弹跳、火系增伤、其他增伤和普通输出，冰减速降序");
        check(P(fireDamage) > P(crit) && P(lightning) > P(crit), "火系纯状态增伤与电系弹跳不再被家族回避误伤");
        check(Effects.Parse([plague], cfg).Cleave == 5 && Effects.Parse([plague], cfg).Poison, "识别真实毒系传说五次弹跳的复数描述");
        check(Math.Abs(Effects.Parse([Card("po_entumecer", "ponzona", "Tus golpes dejan al objetivo un 10% más lento durante 3 s")], cfg).Slow - .1) < 1e-8, "识别真实更慢百分比描述");
        check(Effects.Parse([shield], cfg).Shield == .25 && Effects.Parse([Card("au6_vigilia", "aurora", "Escudo del 5% los 3 primeros segundos de cada sala")], cfg).Shield == .05, "晨昏普通及徽章开局护盾描述均可解析");
        run["reliquias"]!.AsArray().Add(shield.DeepClone());
        check(P(shield) < P(poison) && P(shield) < P(crit), "已有护盾可继续补高级盾，但不压过异常增伤和双暴");
        run["reliquias"] = new JsonArray(core.DeepClone());
        run["blasones"] = new JsonArray(shield.DeepClone());
        check(P(shield) > P(slow), "本层护盾徽章不能冒充可继承护盾");
        check(P(mark) > P(deepen) && P(deepen) < P(crit), "没有标记来源时先标记，标记加深不空占高优先级");
        run["reliquias"]!.AsArray().Add(mark.DeepClone());
        check(P(deepen) > P(crit), "有标记来源后标记加深优先于双暴补充");
        check(Effects.Parse([mark, deepen], cfg) is { Mark: > 0, MarkDeepen: > 0, MarkCap: 4 }, "标记来源、加深和上限进入组合模型");
        var burn = Card("br_yesca", "brasa", "Tus autoataques dejan al objetivo ARDIENDO");
        var detonate = Card("br_detonador", "brasa", "Tus autoataques CONSUMEN el estado del objetivo y fuerzan su reacción");
        check(Cards.Rate(burn,cfg).Allowed && P(detonate) > P(fireDamage), "默认允许点燃，继承冻结后引爆器提高优先级");
        cfg.AllowBasicBurn = false;
        check(!Cards.Rate(burn,cfg).Allowed && P(detonate) < 0 && P(fireDamage) > 0, "只关闭普攻点燃，引爆器不再加权，火系纯增伤仍可选");
        cfg.AllowBasicBurn = true;
        run["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(shield.DeepClone()) };
        check(P(shield, "sima.guarda") < P(poison, "sima.guarda"), "继承已有冻结后，增伤优先于首次补盾");
        run["guardaPendiente"]!["cartas"]![0]!["llevas"] = 1;
        check(P(shield, "sima.guarda") < P(poison, "sima.guarda"), "已有继承盾后优先异常增伤，高级盾仍为合法补充");

        var iceOriginalData = Paths.Data;
        var iceTestData = Path.Combine(Path.GetTempPath(), "FrostAbyss-Ice-" + Guid.NewGuid().ToString("N"));
        try
        {
        Paths.Data = iceTestData;
        var iceCfg = new Settings { StrategyId = 3, SkipShieldCardsEarly = true, ShieldSkipFloors = 7 };
        var bounce4 = Card("fu_bounce4", "fulgor", "Cada 4 autoataques, tu golpe salta a todos los enemigos");
        var bounce5 = Card("po_bounce5", "ponzona", "Cada 5 autoataques, tu golpe salta a todos los enemigos");
        var bounce6 = Card("um_bounce6", "umbra", "Cada 6 autoataques, tu golpe salta a todos los enemigos");
        var iceRun = new JsonObject { ["profundidad"] = 8, ["reliquias"] = new JsonArray(core.DeepClone()) };
        var icePilot = new Pilot(); icePilot.Save(iceCfg);
        Choice IcePick(params JsonNode[] cards) => Pilot.Rank(icePilot.CardChoices(cards, "sima.reliquia", iceRun))[0];
        check(IcePick(bounce6, bounce5, bounce4).Card!.At("id").S() == "fu_bounce4", "ICE先补齐缺少的4次全体弹跳档");
        iceRun["reliquias"]!.AsArray().Add(bounce4.DeepClone());
        check(IcePick(bounce6, bounce5).Card!.At("id").S() == "po_bounce5", "已有4次弹跳后ICE补5次档，不误认为后续档已齐");
        iceRun["reliquias"]!.AsArray().Add(bounce5.DeepClone());
        check(IcePick(bounce6).Card!.At("id").S() == "um_bounce6", "4与5次档完成后ICE补6次档");
        iceRun["reliquias"]!.AsArray().Add(bounce6.DeepClone());
        var crit8 = Card("um_crit8", "umbra", "Cada 8 ataques, el siguiente es crítico garantizado");
        var crit10 = Card("fu_punteria", "fulgor", "Crítico cada 10 golpes");
        check(IceStrategyRules.GuaranteedCritEvery(crit8) == 8 && IceStrategyRules.GuaranteedCritEvery(crit10) == 10, "ICE识别8次攻击与fu_punteria每10次必暴击文案");
        check(IcePick(crit10, crit8).Card!.At("id").S() == "um_crit8", "ICE弹跳齐备后优先补8次必暴击");
        iceRun["reliquias"]!.AsArray().Add(crit8.DeepClone());
        check(IcePick(crit8, crit10).Card!.At("id").S() == "fu_punteria", "已有8次必暴击仍补10次档，不重复抢同档");
        var slowAfterCore = Card("es_slow_ice", "escarcha", "Tus golpes ralentizan un 15% durante 4 s");
        var series7 = Enumerable.Range(0, 6).Select(i => Card("um_series_" + i, "umbra", "+1% de daño")).ToArray();
        iceRun["reliquias"] = new JsonArray([core.DeepClone(), bounce4.DeepClone(), bounce5.DeepClone(), bounce6.DeepClone(), .. series7.Select(c => c.DeepClone())]);
        var otherFamily = Card("au_series_next", "aurora", "+1% de daño");
        var readyFamily = Card("um_series_next", "umbra", "+1% de daño");
        check(icePilot.CardChoices([slowAfterCore], "sima.reliquia", iceRun).Any(c => c.Card!.At("id").S() == "es_slow_ice"), "ICE继承冻结后仍保留冰减速牌候选");
        check(IcePick(readyFamily, otherFamily).Card!.At("id").S() == "au_series_next", "系列目标逐系计算：7张暗影后优先补0张的光耀系列");
        iceRun["reliquias"]!.AsArray().Add(otherFamily.DeepClone());
        for (int i = 1; i < 8; i++) iceRun["reliquias"]!.AsArray().Add(Card("au_series_" + i, "aurora", "+1% de daño"));
        check(System.Text.Json.JsonSerializer.SerializeToNode(IceStrategyRules.Progress(iceRun, iceCfg), J.Options)!.AsArray().All(x => x.At("target").N() == 8), "ICE各元素系列目标分别显示为8张");
        var progressBeforeBadge = System.Text.Json.JsonSerializer.SerializeToNode(IceStrategyRules.Progress(iceRun, iceCfg), J.Options)!.AsArray();
        iceRun["blasones"] = new JsonArray(Card("au2_badge", "aurora", "+10% de daño"));
        var progressAfterBadge = System.Text.Json.JsonSerializer.SerializeToNode(IceStrategyRules.Progress(iceRun, iceCfg), J.Options)!.AsArray();
        check(progressAfterBadge.ToJsonString() == progressBeforeBadge.ToJsonString(), "系列八张计数只统计遗物，临时徽章不补张数");
        check(IcePick(otherFamily, readyFamily).Card!.At("id").S() == "um_series_next", "光耀8张后不再抢系列名额，继续补未满8张的暗影");
        var inheritedSeries = new JsonObject { ["profundidad"] = 8, ["reliquias"] = new JsonArray([core.DeepClone(), .. Enumerable.Range(0, 8).Select(i => Card("au_held_" + i, "aurora", "+1% de daño")), readyFamily.DeepClone()]), ["llegada"] = new JsonObject { ["reliquias"] = 9 } };
        check(Pilot.Rank(icePilot.CardChoices([otherFamily, readyFamily], "sima.guarda", inheritedSeries))[0].Card!.At("id").S() == "um_series_next", "继承只按真正旧遗物计算系列进度，不把本章全部新增卡当已继承");
        var lowLifeShadow = Card("um_low_life", "umbra", "Si tu vida está por debajo del 25%, +40% de daño");
        iceRun["reliquias"] = new JsonArray([core.DeepClone(), bounce4.DeepClone(), bounce5.DeepClone(), bounce6.DeepClone(), crit8.DeepClone(), crit10.DeepClone(), lowLifeShadow.DeepClone()]);
        var repeatLowLife = Card("um_low_life_repeat", "umbra", "Si tu vida está por debajo del 25%, +40% de daño");
        check(IcePick(repeatLowLife, otherFamily).Card!.At("id").S() == "au_series_next", "暗影低血增伤满足一次后停止无限压过系列补齐");
        var heldDeepen = Card("um_deepen_held", "umbra", "Marcas más hondo: +2% de daño recibido por marca, hasta 5");
        iceRun["reliquias"]!.AsArray().Add(heldDeepen.DeepClone());
        var repeatDeepen = Card("um_deepen_repeat", "umbra", "Marcas más hondo: +2% de daño recibido por marca, hasta 5");
        check(IcePick(repeatDeepen, otherFamily).Card!.At("id").S() == "au_series_next", "暗影标记加深满足一次后不再无限重复争抢系列名额");
        iceRun["reliquias"] = new JsonArray([core.DeepClone(), .. Enumerable.Repeat(Card("br_stack", "brasa", "+1% de daño"), 3).Select(c => c.DeepClone())]);
        var fourthSameRelic = Card("br_stack", "brasa", "+1% de daño");
        check(icePilot.CardChoices([fourthSameRelic, otherFamily], "sima.reliquia", iceRun)
                .Any(c => c.Card!.At("id").S() == "br_stack" && !c.Label.Contains("兜底")),
            "同种遗物超过3张仍允许本层继续获取，继承上限不误当成获取上限");
        var cappedInheritance = Card("br_capped", "brasa", "+1% de daño"); cappedInheritance["llevas"] = 3;
        var openInheritance = Card("au_open", "aurora", "+1% de daño"); openInheritance["llevas"] = 0;
        var carryRun = new JsonObject { ["profundidad"] = 8, ["reliquias"] = new JsonArray(core.DeepClone(), cappedInheritance.DeepClone()),
            ["llegada"] = new JsonObject { ["reliquias"] = 2 }, ["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(cappedInheritance.DeepClone(), openInheritance.DeepClone()) } };
        check(IceStrategyRules.SkipReason(cappedInheritance, carryRun, iceCfg, "sima.guarda") != null
                && IceStrategyRules.SkipReason(cappedInheritance, carryRun, iceCfg, "sima.reliquia") == null,
            "三层限制只用于继承选择，不误伤本层普通遗物选择");
        // Screenshot numbers mean left-to-right. IDs below are local fixtures;
        // selection must work from Chinese or Spanish effects even for new IDs.
        var screenshotEffects = new (string Family, string Zh, string Es)[][]
        {
            [("umbra", "标记更深：每层 +2%，最多5层，持续时间翻倍", "Marcas más hondo: +2% de daño recibido por marca, hasta 5; duran el doble"),
             ("umbra", "生命低于25%时伤害+40%", "Si tu vida está por debajo del 25%, +40% de daño"),
             ("umbra", "击杀后2秒无敌，下一击必定暴击；伤害+3%", "Matar te deja intocable 2 s, el siguiente golpe es crítico y +3% de daño")],
            [("brasa", "你的普攻使目标陷入燃烧", "Tus autoataques dejan al objetivo ARDIENDO"),
             ("brasa", "元素反应伤害+25%，伤害+5%", "+25% de daño de tus reacciones y +5% de daño"),
             ("brasa", "对带有状态的目标伤害+20%，伤害+2%", "+20% de daño contra lo que lleve un estado encima y +2% de daño")],
            [("umbra", "你的攻击施加印记，每层受到伤害+2%，最多3层；对被标记目标伤害+3%", "Tus golpes MARCAN: +2% de daño recibido por marca, hasta 3; pegas +3% a lo marcado"),
             ("ponzona", "对带有状态的目标伤害+40%", "+40% de daño contra lo que lleve un estado encima"),
             ("brasa", "你的普攻附带燃烧，反应伤害+15%，伤害+2%", "Tus autos queman; +15% de daño de tus reacciones y +2% de daño")],
            [("aurora", "生命低于40%时，防御提升30%并清除自身状态", "Por debajo del 40%, tu defensa sube un 30% y limpias tus estados"),
             ("fulgor", "你每8次攻击必定暴击", "Cada 8 ataques tu golpe es crítico"),
             ("escarcha", "每承受一次攻击防御+4%，最多+60%，并反击两次", "Cada golpe que recibes te suma 4% de defensa hasta +60%, y contraatacas dos veces")],
            [("ponzona", "对生命低于30%的目标伤害+25%", "+25% de daño contra objetivos por debajo del 30% de vida"),
             ("brasa", "对带有状态的目标伤害+22%，伤害+2%", "+22% de daño contra lo que lleve un estado encima y +2% de daño"),
             ("escarcha", "每承受一次攻击防御+4%，最多+60%，并反击两次", "Cada golpe que recibes te suma 4% de defensa hasta +60%, y contraatacas dos veces")],
            [("ponzona", "你的普攻使目标中毒", "Tus autoataques ENVENENAN"),
             ("ponzona", "对带有状态的目标伤害+20%，伤害+2%", "+20% de daño contra lo que lleve un estado encima y +2% de daño"),
             ("ponzona", "你的攻击使目标减速10%，持续3秒", "Tus golpes dejan al objetivo un 10% más lento durante 3 s")]
        };
        int[] primary = [1, 1, 1, 1, 0, 1];
        int[] secondary = [0, 2, -1, 2, 1, 2];
        int[][] permutations = [[0,1,2],[0,2,1],[1,0,2],[1,2,0],[2,0,1],[2,1,0]];
        foreach (var language in new[] { "zh", "es" })
        for (int image = 0; image < screenshotEffects.Length; image++)
        foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade" })
        foreach (bool full in new[] { false, true })
        {
            var held = IceStrategyRules.Families.SelectMany(f => Enumerable.Range(0, full ? 8 : 1)
                .Select(i => Card($"held_{f}_{i}", f, "+1% de daño"))).Prepend(core.DeepClone()).ToArray();
            iceRun["reliquias"] = new JsonArray(held);
            iceRun.AsObject().Remove("llegada"); iceRun.AsObject().Remove("guardaPendiente");
            var menu = screenshotEffects[image].Select((x,i) =>
            {
                var c = Card($"screenshot_{image}_{i}_{language}", x.Family, language == "es" ? x.Es : "");
                if (language == "zh") c["descZh"] = x.Zh;
                return c;
            }).ToArray();
            foreach (var order in permutations)
            {
                var options = order.Select(i => menu[i]).ToArray();
                var screenshotPick = Pilot.Rank(icePilot.CardChoices(options, method, iceRun))[0];
                check(screenshotPick.Card!.At("id").S() == menu[primary[image]].At("id").S(),
                    $"ICE图{image+1} {language} {method} 系列满{full} 换位{string.Join(',',order)}首选按效果");
                if (secondary[image] >= 0)
                {
                    var fallbackOptions = options.Where(c => c != menu[primary[image]]).ToArray();
                    var fallbackPick = Pilot.Rank(icePilot.CardChoices(fallbackOptions, method, iceRun))[0];
                    check(fallbackPick.Card!.At("id").S() == menu[secondary[image]].At("id").S(),
                        $"ICE图{image+1} {language} {method} 系列满{full} 换位{string.Join(',',order)}首选缺席选次选");
                }
            }
            menu[primary[image]]["disponible"] = false;
            if (secondary[image] >= 0)
                check(Pilot.Rank(icePilot.CardChoices(menu, method, iceRun))[0].Card!.At("id").S()
                    == menu[secondary[image]].At("id").S(), $"ICE图{image+1}首选不可用时选次选");
        }
        iceRun["reliquias"] = new JsonArray(core.DeepClone());
        check(IcePick(poison, bounce4).Card!.At("id").S() == bounce4.At("id").S(), "截图偏好不压过缺少的全体弹跳");
        check(IcePick(poison, crit10).Card!.At("id").S() == crit10.At("id").S(), "截图偏好不压过缺少的10次必暴击");
        iceRun["reliquias"] = new JsonArray();
        check(IcePick(poison, core).Core, "截图偏好不压过尚未继承的普攻冻结");
        var badgeNoFreeze = Pilot.Rank(icePilot.CardChoices([
            Card("badge_low", "umbra", "Si tu vida está por debajo del 25%, +40% de daño"),
            Card("badge_deepen", "umbra", "Marcas más hondo: +2% de daño recibido por marca, hasta 5")], "sima.blason", iceRun))[0];
        check(badgeNoFreeze.Card!.At("id").S() == "badge_low", "冻结尚未继承且仅有暗影徽章时仍按低血优先兜底");

        icePilot.Save(new Settings { StrategyId = 2, SkipShieldCardsEarly = false });
        var v131Run = new JsonObject { ["profundidad"] = 8, ["reliquias"] = new JsonArray(core.DeepClone()) };
        check(Pilot.Rank(icePilot.CardChoices([crit, slow, shield], "sima.reliquia", v131Run))[0].Card!.At("id").S() == "au_alba", "策略二保留v1.3.1已有冻结后首张护盾优先顺序");

        }
        finally { Paths.Data = iceOriginalData; if (Directory.Exists(iceTestData)) Directory.Delete(iceTestData, true); }

        check(cfg.AvoidEliteIds.SequenceEqual(["sg_alferez"]) && EliteRules.Catalog.Length == 22, "精英多选默认陪葬旗官，清单包含22种已核对精英");
        cfg.AvoidEliteIds = []; // Explicit empty-list case; new default is tested above.
        var allElites = new Settings { AvoidEliteIds = EliteRules.Catalog.Select(e => e.Id).ToArray() }; allElites.Validate();
        check(EliteRules.Catalog.All(e => EliteRules.Avoided(new JsonObject { ["enemigos"] = new JsonArray(new JsonObject { ["char"] = e.Id }) }, allElites).Single().Id == e.Id), "资料站全部22种精英的真实char逐项匹配通过，不仅陪葬旗官");
        check(EliteRules.Catalog.All(e => EliteRules.Avoided(new JsonObject { ["planta"] = e.Hall == 10 ? 0 : e.Hall, ["enemigos"] = new JsonArray(new JsonObject { ["variant"] = e.Variant }) }, allElites).Single().Id == e.Id), "22种精英的大厅变体回退逐项匹配通过，包含两种伏击精英");
        var avoid = new Settings { AvoidEliteIds = ["sg_alferez"] }; avoid.Validate();
        var preview = JsonNode.Parse("""{"planta":0,"enemigos":[{"char":"sg_alferez","variant":3,"name":"Alférez Sepulcral"},{"char":"sg_centinela","variant":0}]}""")!;
        check(EliteRules.Avoided(preview, cfg).Length == 0 && EliteRules.Avoided(preview, avoid).Single().Name == "陪葬旗官", "空选不避让，选中陪葬旗官按真实char匹配而非泛化房间标签");
        preview["enemigos"]![0]!.AsObject().Remove("char");
        check(EliteRules.Avoided(preview, avoid).Length == 1, "旧预览无char时按客户端planta加variant定位精英");
        preview.AsObject().Remove("planta");
        check(EliteRules.Avoided(preview, avoid).Length == 0, "预览身份不完整时不猜测精英类型");
        bool rejected = false; try { new Settings { AvoidEliteIds = ["unknown"] }.Validate(); } catch { rejected = true; }
        check(rejected, "拒绝未知精英ID，避免无效名单悄悄失效");
        var route = JsonNode.Parse("""{"nodo":"start","hechos":["start"],"destinos":["elite"],"mapa":{"nodos":{"start":{"tipo":"osario"},"elite":{"tipo":"elite"}}}}""")!;
        var snap = new JsonObject { ["sima"] = new JsonObject { ["run"] = route }, ["peeks"] = JsonNode.Parse("""{"elite":{"enemigos":[{"char":"sg_alferez"}]}}""") };
        var plainMove = Pilot.RouteChoice(route, "elite", cfg, snap);
        var avoidMove = Pilot.RouteChoice(route, "elite", avoid, snap);
        check(avoidMove.Risk > plainMove.Risk && avoidMove.Reason.Contains("陪葬旗官") && avoidMove.Priority == plainMove.Priority, "避让是有限风险评分，并在候选理由中显示具体精英");
        var originalData = Paths.Data;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-Strategy-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = temp;
            var pilot = new Pilot(); pilot.Save(avoid);
            check(pilot.Build(snap).Single().Method == "sima.move", "唯一必经路线命中避让名单仍可前进");
            avoid.AllowElite = false; pilot.Save(avoid);
            check(pilot.Build(snap).Single().Reason.Contains("陪葬旗官"), "关闭精英路线后的必经fallback仍保留避让评分而不停止");
            check(new Pilot().Config.AvoidEliteIds.SequenceEqual(["sg_alferez"]), "精英多选写入磁盘并能重新加载");
            var unrestricted = new Settings { Simulations=100001, DelayMs=-50, MaxRuns=2001, StartDepth=-12, StopDepth=1, ReserveRopes=101, PactFloors=2001, RewardFloors=-3, ClearFloors=5001, CoreRewardWeight=51, ShardRewardWeight=-40, FreezeDuration=10, RetreatHp=-.3, MinWinRate=1.4, RiskAversion=-2, ModelUncertainty=2, EnemyStrength=-1, FreezeWeight=30 };
            pilot.Save(unrestricted);
            var loaded = new Pilot().Config;
            check(loaded.Simulations==100001 && loaded.DelayMs==-50 && loaded.CoreRewardWeight==51 && loaded.MinWinRate==1.4 && loaded.StartDepth==-12, "超出旧上下限的整数、比例与权重原样写入并重新加载");
            bool nonfinite=false; try { new Settings { RiskAversion=double.NaN }.Validate(); } catch { nonfinite=true; }
            check(nonfinite && new Settings().DelayMs==1000, "仍拒绝非有限数字，操作间隔默认1000毫秒");
            check(LoopPolicy.Configure(cfg,new("count",10001)).MaxRuns==10001 && LoopPolicy.Stop(LoopPolicy.Configure(cfg,new("count",-2)),0,10)!=null, "次数循环没有1000上限，负次数不新入场");
            pilot.Save(new Settings { PreferFreezeBuild=false, AvoidFireLightning=false, AllowBasicBurn=false, AllowBasicLightning=false });
            var prohibited=new[]{"br_yesca","br_incendio","fu_descarga","fu_tormenta"}.Select(id=>Card(id,"","")).ToArray();
            var described=Card("future_electric","fulgor","Tus autos ELECTRIFICAN y pegas +5% a lo que lleve un estado");
            var localized=Card("future_burn","brasa",""); localized["descZh"]="普攻使敌人燃烧";
            foreach(var method in new[]{"sima.reliquia","sima.blason","sima.guarda"})
            {
                var allowed=pilot.CardChoices(prohibited.Append(described).Append(localized).Append(lightning).ToArray(),method,run);
                check(allowed.Length==1 && allowed[0].Card.At("id").S()=="fu_cadena",method+"按独立开关排除普攻点燃带电，关闭构筑偏好仍有效，已继承冻结后电系弹跳允许");
            }
            var fallback=pilot.CardChoices(prohibited,"sima.reliquia",new JsonObject());
            check(fallback.Length==prohibited.Length && fallback.All(c=>c.Label.Contains("兜底")),"只有策略避让牌时恢复实际候选并记录兜底，不暂停");
            var store=JsonNode.Parse("""{"sima":{"run":{"nodo":"shop","reliquias":[{"id":"es_carambano"},{"id":"au_alba"}],"mapa":{"nodos":{"shop":{"tipo":"chatarrero"},"next":{"tipo":"santuario"}}},"destinos":["next"],"botin":{"obolos":100},"tienda":{"precio":1}}}}""")!;
            store["sima"]!["run"]!["tienda"]!["buffs"]=new JsonArray(prohibited.Append(described).Append(shield).Select(c=>c.DeepClone()).ToArray());
            cfg.AllowBasicBurn=false; pilot.Save(cfg);
            var shop=pilot.Build(store).Where(c=>c.Method=="sima.trade").ToArray();
            check(shop.Length==1 && shop[0].Card.At("id").S()=="au_alba","生产商店入口过滤禁牌且允许第二张高级盾");
            var inherited=JsonNode.Parse("""{"reliquias":[{"id":"es_carambano"}],"guardaPendiente":{"cartas":[{"id":"au_alba","llevas":1}]}}""")!;
            inherited["guardaPendiente"]!["cartas"]![0]!["desc"] = shield.At("desc").S();
            check(Pilot.Rank(pilot.CardChoices([core,shield,crit],"sima.guarda",inherited))[0].Card.At("id").S()=="um_filo","已继承冻结和盾后优先双暴，不重复抢冻结或高级盾");
            var temporary=JsonNode.Parse("""{"blasones":[{"id":"es2_carambano"}],"reliquias":[],"guardaPendiente":{"cartas":[{"id":"es_carambano","llevas":0},{"id":"au_alba","llevas":0}]}}""")!;
            check(Pilot.Rank(pilot.CardChoices([shield,core],"sima.guarda",temporary))[0].Core,"临时冻结徽章不替代必须继承的首个冻结核心");
            var zeroChoice=new Choice("不抽样路线","sima.move",new(),risk:10);
            new Simulation(false).Evaluate([zeroChoice],new JsonObject(),new Settings{Simulations=0},CancellationToken.None);
            check(zeroChoice.Forecast==null && Pilot.Rank([zeroChoice,new Choice("安全路线","sima.move",new())])[0].Label=="安全路线","非正推演次数不除零、不伪造胜率，仍按路线风险排序");
        }
        finally { Paths.Data = originalData; if (Directory.Exists(temp)) Directory.Delete(temp, true); }

        var sanctuary = JsonNode.Parse("""{"sima":{"run":{"santuario":{"reposarPct":30,"levanta":true},"heroes":[{"id":"caster","nombre":"法师","hp":950,"hpMax":1000,"anima":58},{"id":"support","nombre":"圣骑士","hp":1000,"hpMax":1000,"anima":100}]}},"heroes":[{"id":"caster","skills":[{"equipped":true,"damages":true,"desc":"300% de daño","cooldown":4}]},{"id":"support","skills":[{"equipped":true,"damages":false,"desc":"Heal","cooldown":3}]}]}""")!;
        Choice[] Rest() => Pilot.SanctuaryChoices(sanctuary, sanctuary.At("sima", "run")!);
        var pick = Pilot.Rank(Rest()).First();
        check(pick.Args.At("opcion").S() == "afilar" && pick.Args.At("heroId").S() == "caster", "生命充足且魂力58/100时优先磨利伤害技能，而不为小缺口冥想");
        sanctuary["sima"]!["run"]!["heroes"]![0]!["hp"] = 300;
        check(Pilot.Rank(Rest()).First().Args.At("opcion").S() == "reposar", "明显低血时恢复生命仍先于磨利");
        sanctuary["sima"]!["run"]!["heroes"]![0]!["hp"] = 1000;
        sanctuary["sima"]!["run"]!["heroes"]![0]!["anima"] = 10;
        check(Pilot.Rank(Rest()).First().Args.At("opcion").S() == "meditar", "魂力枯竭时先恢复技能启动资源");
        sanctuary["sima"]!["run"]!["heroes"]![1]!["caido"] = true;
        check(Pilot.Rank(Rest()).First().Args.At("opcion").S() == "levantar", "有倒地队员时优先复活，磨利不抢急救");

        // Same fields as the client event card: soul cost is represented by
        // disponible=false plus motivo, distinct from run.pacto combat challenge.
        var echo = JsonNode.Parse("""{"omen":{"id":"echo-fixture","opciones":[{"id":"firmar","disponible":false,"motivo":"Tu Ánima no alcanza","texto":"Firmar (−20 Ánima) → +1 Lágrima"},{"id":"pasar","disponible":true,"texto":"Seguir de largo"}]}}""")!;
        var availableEvents = EventRules.Choices(echo);
        check(availableEvents.Length == 1 && availableEvents[0].Args.At("opcion").S() == "pasar", "截图回归：无法支付20魂力的缔约被排除，只选择径直前行");
        echo["omen"]!["opciones"]![0]!["motivo"] = "No llevas óbolos suficientes";
        check(EventRules.Choices(echo).Single().Args.At("opcion").S() == "pasar", "货币不足等其他不可用交易同样排除，不按收益反复尝试");
        echo["omen"]!["opciones"]![0]!["disponible"] = true;
        check(EventRules.Choices(echo).Length == 2, "可支付的事件交易仍参与正常收益比较，不一律跳过");
        echo["omen"]!["opciones"]![0]!.AsObject().Remove("disponible");
        check(EventRules.Choices(echo).Length == 2, "兼容客户端旧选项缺失disponible时默认可用");
        echo["omen"]!["opciones"]![0]!["disponible"] = false;
        echo["omen"]!["opciones"]![1]!["disponible"] = false;
        rejected = false; try { EventRules.Choices(echo); } catch (Exception e) { rejected = e.Message.Contains("没有可用选项"); }
        check(rejected, "所有选项都不可用时保留暂停保护，不杜撰选择或无限循环");
        check(EventRules.Choices(new JsonObject()).Single().Args.At("opcion").S() == "", "尚未加载事件时仍正常请求选项");
        echo["omen"]!["opciones"]![1]!["disponible"] = true;
        echo["sima"] = JsonNode.Parse("""{"run":{"nodo":"mist","hechos":[],"mapa":{"nodos":{"mist":{"tipo":"presagio"}}}}}""");
        check(new Pilot().Build(echo).Single().Args.At("opcion").S() == "pasar", "真实生产决策入口也选择右侧前行，不只测试独立过滤函数");
    }
}
