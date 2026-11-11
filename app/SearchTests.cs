using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class SearchTests
{
    internal static void Run(Action<bool, string> assert)
    {
        var originalData = Paths.Data;
        Paths.Data = Path.Combine(originalData, "search-fixture");
        Directory.CreateDirectory(Paths.Data);
        try
        {
            foreach (var (threads, memoryGiB, expected) in new (int, double?, string)[]
                { (2, 32, "轻量"), (32, 4, "轻量"), (32, null, "轻量"), (32, double.NaN, "轻量"),
                  (4, 8, "入门"), (8, 8, "入门"), (8, 16, "均衡"), (32, 16, "均衡"), (16, 32, "性能"), (64, 64, "性能") })
            {
                var recommendation = SearchHardware.Recommend(threads, memoryGiB);
                var parameters = new SearchSettings(); recommendation.Parameters.Apply(parameters); parameters.Validate();
                assert(recommendation.Level == expected && parameters.Workers <= Math.Max(1, threads / 2)
                    && parameters.AdaptiveIterations >= parameters.Iterations && parameters.Horizon <= 12,
                    $"Auto 按 {threads} 逻辑线程 / {memoryGiB?.ToString() ?? "未知"} GB 选择{expected}配置并保留游戏空间");
            }
            var automatic = new SearchSettings { Mode = "auto", Workers = 64, Iterations = 99999,
                TrainingDevice = "cuda", AutoTrain = false }; automatic.Validate();
            var effective = SearchHardware.Current.Recommendation.Parameters;
            assert(automatic.Iterations == effective.Iterations && automatic.Workers == effective.Workers
                && automatic.TrainingDevice == "cuda" && !automatic.AutoTrain,
                "Auto 由实际主机重新核对数值，保留训练设备与自动训练开关");
            var manual = new SearchSettings { Iterations = 137, Workers = 3 }; manual.Validate();
            assert(manual.Mode == "manual" && manual.Iterations == 137 && manual.Workers == 3
                && manual.TrainingDevice == "cpu", "旧配置默认手动及CPU，不被Auto覆盖");
            bool badMode = false;
            try { new SearchSettings { Mode = "unknown" }.Validate(); } catch { badMode = true; }
            assert(badMode, "后端拒绝未知配置方式");
            var root = Path.Combine(Paths.Data, "learning"); var memory = new SearchMemory(root);
            var snapshot = JsonNode.Parse("""
            {"sima":{"run":{"id":"floor-a","profundidad":1,"nodo":"reward","llegada":{"reliquias":0},
            "reliquias":[],"blasones":[],"buffs":[],"hechos":["reward"],"destinos":["fight"],
            "heroes":[{"id":"hero","nombre":"Hero","hp":100,"hpMax":100,"anima":0}],
            "mapa":{"nodos":{"reward":{"tipo":"combate","fila":1,"salidas":["fight"]},"fight":{"tipo":"combate","fila":2,"salidas":[]}}},
            "reliquiaOferta":[{"id":"strong","desc":"+1000% de daño"},{"id":"weak","desc":"+1% de defensa"}]}},
            "heroes":[{"id":"hero","stats":{"atk":100,"hp":100,"spd":1,"crit":0,"critdmg":200},"skills":[]}],"peeks":{}}
            """)!;
            string unchanged = snapshot.ToJsonString();
            var cfg = new Settings { StrategyId = 5, Simulations = 1, ModelUncertainty = 0,
                Search = new() { Iterations = 128, Workers = 2, Horizon = 3 } };
            cfg.Validate(); memory.Observe(snapshot.At("sima", "run")); memory.PrepareSearch();
            var actions = SearchWorld.LegalActions(snapshot, cfg);
            var ranked = new SearchPlanner(memory).Rank(actions, snapshot, cfg, new Simulation(false), CancellationToken.None);
            assert(ranked[0].Card.At("id").S() == "strong", "MCTS跨选牌、路线、战斗三步识别能存活的输出牌");
            assert(ranked.Sum(a => a.Search!.Visits) == 128 && ranked.All(a => a.Search!.Visits > 0)
                && ranked[0].Search!.MaxDepth == 3 && ranked[0].Search!.Nodes > actions.Length, "树搜索执行预算、覆盖根候选并扩展后续节点");
            assert(snapshot.ToJsonString() == unchanged, "并行树搜索不修改游戏快照或派发游戏操作");
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel(); bool stopped = false;
                try { new SearchPlanner(memory).Rank(actions, snapshot, cfg, new Simulation(false), canceled.Token); }
                catch (OperationCanceledException) { stopped = true; }
                assert(stopped, "MCTS响应暂停令牌，不在取消后继续搜索");
            }
            var forced = new SearchPlanner(memory).Rank([actions[0]], snapshot, cfg, new Simulation(false), CancellationToken.None);
            assert(ReferenceEquals(forced[0], actions[0]) && forced[0].Search!.TotalIterations == 0
                && forced[0].Search!.Visits == 0 && snapshot.ToJsonString() == unchanged,
                "唯一合法操作保留原候选与快照，不执行无分支搜索或伪造访问数");
            string forcedId = memory.Begin(snapshot, forced, forced[0], cfg); memory.Confirm(forcedId, snapshot);
            var forcedRow = J.Read(Path.Combine(root, "decisions", forcedId + ".json"));
            assert(forcedRow.At("confirmed").B() && forcedRow.At("features").A().Count() == 1 && forcedRow.At("policy") == null,
                "唯一合法操作仍记录确认轨迹供价值学习，不伪造成策略训练标签");
            using (var canceledForced = new CancellationTokenSource())
            {
                canceledForced.Cancel(); bool stopped = false;
                try { new SearchPlanner(memory).Rank(forced, snapshot, cfg, new Simulation(false), canceledForced.Token); }
                catch (OperationCanceledException) { stopped = true; }
                assert(stopped, "唯一操作快捷路径同样响应暂停令牌");
            }
            cfg.StrategyId = 7; cfg.Search.NeuralIterations = 16;
            var cold = new SearchPlanner(memory).Rank(SearchWorld.LegalActions(snapshot, cfg), snapshot, cfg, new Simulation(false), CancellationToken.None);
            assert(!memory.Neural.Ready && cold[0].Search!.ModelVersion == 0 && cold[0].Search!.Algorithm.Contains("冷启动"), "神经网络冷启动明确回退UCT，随机权重不决定真实选择");

            // Every source strategy records complete before/after state and a
            // confirmed receipt. Different floor ids stay in the same episode.
            var ids = new List<string>();
            for (int strategy = 1; strategy <= 7; strategy++)
            {
                cfg.StrategyId = strategy; cfg.Search.AutoTrain = false;
                var candidates = SearchWorld.LegalActions(snapshot, cfg); string id = memory.Begin(snapshot, candidates, candidates[0], cfg);
                ids.Add(id); memory.Confirm(id, snapshot, new JsonObject { ["ok"] = true, ["id"] = "command-" + strategy });
            }
            var carry = new Choice("carry", "sima.guarda", new() { ["id"] = "strong" }, card: actions[0].Card);
            string carryId = memory.Begin(snapshot, [carry], carry, cfg);
            var next = snapshot.DeepClone(); next["sima"]!["run"]!["id"] = "floor-b"; next["sima"]!["run"]!["profundidad"] = 2;
            memory.Confirm(carryId, next, new JsonObject { ["ok"] = true });
            string nextId = memory.Begin(next, [carry], carry, cfg); memory.Confirm(nextId, next);
            string unknown = memory.Begin(next, [carry], carry, cfg); memory.Abort(unknown, "unknown");
            memory.FinishRun("floor-b", 3, cfg);
            var rows = ids.Append(carryId).Append(nextId).Select(id => J.Read(Path.Combine(root, "decisions", id + ".json"))).ToArray();
            assert(rows.All(r => r.At("confirmed").B() && r.At("finalDepth").N() == 3 && r.At("return").N() > .5)
                && rows.Select(r => r.At("episodeId").S()).Distinct().Count() == 1, "全部七策略轨迹跨层串成同次下井，用最终层数回填回报");
            assert(rows.Take(7).Select(r => r.At("strategy").N()).SequenceEqual(Enumerable.Range(1, 7).Select(i => (double)i))
                && rows.All(r => r.At("before") != null && r.At("after") != null), "所有策略均保存战前战后状态，不只记录新策略");
            var excluded = J.Read(Path.Combine(root, "decisions", unknown + ".json"));
            assert(!excluded.At("confirmed").B() && excluded.At("return") == null && excluded.At("outcome").S() == "unknown", "未知结果轨迹保留审计但不进入训练或被补成成功");
            assert(rows.Take(7).All(r => r.At("policy") == null), "旧策略未搜索的动作不伪造成最优策略标签");

            var stopState = snapshot.DeepClone(); stopState["sima"]!["run"]!["reliquiaOferta"] = new JsonArray();
            foreach (int strategy in new[] { 5, 6, 7 })
            {
                var pilot = new Pilot();
                var stopCfg = new Settings { StrategyId = strategy, StopDepth = 1 };
                pilot.Save(stopCfg); bool stopped = false;
                try { pilot.Build(stopState); } catch (Exception e) { stopped = e.Message.Contains("已暂停保留深渊进度"); }
                assert(stopped, $"策略{strategy}尊重自定义到达深度暂停");
                stopCfg.StopDepth = 0; stopCfg.ClearFloors = 1; pilot.Save(stopCfg);
                typeof(Pilot).GetField("clearedFloors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(pilot, 1);
                stopped = false;
                try { pilot.Build(stopState); } catch (Exception e) { stopped = e.Message.Contains("已暂停保留深渊进度"); }
                assert(stopped, $"策略{strategy}尊重通关层数暂停");
                stopCfg.ClearFloors = 0; stopCfg.RetreatHp = 1.1; pilot.Save(stopCfg); stopped = false;
                try { pilot.Build(stopState); } catch (Exception e) { stopped = e.Message.Contains("生命低于"); }
                assert(stopped, $"策略{strategy}尊重生命暂停线");
                assert(pilot.Build(snapshot).All(c => c.Method == "sima.reliquia"), $"策略{strategy}暂停前先处理已获得的强制选牌");
            }

            var features = PlannerFeatures.Extract(snapshot, actions[0], cfg);
            actions[0].Forecast = new(.01, 0, .1, .1, -20, 10);
            assert(features.SequenceEqual(PlannerFeatures.Extract(snapshot, actions[0], cfg)), "网络特征不因仅对推荐项补充预测而泄露被选中标签");
            actions[0].Forecast = null;
            assert(ModelAudit.HistoricalSnapshot(new JsonObject { ["before"] = snapshot.At("sima", "run")!.DeepClone() }) == null,
                "缺历史属性技能上下文时拒绝拿当前账号替代回测");

            var historicalSkill = JsonNode.Parse("""{"equipped":true,"target":"enemy_front","desc":"310% de daño","cooldown":6.8}""")!;
            assert(Simulation.DamagingSkill(historicalSkill) && Simulation.SharpenPotential(new JsonObject { ["skills"] = new JsonArray(historicalSkill) }) > 0,
                "旧战报缺damages标记时按敌方目标和伤害描述识别已装备技能");
            assert(!Simulation.DamagingSkill(JsonNode.Parse("""{"target":"ally_all","desc":"+20% de daño"}""")!)
                && !Simulation.DamagingSkill(JsonNode.Parse("""{"damages":false,"target":"enemy_front","desc":"100% de daño"}""")!),
                "盟友增伤与明确非伤害技能不会误判为攻击");

            var carryRun = JsonNode.Parse("""{"llegada":{"reliquias":1,"blasones":0},"reliquias":[{"id":"ice1","familia":"escarcha"},{"id":"ice2","familia":"escarcha"}],"blasones":[{"id":"badge-ice","familia":"escarcha","umbral":2},{"id":"badge-fire","familia":"brasa","umbral":2}],"buffs":[{"id":"shop","desc":"+8% de vida"}]}""")!;
            var newRelic = carryRun.At("reliquias").A().Last();
            var projected = Cards.Project(carryRun, new Choice("carry", "sima.guarda", new(), card: newRelic));
            assert(projected.Select(c => c.At("id").S()).SequenceEqual(new[] { "ice1", "ice2", "badge-ice" }),
                "继承按新增遗物后的系列门槛保留徽章，不保留失去门槛徽章或商店增益");

            var revivalState = stopState.DeepClone(); var revivalRun = revivalState["sima"]!["run"]!;
            revivalRun["lagrimas"] = 1; var member = revivalRun["heroes"]![0]!;
            member["caido"] = true; member["hp"] = 0; member["anima"] = 90;
            var world = new SearchWorld(revivalState, cfg, new Simulation(false), memory, new Random(1), CancellationToken.None);
            world.Apply(new Choice("revive", "sima.tear", new() { ["heroId"] = "hero" }));
            assert(member.At("hp").N() == 50 && member.At("anima").N() == 0 && !member.At("caido").B() && revivalRun.At("lagrimas").N() == 0,
                "眼泪前推按客户端50%生命、零魂力与一滴消耗处理");
            member["caido"] = true; member["hp"] = 0; member["anima"] = 90;
            revivalRun["santuario"] = new JsonObject { ["levantaPct"] = 25 };
            world.Apply(new Choice("sanctuary", "sima.rest", new() { ["opcion"] = "levantar" }));
            assert(member.At("hp").N() == 25 && member.At("anima").N() == 0, "圣所前推读取本层复活百分比，独立于眼泪规则");

            var restartRoot = Path.Combine(Paths.Data, "restart-fixture"); var first = new SearchMemory(restartRoot);
            string oldId = first.Begin(snapshot, [carry], carry, cfg); first.Confirm(oldId, next);
            string interrupted = first.Begin(next, [carry], carry, cfg);
            var resumed = new SearchMemory(restartRoot); resumed.EnsureLoaded(null, CancellationToken.None);
            string resumedId = resumed.Begin(next, [carry], carry, cfg); resumed.Confirm(resumedId, next); resumed.FinishRun("floor-b", 4, cfg);
            assert(J.Read(Path.Combine(restartRoot, "decisions", oldId + ".json")).At("finalDepth").N() == 4
                && J.Read(Path.Combine(restartRoot, "decisions", interrupted + ".json")).At("return") == null,
                "重启恢复跨层episode映射，终局回填历史已确认操作而排除中断请求");

            var neuralRoot = Path.Combine(Paths.Data, "network-fixture"); Directory.CreateDirectory(Path.Combine(neuralRoot, "decisions"));
            var episodes = Enumerable.Range(0, 200).Select(i => "episode-" + i).ToArray();
            var training = episodes.Where(e => !NeuralLearner.ValidationEpisode(e)).Take(4);
            var validation = episodes.Where(NeuralLearner.ValidationEpisode).Take(2);
            int seq = 0;
            foreach (string episode in training.Concat(validation))
            for (int i = 0; i < 20; i++)
            {
                var state = new double[PlannerFeatures.Count]; state[0] = .2; var x = state.ToArray(); x[1] = 1;
                var row = new LearningDecision { Id = (seq++).ToString(), EpisodeId = episode, RunId = episode,
                    StateFeatures = state, Features = [x, state], Chosen = 0, Confirmed = true, Return = .9, FinalDepth = 10, Policy = [.9, .1] };
                J.Write(Path.Combine(neuralRoot, "decisions", row.Id + ".json"), row);
            }
            var learner = new NeuralLearner(neuralRoot); learner.ScheduleTraining(12, 42).GetAwaiter().GetResult();
            assert(learner.Ready && learner.Version == 1 && learner.Value(new double[PlannerFeatures.Count]) > .5,
                "神经网络真正执行梯度训练，通过按整次下井隔离的留出验证后发布模型");
            var restored = new NeuralLearner(neuralRoot);
            assert(restored.Ready && restored.Version == learner.Version
                && restored.Value(new double[PlannerFeatures.Count]) == learner.Value(new double[PlannerFeatures.Count]), "神经网络权重原子持久化，重启恢复相同预测");
            assert(JsonSerializer.Deserialize<SearchSettings>("{}", J.Options)!.TrainingDevice == "cpu", "旧配置缺训练设备时默认CPU");
            foreach (string selection in new[] { "cpu", "cuda" })
            {
                var settings = new SearchSettings { TrainingDevice = selection }; settings.Validate();
                assert(JsonSerializer.Deserialize<SearchSettings>(JsonSerializer.Serialize(settings, J.Options), J.Options)!.TrainingDevice == selection,
                    selection + "训练设备配置往返保存");
            }
            bool rejected = false;
            try { new SearchSettings { TrainingDevice = "invalid" }.Validate(); } catch { rejected = true; }
            assert(rejected, "后端拒绝未知训练设备");
            var fallbackRoot = Path.Combine(Paths.Data, "fallback-network-fixture");
            Directory.CreateDirectory(Path.Combine(fallbackRoot, "decisions"));
            foreach (var file in Directory.GetFiles(Path.Combine(neuralRoot, "decisions")))
                File.Copy(file, Path.Combine(fallbackRoot, "decisions", Path.GetFileName(file)), true);
            var fallback = new NeuralLearner(fallbackRoot, _ => throw new OutOfMemoryException("测试显存不足"));
            fallback.ScheduleTraining(12, 42, "cuda").GetAwaiter().GetResult();
            var fallbackStatus = JsonSerializer.SerializeToNode(fallback.Status(), J.Options);
            assert(fallback.Ready && fallbackStatus.At("device").S() == "CPU" && fallbackStatus.At("requestedDevice").S() == "cuda"
                && fallbackStatus.At("fallbackReason").S().Contains("测试显存不足"), "CUDA显存失败后实际CPU训练并显示回退原因");
            assert(fallback.Value(new double[PlannerFeatures.Count]) == learner.Value(new double[PlannerFeatures.Count]),
                "CUDA失败从初始权重重训，结果与相同种子的CPU训练一致");
            if (Environment.GetEnvironmentVariable("FROST_TEST_CUDA") == "1")
            {
                CudaTrainingTests.Run(assert);
                File.Delete(Path.Combine(fallbackRoot, "network.json"));
                var gpu = new NeuralLearner(fallbackRoot); gpu.ScheduleTraining(12, 42, "cuda").GetAwaiter().GetResult();
                var gpuStatus = JsonSerializer.SerializeToNode(gpu.Status(), J.Options);
                assert(gpu.Ready && gpu.Version == 1 && gpuStatus.At("device").S() == "CUDA"
                    && gpuStatus.At("fallbackReason") == null && gpuStatus.At("cudaBatches").N() > 0,
                    "真实CUDA训练通过留出验证发布模型，未以CPU回退冒充GPU成功");
                var gpuRestored = new NeuralLearner(fallbackRoot);
                assert(gpuRestored.Ready && gpuRestored.Value(new double[PlannerFeatures.Count]) == gpu.Value(new double[PlannerFeatures.Count]),
                    "CUDA模型兼容CPU推理与重启加载");
                J.Write(Path.Combine(Paths.Data, "cuda-training-evidence.json"), gpu.Status());
            }
            J.Write(Path.Combine(neuralRoot, "network.json"), new { version = 99, weights = new[] { double.MaxValue } });
            assert(!new NeuralLearner(neuralRoot).Ready, "损坏或形状不符的网络不进入真实决策");
        }
        finally { Paths.Data = originalData; }
    }
}
