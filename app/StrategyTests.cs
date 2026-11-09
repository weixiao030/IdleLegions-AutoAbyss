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
                var allowed=pilot.CardChoices(prohibited.Append(described).Append(localized).Append(lightning).ToArray(),method,new JsonObject());
                check(allowed.Length==1 && allowed[0].Card.At("id").S()=="fu_cadena",method+"按独立开关排除普攻点燃带电，关闭构筑偏好仍有效，电系弹跳允许");
            }
            bool noUnsafeFallback=false; try {pilot.CardChoices(prohibited,"sima.reliquia",new JsonObject());} catch(Exception e){noUnsafeFallback=e.Message.Contains("没有符合");}
            check(noUnsafeFallback,"只有禁牌时明确暂停，不以fallback选中禁牌");
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
