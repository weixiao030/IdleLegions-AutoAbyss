using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

// Isolated production Step + Bridge test. No game connection or player resources.
static class CardFlowTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var oldData = Paths.Data; var oldGame = Paths.GameData;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-CardFlow-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = Path.Combine(temp, "profile"); Paths.GameData = Path.Combine(temp, "game");
            Directory.CreateDirectory(Paths.Bridge);
            File.WriteAllText(Path.Combine(Paths.Bridge, "session.key"), new string('x', 64));
            var freeze = JsonNode.Parse("""{"id":"es_carambano","familia":"escarcha","desc":"Tus autoataques CONGELAN al objetivo"}""")!;
            var slow = JsonNode.Parse("""{"id":"es_helada","familia":"escarcha","desc":"Tus golpes ralentizan un 15% durante 4 s"}""")!;
            var badge = JsonNode.Parse("""{"id":"es4_hielo","familia":"escarcha","desc":"+40% de daño contra enemigos LENTOS"}""")!;
            var snapshot = JsonNode.Parse("""{"loggedIn":true,"heroes":[{"id":"hero","nombre":"Hero","stats":{"hp":1000,"atk":100,"spd":1,"crit":0}}],"sima":{"run":{"id":"flow-run","nodo":"end","profundidad":0,"llegada":null,"heroes":[{"id":"hero","nombre":"Hero","hp":1000,"hpMax":1000,"anima":100}],"reliquias":[],"blasones":[],"buffs":[],"mapa":{"nodos":{"end":{"tipo":"jefe","fila":8},"start":{"tipo":"santuario","fila":0}}},"destinos":[],"hechos":["end"]}},"peeks":{"end":{"enemigos":[{"name":"Defeated boss"}]},"start":{"enemigos":[]}}}""")!;
            var run = snapshot.At("sima", "run")!;
            var fire = JsonNode.Parse("""{"id":"br_fragua","familia":"brasa","desc":"+50% de daño de tus reacciones elementales, y +5% de daño"}""")!;
            var bounce = JsonNode.Parse("""{"id":"fu_cadena","familia":"fulgor","desc":"Cada 5 autoataques, tu golpe salta a todos, y +2% de daño"}""")!;
            var iceDamage = JsonNode.Parse("""{"id":"es_ventisca","familia":"escarcha","desc":"+22% de daño contra enemigos LENTOS"}""")!;
            run["reliquiaOferta"] = new JsonArray(fire.DeepClone(), slow.DeepClone(), bounce.DeepClone());
            var requests = new List<string>();
            int relicPicks = 0;
            using var stop = new CancellationTokenSource();
            void Publish() { snapshot["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0; J.Write(Path.Combine(Paths.Bridge, "snapshot.json"), snapshot); }
            var service = Task.Run(async () =>
            {
                var seen = "";
                while (!stop.IsCancellationRequested)
                {
                    var command = J.Read(Path.Combine(Paths.Bridge, "command.json"));
                    var id = command.At("id").S();
                    if (id != "" && id != seen)
                    {
                        seen = id; var method = command.At("method").S(); requests.Add(method);
                        if (method == "sima.reliquia")
                        {
                            var selected = run.At("reliquiaOferta").A().Single(c => c.At("id").S() == command.At("args", "id").S());
                            run["reliquias"]!.AsArray().Add(selected.DeepClone()); run["reliquiaOferta"] = null;
                            relicPicks++;
                            if (relicPicks == 1) run["reliquiaOferta"] = new JsonArray(fire.DeepClone(), freeze.DeepClone(), bounce.DeepClone());
                            else if (relicPicks == 2) run["reliquiaOferta"] = new JsonArray(fire.DeepClone(), freeze.DeepClone(), iceDamage.DeepClone(), bounce.DeepClone());
                            else if (relicPicks == 3) run["reliquiaOferta"] = new JsonArray(fire.DeepClone(), iceDamage.DeepClone(), bounce.DeepClone());
                            else if (relicPicks == 4) run["reliquiaOferta"] = new JsonArray(JsonNode.Parse("""{"id":"au_alba","desc":"Empiezas cada sala con un escudo del 25% de tu vida durante 3 s"}"""));
                            else run["blasonPendiente"] = new JsonObject { ["opciones"] = new JsonArray(badge.DeepClone()) };
                        }
                        else if (method == "sima.blason")
                        {
                            run["blasones"]!.AsArray().Add(badge.DeepClone()); run["blasonPendiente"] = null;
                            run["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(fire.DeepClone(), slow.DeepClone(), freeze.DeepClone()), ["heredadas"] = 0 };
                        }
                        else if (method == "sima.guarda")
                        {
                            var selected = run.At("guardaPendiente", "cartas").A().Single(c => c.At("id").S() == command.At("args", "id").S());
                            run["profundidad"] = 1; run["nodo"] = "start"; run["reliquias"] = new JsonArray(selected.DeepClone());
                            run["blasones"] = new JsonArray(); run["buffs"] = new JsonArray(); run["guardaPendiente"] = null;
                            run["llegada"] = new JsonObject { ["reliquias"] = 1, ["blasones"] = 0, ["buffs"] = 0 };
                        }
                        var response = new JsonObject { ["id"] = id, ["ok"] = true, ["confirmed"] = true };
                        snapshot["lastCommand"] = response.DeepClone(); Publish();
                        J.Write(Path.Combine(Paths.Bridge, "response.json"), response);
                    }
                    await Task.Delay(10);
                }
            });
            try
            {
                Publish(); var pilot = new Pilot();
                pilot.Save(new Settings { Simulations = 64, SkipShieldCardsEarly = true });
                async Task Step() => await (Task)typeof(Pilot).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pilot, [false, CancellationToken.None])!;
                await Step();
                check(run.At("reliquias").A().Single().At("id").S() == "es_helada",
                    "真实Step空开局选择冰减速，不被高火增伤或五次电弹跳抢走");
                await Step();
                check(run.At("reliquias").A().Any(c => c.At("id").S() == "es_carambano") && !CardRules.HasInheritedCore(run, pilot.Config),
                    "真实Step第二选取得冻结但未继承，不提前认定跨章核心成型");
                await Step();
                check(run.At("reliquias").A().Count(c => c.At("id").S() == "es_carambano") == 1 && run.At("reliquias").A().Any(c=>c.At("id").S()=="es_ventisca"),
                    "真实Step本章尚未继承时第三选补新增冰伤，不重复抢普通冻结");
                await Step();
                check(run.At("reliquias").A().Select(c => c.At("id").S()).SequenceEqual(["es_helada", "es_carambano", "es_ventisca", "es_ventisca"]),
                    "真实Step没有冻结候选时继续选冰伤，不拿非冰增伤");
                check(pilot.Build(snapshot).Single().Label.Contains("兜底"), "真实Step护盾且非冰的唯一候选启用兜底");
                await Step();
                check(run.At("reliquias").A().Any(c => c.At("id").S() == "au_alba"), "真实Step仅剩策略避让护盾仍派发一次并继续到徽章，不暂停");
                check(pilot.Build(snapshot).Single().Method == "sima.blason", "后续徽章选择明确标为本章徽章，不冒充可继承遗物");
                await Step();
                check(run.At("blasones").A().Any() && !Cards.Project(run, new Choice("next", "sima.guarda", new(), card: freeze)).Any(c => c.At("id").S() == "es4_hielo"),
                    "取得的临时徽章不进入继承投影，不以临时效果挤掉冻结名额");
                await Step();
                var session = J.Read(Path.Combine(Paths.Data, "last-session.json"));
                var review = session.At("chapterReviews").A().Single();
                check(requests.SequenceEqual(["sima.reliquia", "sima.reliquia", "sima.reliquia", "sima.reliquia", "sima.reliquia", "sima.blason", "sima.guarda"]),
                    "四次冰遗物（普通冻结仅一份）、一次护盾兜底、徽章、继承七步真实派发；章末重推演仅本地执行不消费资源");
                check(!review.At("freezeInherited").B() && review.At("depth").N() == 1
                    && review.At("relics").A().Select(c => c.S()).SequenceEqual(["es_helada"])
                    && review.At("prediction", "trials").N() == 64,
                    "无冰川时继承有增益冰牌补冰4，按回执重新推演64次且不虚构冻结");
                var status = JsonSerializer.SerializeToNode(pilot.Status(), J.Options);
                check(status.At("cardState", "iceOnly").B() && !status.At("cardState", "freezeInherited").B()
                    && status.At("choices").A().Single().At("method").S() == "local.chapterReview",
                    "控制台区分实际冻结继承与冰4前置，并显示章节复核，不残留上一章继承候选");
                run["reliquiaOferta"] = new JsonArray(freeze.DeepClone(), bounce.DeepClone(), fire.DeepClone()); Publish();
                await Step();
                check(run.At("reliquias").A().Count(c => c.At("id").S() == "es_carambano") == 1,
                    "真实Step继承冰牌后仍补冰4，只有普通冻结时用它增加冰系张数");
                run["blasonPendiente"] = null; run["reliquias"] = new JsonArray(freeze.DeepClone()); Publish();
                var repeat = pilot.ReviewChapter(snapshot, CancellationToken.None);
                snapshot["peeks"]!["start"]!["enemigos"] = JsonNode.Parse("""[{"name":"Stale boss"},{"name":"Stale escort"}]""");
                run["mapa"]!["nodos"]!["start"]!["tipo"] = "jefe";
                var repeat2 = pilot.ReviewChapter(snapshot, CancellationToken.None);
                check(JsonSerializer.Serialize(repeat.Forecast, J.Options) == JsonSerializer.Serialize(repeat2.Forecast, J.Options),
                    "章末复核使用新章普通房间先验，不重打旧Boss，不把深度再加一层");
                var flame = JsonNode.Parse("""{"id":"br_incendio","familia":"brasa","desc":"Tus autoataques dejan al objetivo ARDIENDO, y +4% de daño"}""")!;
                run["profundidad"] = 3; run["reliquias"] = JsonNode.Parse("""[{"id":"es_carambano"},{"id":"br_yesca","desc":"Tus autoataques dejan al objetivo ARDIENDO"}]""");
                run["llegada"] = new JsonObject { ["reliquias"] = 2 };
                run["reliquiaOferta"] = new JsonArray(
                    JsonNode.Parse("""{"id":"fu_descarga","desc":"Tus autoataques ELECTRIFICAN al objetivo, y +5% de daño"}"""),
                    JsonNode.Parse("""{"id":"au_alba","desc":"Empiezas cada sala con un escudo del 10% de tu vida durante 3 s"}"""), flame.DeepClone());
                pilot.Config.ShieldSkipFloors = 20; Publish();
                await Step();
                check(run.At("reliquias").A().Any(c => c.At("id").S() == "br_incendio") && requests.Last() == "sima.reliquia",
                    "截图回归真实Step自动派发第三张烈焰并确认成功，不暂停等待玩家处理");
                pilot.Config.Simulations = 0;
                check(pilot.ReviewChapter(snapshot, CancellationToken.None).Forecast == null, "章节复核尊重关闭抽样，不伪造胜率");

                // Exercise the actual dispatcher and receipt path for every
                // search strategy, using the isolated bridge above.
                foreach (int strategy in new[] { 5, 6, 7 })
                {
                    var searchPilot = new Pilot();
                    searchPilot.Save(new Settings { StrategyId = strategy, Simulations = 1,
                        Search = new() { Iterations = 8, AdaptiveIterations = 8, NeuralIterations = 8,
                            Horizon = 2, Workers = 1, AutoTrain = false } });
                    run["blasonPendiente"] = null; run["guardaPendiente"] = null;
                    run["reliquias"] = new JsonArray(JsonNode.Parse("{\"id\":\"es_glaciar\",\"familia\":\"escarcha\"}"));
                    run["llegada"] = new JsonObject { ["reliquias"] = 1 };
                    run["reliquiaOferta"] = new JsonArray(bounce.DeepClone()); Publish();
                    int priorRequests = requests.Count;
                    await (Task)typeof(Pilot).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(searchPilot, [false, CancellationToken.None])!;
                    check(requests.Count == priorRequests + 1 && requests.Last() == "sima.reliquia",
                        $"策略{strategy}通过生产Step派发且仅派发一次唯一合法选牌请求");
                    var rows = Directory.GetFiles(Path.Combine(Paths.Data, "learning-v1", "decisions"), "*.json").Select(J.Read);
                    check(rows.Any(r => r.At("strategy").N() == strategy && r.At("confirmed").B()
                        && r.At("before") != null && r.At("after") != null
                        && r.At("features").A().Count() == 1 && r.At("policy") == null),
                        $"策略{strategy}唯一候选经生产回执确认后保存前后快照，不伪造搜索标签，隔离测试不写用户数据");
                }
            }
            finally { stop.Cancel(); await service; }
        }
        finally { Paths.Data = oldData; Paths.GameData = oldGame; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
