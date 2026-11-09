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
            run["reliquiaOferta"] = new JsonArray(slow.DeepClone(), freeze.DeepClone());
            var requests = new List<string>();
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
                            run["blasonPendiente"] = new JsonObject { ["opciones"] = new JsonArray(badge.DeepClone()) };
                        }
                        else if (method == "sima.blason")
                        {
                            run["blasones"]!.AsArray().Add(badge.DeepClone()); run["blasonPendiente"] = null;
                            run["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(slow.DeepClone(), freeze.DeepClone()), ["heredadas"] = 0 };
                        }
                        else if (method == "sima.guarda")
                        {
                            run["profundidad"] = 1; run["nodo"] = "start"; run["reliquias"] = new JsonArray(freeze.DeepClone());
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
                pilot.Save(new Settings { Simulations = 64, SkipShieldCardsEarly = false });
                async Task Step() => await (Task)typeof(Pilot).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pilot, [false, CancellationToken.None])!;
                await Step();
                check(run.At("reliquias").A().Any(c => c.At("id").S() == "es_carambano") && !CardRules.HasInheritedCore(run, pilot.Config),
                    "真实Step第一选取得冻结但未继承，不提前认定跨章核心成型");
                check(pilot.Build(snapshot).Single().Label.Contains("本章徽章"), "第二次选择明确标为本章徽章，不冒充可继承遗物");
                await Step();
                check(run.At("blasones").A().Any() && !Cards.Project(run, new Choice("next", "sima.guarda", new(), card: freeze)).Any(c => c.At("id").S() == "es4_hielo"),
                    "第二次取得的徽章不进入继承投影，不以临时效果挤掉冻结名额");
                await Step();
                var session = J.Read(Path.Combine(Paths.Data, "last-session.json"));
                var review = session.At("chapterReviews").A().Single();
                check(requests.SequenceEqual(["sima.reliquia", "sima.blason", "sima.guarda"]),
                    "遗物、徽章、继承三步真实派发；章末重推演仅本地执行不消费资源");
                check(review.At("freezeInherited").B() && review.At("depth").N() == 1
                    && review.At("relics").A().Select(c => c.S()).SequenceEqual(["es_carambano"])
                    && review.At("prediction", "trials").N() == 64,
                    "继承成功后按下一章确认保留的冻结重新推演64次并落盘，临时徽章已清空");
                var status = JsonSerializer.SerializeToNode(pilot.Status(), J.Options);
                check(status.At("cardState", "freezeInherited").B()
                    && status.At("choices").A().Single().At("method").S() == "local.chapterReview",
                    "控制台显示已继承核心和章节复核，不残留上一章继承候选");
                var repeat = pilot.ReviewChapter(snapshot, CancellationToken.None);
                snapshot["peeks"]!["start"]!["enemigos"] = JsonNode.Parse("""[{"name":"Stale boss"},{"name":"Stale escort"}]""");
                run["mapa"]!["nodos"]!["start"]!["tipo"] = "jefe";
                var repeat2 = pilot.ReviewChapter(snapshot, CancellationToken.None);
                check(JsonSerializer.Serialize(repeat.Forecast, J.Options) == JsonSerializer.Serialize(repeat2.Forecast, J.Options),
                    "章末复核使用新章普通房间先验，不重打旧Boss，不把深度再加一层");
                pilot.Config.Simulations = 0;
                check(pilot.ReviewChapter(snapshot, CancellationToken.None).Forecast == null, "章节复核尊重关闭抽样，不伪造胜率");
            }
            finally { stop.Cancel(); await service; }
        }
        finally { Paths.Data = oldData; Paths.GameData = oldGame; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
