using System.Text.Json.Nodes;

namespace FrostAbyss;

static class SelfTest
{
    public static int Run(string[] args)
    {
        var checks = new List<string>();
        try
        {
            void Assert(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
            using (var icon = Branding.LoadIcon(new System.Drawing.Size(16, 16)))
                Assert(icon.Size == new System.Drawing.Size(16, 16) && icon.Handle != IntPtr.Zero, "托盘16像素图标可在源流释放后使用");
            using (var icon = Branding.LoadIcon(new System.Drawing.Size(32, 32)))
                Assert(icon.Size == new System.Drawing.Size(32, 32) && icon.Handle != IntPtr.Zero, "高DPI32像素托盘图标可独立释放");
            var assembly = typeof(Branding).Assembly;
            using var iconStream = assembly.GetManifestResourceStream("app.ico")!;
            using var iconBytes = new MemoryStream(); iconStream.CopyTo(iconBytes);
            var rawIcon = iconBytes.ToArray();
            var iconSizes = Enumerable.Range(0, BitConverter.ToUInt16(rawIcon, 4)).Select(i => rawIcon[6 + i * 16] == 0 ? 256 : (int)rawIcon[6 + i * 16]).ToArray();
            Assert(BitConverter.ToUInt16(rawIcon, 2) == 1 && iconSizes.SequenceEqual(new[] {16,20,24,32,40,48,64,128,256}), "ICO包含九档Windows尺寸，含256像素");
            using var faviconStream = assembly.GetManifestResourceStream("web/favicon.ico")!;
            using var faviconBytes = new MemoryStream(); faviconStream.CopyTo(faviconBytes);
            Assert(rawIcon.SequenceEqual(faviconBytes.ToArray()), "EXE托盘与网页favicon图标资源一致");
            using var htmlStream = assembly.GetManifestResourceStream("web/index.html")!;
            using var htmlReader = new StreamReader(htmlStream);
            Assert(htmlReader.ReadToEnd().Contains("href=\"/favicon.ico\""), "生产HTML声明浏览器页签图标");
            var cfg = new Settings(); cfg.Validate();
            var loopOriginal = new Settings { MaxRuns = 7, ReserveRopes = 2, AutoEnter = false };
            var countLoop = LoopPolicy.Configure(loopOriginal, new("count", 3));
            var ropeLoop = LoopPolicy.Configure(loopOriginal, new("ropes"));
            Assert(countLoop.MaxRuns == 3 && !countLoop.LoopUntilRopesEmpty && countLoop.AutoEnter && loopOriginal.MaxRuns == 7 && !loopOriginal.AutoEnter, "次数循环配置独立复制，不污染原参数");
            Assert(LoopPolicy.Stop(countLoop, 2, 5) == null && LoopPolicy.Stop(countLoop, 3, 5) != null, "循环3次只在第三次结算后停止，胜败不改变次数规则");
            Assert(ropeLoop.LoopUntilRopesEmpty && ropeLoop.ReserveRopes == 0 && LoopPolicy.Stop(ropeLoop, 2000, 1) == null && LoopPolicy.Stop(ropeLoop, 2000, 0) != null, "用完模式忽略次数与旧保留量，最后一根仍可入场，0根停止");
            Assert(LoopPolicy.Stop(countLoop, 1, 2) != null && LoopPolicy.Configure(loopOriginal, new("single")).MaxRuns == 1, "次数模式尊重保留绳索，单次模式重置循环上限");
            Assert(LoopPolicy.Stop(LoopPolicy.Configure(loopOriginal,new("count",0)),0,10)!=null, "零次循环保存成功且不新入场");
            var retained = JsonNode.Parse("{\"reliquias\":[{\"id\":\"es_carambano\",\"familia\":\"escarcha\"},{\"id\":\"um_filo\"}],\"guardaPendiente\":{\"cartas\":[{\"id\":\"um_filo\",\"llevas\":0}],\"heredadas\":1}}")!;
            var critCard = JsonNode.Parse("{\"id\":\"um_filo\",\"familia\":\"umbra\",\"desc\":\"+6 puntos de crítico y +20% de daño crítico\"}")!;
            Assert(Cards.Inherited(retained).Length == 1 && Cards.IsCore(Cards.Inherited(retained)[0], cfg) && Cards.Priority(critCard, cfg, retained, "sima.guarda") > Cards.Priority(JsonNode.Parse("{\"desc\":\"+25% de defensa\"}")!, cfg, retained, "sima.guarda"), "继承基线保留候选外旧核心，双暴优于纯防御补充");
            var onlyBadge = JsonNode.Parse("{\"blasones\":[{\"id\":\"es2_carambano\"}],\"reliquias\":[],\"guardaPendiente\":{\"cartas\":[{\"id\":\"es_carambano\",\"llevas\":0}]}}")!;
            var transferable = JsonNode.Parse("{\"id\":\"es_carambano\",\"familia\":\"escarcha\",\"desc\":\"Tus autoataques CONGELAN al objetivo\"}")!;
            Assert(Cards.Priority(transferable, cfg, onlyBadge, "sima.guarda") == 2000 && Cards.Priority(transferable, cfg, onlyBadge) == 2000, "本层冻结徽章不能替代可继承冻结牌，选牌和唯一带走名额都优先核心");
            var carryChoice = new Choice("core", "sima.guarda", new(), card: transferable);
            Assert(Cards.Project(onlyBadge, carryChoice).Count(c => Cards.IsCore(c, cfg)) == 1, "下一层只投影实际保留的冻结牌，不带走本层徽章");
            var blast = JsonNode.Parse("{\"id\":\"damage_bounce\",\"desc\":\"+10% de daño y cada 5 autoataques tu golpe salta a todos\"}")!;
            Assert(Cards.Priority(blast,cfg,retained,"sima.guarda") > Cards.Priority(critCard,cfg,retained,"sima.guarda"), "跨层核心已有时先弹跳再双暴，同级比较模拟");
            Assert(Cards.Priority(JsonNode.Parse("{\"familia\":\"brasa\",\"desc\":\"+50% de daño\"}")!,cfg) == Cards.Priority(critCard,cfg), "通用攻击与双暴同档比较模拟，不因火系家族低估输出");
            var shopExit=new Choice("exit","sima.move",new()){Forecast=new(1,1,1,1,115,4096)};
            var shopGood=new Choice("good","sima.trade",new(),card:JsonNode.Parse("{\"desc\":\"+6% de daño\"}")){Forecast=new(.8,.7,.9,.5,90,4096)};
            var shopBad=new Choice("bad","sima.trade",new(),card:new JsonObject()){Forecast=new(.6,.5,.7,.4,70,4096)};
            var shopPick=Pilot.BeforeShopExit([shopExit,shopGood,shopBad],new(.7,.6,.8,.4,80,4096));
            Assert(shopPick.Length==1 && shopPick[0]==shopGood,"商店有收益增益先买再离开，并排除比不买更差的候选");
            var supplyRun = JsonNode.Parse("{\"heroes\":[{\"hp\":99,\"hpMax\":100,\"anima\":99},{\"caido\":true,\"hp\":0,\"hpMax\":100,\"anima\":0}]}")!;
            Assert(Pilot.SupplyBenefit(supplyRun,"vida")>.009 && Pilot.SupplyBenefit(supplyRun,"anima")>.009, "商店少量缺血或魂力也购买，不再要求缺口大于12%");
            supplyRun["heroes"]![0]!["hp"]=100;supplyRun["heroes"]![0]!["anima"]=100;
            Assert(Pilot.SupplyBenefit(supplyRun,"vida")==0 && Pilot.SupplyBenefit(supplyRun,"anima")==0 && Pilot.SupplyBenefit(supplyRun,"esquirla")>0,"商店碎屑兑换生效，倒下英雄不虚构补给收益");
            var favor=Effects.Parse([JsonNode.Parse("{\"desc\":\"+8% de daño contra élites y el jefe\"}")!],cfg);
            Assert(Math.Abs(favor.Outgoing(false,false,1,true)-1.08)<.00001 && favor.Outgoing(false,false,1)==1, "实测 Rencor 商店增益只对精英与首领计入8%增伤");
            var incense = Effects.Parse([JsonNode.Parse("{\"id\":\"au_incienso\",\"desc\":\"Escudo del 4% de tu vida durante 3 s al empezar cada sala; bajo el 40% de vida, tu defensa sube un 8% y te limpias los estados\"}")!],cfg);
            Assert(Math.Abs(incense.LowAt-.4)<.0001 && Math.Abs(incense.LowDefense-.08)<.0001 && Math.Abs(incense.Shield-.04)<.0001, "实测 Incienso 的低血防御与开场盾均参与模型");
            Assert(!cfg.PrepareSkills && cfg.AvoidFireLightning, "默认保留技能，明确消耗状态效果降序且普攻点燃带电硬禁");
            Assert(cfg.ClearFloors == 0 && cfg.StopDepth == 0 && cfg.RetreatHp == 0 && cfg.MinWinRate == 0, "默认持续下潜，不因层数、生命或预测胜率提前停止");
            Assert(cfg.SkipBattleReplay, "默认跳过战斗播放");
            Assert(cfg.Formation.SequenceEqual(new[] { "berserker", "mago", "arquera", "paladin" }), "默认使用图二：狂战士、法师、弓箭手、圣骑士");
            var professions = FormationRules.Professions;
            Assert(professions.Select(p => p.Id).ToHashSet().SetEquals(new[] { "berserker", "arquera", "healer", "asesino", "paladin", "medico", "mago", "cruzado" }) && professions.All(p => p.Name.Any(c => c >= '\u4e00' && c <= '\u9fff')), "全部八个游戏职业均有中文名，前后端共享职业表");
            int formations = 0;
            foreach (var a in professions) foreach (var b in professions) foreach (var c in professions) foreach (var d in professions)
                if (new[] { a.Id, b.Id, c.Id, d.Id }.Distinct().Count() == 4) { if (!Settings.ValidFormation([a.Id, b.Id, c.Id, d.Id])) throw new Exception("合法自由阵容被拒绝"); formations++; }
            Assert(formations == 1680, "全部1680种四职业有序组合均可保存，不固定狂战士或圣骑士的位置");
            Assert(!Settings.ValidFormation(null) && !Settings.ValidFormation([]) && !Settings.ValidFormation(["berserker", "mago", "paladin"]) && !Settings.ValidFormation(["berserker", "mago", "mago", "paladin"]) && !Settings.ValidFormation(["berserker", "unknown", "arquera", "paladin"]) && !Settings.ValidFormation(["berserker", null!, "arquera", "paladin"]), "拒绝空阵容、缺位、重复、未知职业及空职业");
            var originalData = Paths.Data;
            var formationData = Path.Combine(Path.GetTempPath(), "frost-formation-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Paths.Data = formationData;
                J.Write(Paths.Config, new Settings { Seed = 9988, Formation = ["berserker", "arquera", "mago", "paladin"] });
                var formationPilot = new Pilot();
                Assert(formationPilot.Config.Formation[1] == "arquera", "加载旧配置保留原站位，不重置已有方案");
                var custom = new[] { "berserker", "asesino", "mago", "paladin" };
                formationPilot.SaveFormation(custom);
                custom[0] = "medico";
                var reloaded = new Pilot();
                Assert(reloaded.Config.Seed == 9988 && reloaded.Config.Formation.SequenceEqual(new[] { "berserker", "asesino", "mago", "paladin" }), "独立保存站位落盘且重开可恢复，保留其他参数并隔离调用方数组修改");
                var beforeInvalid = File.ReadAllText(Paths.Config);
                bool refused = false; try { reloaded.SaveFormation(["mago", "mago", "healer", "cruzado"]); } catch { refused = true; }
                Assert(refused && File.ReadAllText(Paths.Config) == beforeInvalid, "非法站位不会覆盖磁盘配置");
                var roster = new JsonArray(professions.Select(p => (JsonNode)new JsonObject { ["defId"] = p.Id, ["id"] = "owned-" + p.Id }).ToArray());
                var entrySnapshot = new JsonObject { ["heroes"] = roster, ["sima"] = new JsonObject { ["puedeEntrar"] = true, ["cuerdas"] = 20 } };
                for (int offset = 0; offset < professions.Length; offset++)
                {
                    var team = Enumerable.Range(0, 4).Select(i => professions[(offset + i) % professions.Length].Id).ToArray();
                    reloaded.SaveFormation(team);
                    var entry = reloaded.Build(entrySnapshot).Single();
                    if (entry.Method != "sima.enter" || !entry.Args.At("ordenado").B() || !entry.Args.At("heroes").A().Select(h => h.S()).SequenceEqual(team.Select(id => "owned-" + id))) throw new Exception("入场英雄顺序或职业映射错误");
                }
                Assert(true, "实际入场请求覆盖八职业，按前排到后排的保存顺序发送真实英雄ID");
                bool missingChinese = false; try { FormationRules.ResolveHeroes(["healer", "asesino", "medico", "cruzado"], new JsonObject()); } catch (Exception e) { missingChinese = e.Message.Contains("治疗师") && !e.Message.Contains("healer"); }
                Assert(missingChinese, "账号缺少职业时显示中文提示");
            }
            finally { Paths.Data = originalData; if (Directory.Exists(formationData)) Directory.Delete(formationData, true); }
            var pactRun = JsonNode.Parse("{\"profundidad\":6,\"pacto\":{\"sellado\":false},\"reliquias\":[{\"id\":\"es2_carambano\"}]}")!;
            Assert(cfg.PactFloors == 7 && Pilot.ShouldPact(pactRun, cfg), "有普攻冻结时默认前七层 P0～P6 缔结契约");
            pactRun["profundidad"] = 7;
            Assert(!Pilot.ShouldPact(pactRun, cfg), "第八层 P7 起不再默认缔约");
            pactRun["profundidad"] = 0; pactRun["reliquias"] = new JsonArray();
            Assert(!Pilot.ShouldPact(pactRun, cfg), "缺少普攻冻结时不缔约");
            pactRun["reliquias"] = JsonNode.Parse("[{\"id\":\"es2_carambano\"}]"); pactRun["pacto"]!["sellado"] = true;
            Assert(!Pilot.ShouldPact(pactRun, cfg), "已缔结的契约不重复执行");
            pactRun["pacto"]!["sellado"] = false;
            Assert(!Pilot.ShouldPact(pactRun, new Settings { PactFloors = 0 }), "契约层数设为零可关闭");
            var mist = JsonNode.Parse("{\"peeks\":{\"3_0\":{\"enemigos\":[]}}}")!;
            var changedRoom = JsonNode.Parse("{\"nodo\":\"3_0\",\"mapa\":{\"nodos\":{\"3_0\":{\"tipo\":\"combate\"}}}}")!;
            Assert(Pilot.NeedsPeek(mist, changedRoom, "3_0"), "迷雾变战斗时刷新旧空预览");
            changedRoom["mapa"]!["nodos"]!["3_0"]!["tipo"] = "presagio";
            Assert(!Pilot.NeedsPeek(mist, changedRoom, "3_0"), "正常迷雾空预览不重复请求");
            changedRoom["mapa"]!["nodos"]!["3_0"]!["tipo"] = "combate";
            mist["peeks"]!["3_0"] = JsonNode.Parse("{\"enemigos\":[{\"name\":\"A\"},{\"name\":\"B\"},{\"name\":\"C\"},{\"name\":\"D\"}],\"legion\":[{\"name\":\"Berserker\"},{\"name\":\"Arquera\"}]}");
            Assert(!Pilot.NeedsPeek(mist, changedRoom, "3_0") && new Pilot().FightChoices(mist, changedRoom, "3_0").Length == 16, "刷新后四敌两存活英雄枚举十六种目标组合");
            var ice = JsonNode.Parse("{\"id\":\"es2_carambano\",\"familia\":\"escarcha\",\"desc\":\"Tus golpes básicos congelan\"}")!;
            var fire = JsonNode.Parse("{\"id\":\"fixture_fire\",\"familia\":\"brasa\"}")!;
            var utility = JsonNode.Parse("{\"id\":\"fixture_util\",\"familia\":\"aurora\",\"desc\":\"+10% vida\"}")!;
            Assert(Cards.IsCore(ice, cfg) && Cards.Priority(ice,cfg) == 2000 && Cards.Priority(utility,cfg) == 100, "缺核心时核心优先，其他系列按构筑优先级比较");
            Assert(Cards.IsCore(JsonNode.Parse("{\"id\":\"es2_carambano\",\"desc\":\"Tus autoataques CONGELAN al objetivo\"}"), cfg), "识别真实核心徽章 es2_carambano");
            Assert(!Cards.IsCore(JsonNode.Parse("{\"id\":\"es_otro\",\"desc\":\"Tus autoataques hacen más daño contra congelados\"}"), cfg), "不把对冻结增伤误认为普攻冻结核心");
            Assert(Cards.IsCore(JsonNode.Parse("{\"id\":\"fixture_advanced\",\"familia\":\"escarcha\",\"desc\":\"Tus autoataques CONGELAN al objetivo, cada 5 ataques salta a todos\"}"), cfg), "识别带普攻冻结的强化弹射效果");
            var prioritized = new[] {
                new Choice("utility", "sima.guarda", new(), priority: 0) { Forecast = new(1, .9, 1, 1, 150, 4096) },
                new Choice("cold", "sima.guarda", new(), priority: Cards.Priority(JsonNode.Parse("{\"id\":\"es_cold\"}")!, cfg)) { Forecast = new(.1, 0, .2, 0, 10, 4096) },
                new Choice("core", "sima.guarda", new(), priority: Cards.Priority(ice, cfg)) { Forecast = new(0, 0, .1, 0, 0, 4096) }
            };
            Assert(Pilot.Rank(prioritized).Select(x => x.Label).SequenceEqual(new[] { "core", "cold", "utility" }), "缺核心时核心优先，寒冷积累优先于其他系列");
            var coreRun = JsonNode.Parse("{\"profundidad\":6,\"reliquias\":[{\"id\":\"es2_carambano\"}]}")!;
            Assert(Cards.Priority(ice,cfg,coreRun)==-10, "已有可保留核心时不重复优先叠冻结");
            Assert(Pilot.RewardPhase(coreRun,cfg) && cfg.RewardFloors==7, "有核心默认前七层优先核心和碎屑奖励");
            coreRun["profundidad"]=7;
            Assert(!Pilot.RewardPhase(coreRun,cfg), "P7 起退出奖励阶段并继续下潜");
            Assert(!Pilot.RewardPhase(coreRun,new Settings { RewardFloors=0 }), "奖励层数为零可关闭");
            var routeRun=JsonNode.Parse("{\"profundidad\":6,\"reliquias\":[{\"id\":\"es_carambano\"}],\"mapa\":{\"nodos\":{\"fight\":{\"tipo\":\"combate\",\"salidas\":[]},\"safe\":{\"tipo\":\"osario\",\"salidas\":[]}}}}")!;
            Assert(Pilot.RouteChoice(routeRun,"fight",cfg).Benefit>Pilot.RouteChoice(routeRun,"safe",cfg).Benefit,"前七层有核心时战斗获得核心/碎屑的偏好生效");
            routeRun["profundidad"]=7;
            Assert(Pilot.RouteChoice(routeRun,"fight",cfg).Risk>Pilot.RouteChoice(routeRun,"safe",cfg).Risk,"P7 之后安全路线优先，必经战斗仍有合法候选");
            Assert(Pilot.RouteChoice(routeRun,"fight",new Settings{AvoidCombatAfterRewardFloors=false}).Risk<Pilot.RouteChoice(routeRun,"fight",cfg).Risk,"关闭避战开关可自定义后半程策略");
            var legacy=JsonNode.Parse("{\"guardaPendiente\":{\"cartas\":[{\"id\":\"es_carambano\",\"llevas\":1}]}}")!;
            Assert(Cards.Priority(ice,cfg,legacy,"sima.guarda")==-10 && Cards.Inherited(legacy).Length==1,"已继承核心不会被误认为下一层丢失");
            legacy["guardaPendiente"]!["cartas"]![0]!["llevas"]=0;
            Assert(Cards.Priority(ice,cfg,legacy,"sima.guarda")==2000,"尚未继承核心时带走核心优先");
            var fx=Effects.Parse(new[]{JsonNode.Parse("{\"desc\":\"+40% de daño contra lo que lleve un estado encima\"}")!,JsonNode.Parse("{\"desc\":\"+5% de velocidad de ataque\"}")!,JsonNode.Parse("{\"desc\":\"+0,6% de robo de vida\"}")!},cfg);
            Assert(Math.Abs(fx.VsStatus-.4)<1e-6 && Math.Abs(fx.Speed-.05)<1e-6 && Math.Abs(fx.Leech-.006)<1e-6 && fx.Damage==0,"解析跨系列状态增伤、攻速及西语小数吸血，不重复算成通用增伤");
            var tizon=Effects.Parse(new[]{JsonNode.Parse("{\"desc\":\"+6% de daño de tus reacciones elementales y +4% contra lo que lleve un estado, y +10% de daño crítico\"}")!},cfg);
            Assert(Math.Abs(tizon.Damage)<1e-6 && Math.Abs(tizon.ReactionDamage-.06)<1e-6 && Math.Abs(tizon.VsStatus-.04)<1e-6 && Math.Abs(tizon.CritDamage-.1)<1e-6,"真实 Tizón 描述的反应增伤不误算成通用增伤");
            Assert(!Effects.Parse(new[]{fire},cfg).Burn,"火系列标签本身不会凭空触发燃烧");
            bool noExtract = false;
            try { new Bridge().Send("sima.extract", new(), CancellationToken.None).GetAwaiter().GetResult(); } catch (Exception e) { noExtract = e.Message.Contains("禁止撤离"); }
            Assert(noExtract, "撤离命令在派发前被硬性拒绝");
            bool rejected = false; var invalid = new Settings { Simulations = 0 }; invalid.Validate(); Assert(invalid.Simulations == 0, "允许零次推演并关闭抽样");
            Assert(!new Bridge().Fresh(JsonNode.Parse("{\"time\":0}")), "拒绝过期游戏快照");
            var ipcFolder = Path.Combine(Path.GetTempPath(), "FrostAbyss-IpcTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ipcFolder);
            try
            {
                var ipc = Path.Combine(ipcFolder, "command.json");
                J.Write(ipc, new { seq = 1 });
                using (var reader = new FileStream(ipc, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    using var publishStarted = new ManualResetEventSlim();
                    var publish = Task.Run(() => { publishStarted.Set(); J.Write(ipc, new { seq = 2 }); });
                    publishStarted.Wait();
                    Thread.Sleep(150);
                    Assert(!publish.IsCompleted && J.Read(ipc).At("seq").N() == 1, "文件被读取时保留旧命令，不发布半写入数据");
                    reader.Dispose();
                    publish.GetAwaiter().GetResult();
                }
                Assert(J.Read(ipc).At("seq").N() == 2 && Directory.GetFiles(ipcFolder, "*.tmp").Length == 0, "共享锁释放后原子发布一次并清理临时文件");
            }
            finally { foreach (var file in Directory.GetFiles(ipcFolder)) File.Delete(file); Directory.Delete(ipcFolder); }
            var carryBefore = JsonNode.Parse("""{"profundidad":2,"reliquias":[{"id":"es_carambano"},{"id":"um_filo"}],"guardaPendiente":{"cartas":[{"id":"um_filo","llevas":0}]}}""")!;
            var carryNext = JsonNode.Parse("""{"profundidad":3,"reliquias":[{"id":"es_carambano"},{"id":"um_filo"}]}""")!;
            var carryPick = carryBefore.At("guardaPendiente", "cartas").A().First();
            Assert(Cards.InheritanceError(carryBefore, carryNext, carryPick, cfg) == null, "真实字段形态：冻结已继承时新增暴击并确认下一深度");
            carryNext["reliquias"] = new JsonArray(JsonNode.Parse("""{"id":"um_filo"}"""));
            carryNext["blasones"] = new JsonArray(JsonNode.Parse("""{"id":"es2_carambano"}"""));
            Assert(Cards.InheritanceError(carryBefore, carryNext, carryPick, cfg) != null, "下一层仅有冻结徽章不能冒充继承遗物，自动暂停");
            carryNext["profundidad"] = 2;
            Assert(Cards.InheritanceError(carryBefore, carryNext, carryPick, cfg) != null, "成功回执没有推进深度时拒绝继续");
            var historicalPath = Path.Combine(AppContext.BaseDirectory, "unused");
            historicalPath = args.SkipWhile(x => x != "--inheritance-fixture").Skip(1).FirstOrDefault();
            if (historicalPath != null)
            {
                var historical = J.Read(historicalPath)!;
                var historicalRun = historical.At("snapshot", "sima", "run") ?? historical.At("sima", "run");
                Assert(historicalRun != null && Cards.Inherited(historicalRun).Select(c => c.At("id").S()).Order().SequenceEqual(new[] { "um_guadana", "um_saña" }.Order()), "真实P2继承快照回归：候选外两张旧遗物均保留");
            }
            var fixturePath = args.SkipWhile(x => x != "--fixture").Skip(1).FirstOrDefault();
            if (fixturePath != null)
            {
                var snapshot = J.Read(fixturePath) ?? throw new Exception("无法读取离线快照");
                var model = new Simulation(); model.Learn(snapshot);
                var a = new Choice("ice", "sima.reliquia", new() { ["id"] = "fixture_ice" }, card: ice);
                var b = new Choice("damage", "sima.reliquia", new() { ["id"] = "fixture_damage" }, card: JsonNode.Parse("{\"desc\":\"+40% de daño\"}"));
                model.Evaluate([a, b], snapshot, cfg, CancellationToken.None);
                Assert(a.Forecast is { Trials: 4096 } && b.Forecast is { Trials: 4096 }, "实机快照每候选实际运行 4096 次组合模型");
                var previous = a.Forecast; model.Evaluate([a], snapshot, cfg, CancellationToken.None);
                Assert(System.Text.Json.JsonSerializer.Serialize(a.Forecast,J.Options) == System.Text.Json.JsonSerializer.Serialize(previous,J.Options), "相同种子可重现推演结果");
                using var ct = new CancellationTokenSource(); ct.Cancel(); rejected = false;
                try { model.Evaluate([a], snapshot, cfg, ct.Token); } catch (OperationCanceledException) { rejected = true; }
                Assert(rejected, "本地推演响应暂停取消");
            }
            var synthetic = JsonNode.Parse("{\"sima\":{\"run\":{\"nodo\":\"1\",\"profundidad\":0,\"mapa\":{\"nodos\":{\"1\":{\"fila\":1,\"tipo\":\"combate\"}}},\"heroes\":[{\"id\":\"hero\",\"nombre\":\"Hero\",\"hp\":1000,\"hpMax\":1000,\"anima\":100}],\"reliquias\":[]}},\"heroes\":[{\"id\":\"hero\",\"nombre\":\"Hero\",\"stats\":{\"hp\":1000,\"atk\":100,\"spd\":1,\"crit\":0}}],\"peeks\":{\"1\":{\"enemigos\":[{\"char\":\"test\",\"name\":\"Enemy\",\"foco\":\"front\"}]}}}")!;
            var local = new Simulation(false); var fastCfg = new Settings { Simulations=2000, ModelUncertainty=.5 };
            var baseChoice=new Choice("base","sima.fight",new());
            var freezeChoice=new Choice("core","sima.reliquia",new(),card:ice);
            local.Evaluate([baseChoice,freezeChoice],synthetic,fastCfg,CancellationToken.None);
            Assert(freezeChoice.Forecast!.Frozen>baseChoice.Forecast!.Frozen && baseChoice.Forecast.Frozen==0,"无冰技能且无核心时绝不凭空冻结，核心才在普攻触发");
            synthetic["sima"]!["run"]!["reliquias"]=new JsonArray(ice.DeepClone());
            var small=new Choice("small cold","sima.reliquia",new(),card:JsonNode.Parse("{\"familia\":\"escarcha\",\"desc\":\"+1% de daño\"}"));
            var mixed=new Choice("mixed","sima.reliquia",new(),card:JsonNode.Parse("{\"familia\":\"ponzona\",\"desc\":\"+40% de daño contra lo que lleve un estado encima\"}"));
            local.Evaluate([small,mixed],synthetic,fastCfg,CancellationToken.None);
            Assert(mixed.Forecast!.Duration<small.Forecast!.Duration && mixed.Forecast.Score>small.Forecast.Score,"已有核心时状态增伤混搭确实改变战斗时序并胜过弱寒冷牌");
            synthetic["heroes"]![0]!["skills"]=JsonNode.Parse("[{\"equipped\":true,\"damages\":true,\"cooldown\":2,\"desc\":\"100% de daño\",\"element\":\"fire\",\"rule\":{\"cond\":\"always\"}}]");
            var fireSkill=new Choice("fire skill","sima.fight",new());local.Evaluate([fireSkill],synthetic,fastCfg,CancellationToken.None);
            synthetic["heroes"]![0]!["skills"]=new JsonArray();
            var noFire=new Choice("physical","sima.fight",new());local.Evaluate([noFire],synthetic,fastCfg,CancellationToken.None);
            Assert(fireSkill.Forecast!.Frozen<noFire.Forecast!.Frozen,"火技能确实消耗冻结并改变冻结覆盖率");
            synthetic["heroes"]![0]!["skills"]=JsonNode.Parse("""[{"equipped":true,"damages":true,"cooldown":2,"desc":"100% de daño","element":"lightning","rule":{"cond":"always"}}]""");
            var electricNo=new Choice("electric-preserve","sima.fight",new());
            local.Evaluate([electricNo],synthetic,fastCfg,CancellationToken.None);
            fastCfg.LightningConsumesFreeze=true;
            var electricYes=new Choice("electric-consume","sima.fight",new());
            local.Evaluate([electricYes],synthetic,fastCfg,CancellationToken.None);
            Assert(electricYes.Forecast!.Frozen<electricNo.Forecast!.Frozen,"纯电伤的两种模型假设可比较，不把未验证机制写死");
            fastCfg.LightningConsumesFreeze=false;synthetic["heroes"]![0]!["skills"]=new JsonArray();
            var inheritance = synthetic.DeepClone();
            inheritance["sima"]!["run"]!["guardaPendiente"] = JsonNode.Parse("{\"cartas\":[{\"id\":\"es_carambano\",\"llevas\":1,\"desc\":\"Tus golpes básicos congelan\"}]}");
            inheritance["sima"]!["run"]!["mapa"]!["nodos"]!["1"]!["tipo"] = "jefe";
            inheritance["sima"]!["run"]!["mapa"]!["nodos"]!["1"]!["fila"] = 8;
            var nextFloor = new Choice("inherit","sima.guarda",new(),card:JsonNode.Parse("{\"desc\":\"+20% de daño\"}"));
            local.Evaluate([nextFloor],inheritance,fastCfg,CancellationToken.None);
            var inheritancePrediction = System.Text.Json.JsonSerializer.Serialize(nextFloor.Forecast,J.Options);
            inheritance["peeks"]!["1"]!["enemigos"] = JsonNode.Parse("[{\"char\":\"other_boss\",\"name\":\"Another boss\",\"foco\":\"back\"},{\"char\":\"escort\",\"name\":\"Escort\"}]");
            local.Evaluate([nextFloor],inheritance,fastCfg,CancellationToken.None);
            Assert(inheritancePrediction == System.Text.Json.JsonSerializer.Serialize(nextFloor.Forecast,J.Options),"继承评估不被刚击败首领的数量与目标污染");
            synthetic["heroes"]![0]!["skills"]=JsonNode.Parse("[{\"equipped\":true,\"damages\":true,\"cooldown\":2,\"desc\":\"500% de daño\",\"element\":\"physical\",\"rule\":{\"cond\":\"always\"}}]");
            synthetic["sima"]!["run"]!["santuario"]=JsonNode.Parse("{\"reposarPct\":30}");
            var sharpen=new Choice("sharpen","sima.rest",new(){["opcion"]="afilar",["heroId"]="hero"});
            var meditate=new Choice("meditate","sima.rest",new(){["opcion"]="meditar"});
            local.Evaluate([sharpen,meditate],synthetic,fastCfg,CancellationToken.None);
            Assert(sharpen.Forecast!.Duration<meditate.Forecast!.Duration,"满血满魂时磨利强化会缩短后续战斗，而非永远选无收益冥想");
            StrategyTests.Run(Assert);
            ResilienceTests.Run(Assert).GetAwaiter().GetResult();
            J.Write(Path.Combine(Paths.Data, "self-test.json"), new { ok = true, checks }); return 0;
        }
        catch (Exception e) { J.Write(Path.Combine(Paths.Data, "self-test.json"), new { ok = false, checks, error = e.ToString() }); return 1; }
    }
}
