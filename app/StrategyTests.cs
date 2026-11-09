using System.Text.Json.Nodes;

namespace FrostAbyss;

static class StrategyTests
{
    static JsonNode Card(string id, string family, string desc) => new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    public static void Run(Action<bool, string> check)
    {
        var cfg = new Settings();
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
        check(P(shield) > P(slow), "已有核心先补一张可保留开局护盾");
        check(P(slow) > P(vsSlow) && P(vsSlow) > P(crit) && P(poison) > P(crit), "构筑先减速、减速增伤和异常增伤，双暴仅为补充");
        check(P(fireDamage) > P(crit) && P(lightning) > P(crit), "火系纯状态增伤与电系弹跳不再被家族回避误伤");
        check(Effects.Parse([plague], cfg).Cleave == 5 && Effects.Parse([plague], cfg).Poison, "识别真实毒系传说五次弹跳的复数描述");
        check(Math.Abs(Effects.Parse([Card("po_entumecer", "ponzona", "Tus golpes dejan al objetivo un 10% más lento durante 3 s")], cfg).Slow - .1) < 1e-8, "识别真实更慢百分比描述");
        check(Effects.Parse([shield], cfg).Shield == .25 && Effects.Parse([Card("au6_vigilia", "aurora", "Escudo del 5% los 3 primeros segundos de cada sala")], cfg).Shield == .05, "晨昏普通及徽章开局护盾描述均可解析");
        run["reliquias"]!.AsArray().Add(shield.DeepClone());
        check(P(shield) < P(poison) && P(shield) < P(crit), "已有可保留护盾后不重复优先抢继承名额");
        run["reliquias"] = new JsonArray(core.DeepClone());
        run["blasones"] = new JsonArray(shield.DeepClone());
        check(P(shield) > P(slow), "本层护盾徽章不能冒充可继承护盾");
        check(P(mark) > P(deepen) && P(deepen) < P(crit), "没有标记来源时先标记，标记加深不空占高优先级");
        run["reliquias"]!.AsArray().Add(mark.DeepClone());
        check(P(deepen) > P(crit), "有标记来源后标记加深优先于双暴补充");
        check(Effects.Parse([mark, deepen], cfg) is { Mark: > 0, MarkDeepen: > 0, MarkCap: 4 }, "标记来源、加深和上限进入组合模型");
        var burn = Card("br_yesca", "brasa", "Tus autoataques dejan al objetivo ARDIENDO");
        var detonate = Card("br_detonador", "brasa", "Tus autoataques CONSUMEN el estado del objetivo y fuerzan su reacción");
        check(P(burn) < 0 && P(detonate) < 0 && P(fireDamage) > 0, "只降低实际破坏冻结的附魔或状态消耗，不屏蔽整个火家族");
        run["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(shield.DeepClone()) };
        check(P(shield, "sima.guarda") > P(poison, "sima.guarda"), "继承时已有核心而尚无继承盾，优先补一张盾");
        run["guardaPendiente"]!["cartas"]![0]!["llevas"] = 1;
        check(P(shield, "sima.guarda") < P(poison, "sima.guarda"), "旧护盾llevas计数参与继承去重");

        check(cfg.AvoidEliteIds.Length == 0 && EliteRules.Catalog.Length == 22, "精英多选默认空名单，清单包含22种已核对精英");
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
