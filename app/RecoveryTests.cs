using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class RecoveryTests
{
    static JsonObject Fixture() => JsonNode.Parse("""{"version":"0.3.4","loggedIn":true,"sima":{"run":{"id":"run-a","nodo":"boss","hechos":[]}}} """)!.AsObject();
    static Bridge FastBridge() => new() { RecoveryDelays = Enumerable.Repeat(TimeSpan.FromMilliseconds(20), 6).ToArray() };

    public static async Task Run(Action<bool, string> check)
    {
        var before = Fixture();
        var after = before.DeepClone();
        after["sima"]!["run"] = null;
        after["lastBattle"] = JsonNode.Parse("""{"time":100,"method":"sima.fight","commandId":"command-a","before":{"id":"run-a","nodo":"boss"},"result":{"win":true}}""");
        bool Completed(JsonNode snapshot, string id = "command-a") => RecoveryRules.Completed("sima.fight", new(), before, snapshot, id);
        check(Completed(after), "最终战斗胜利后run已结束，匹配战斗回执仍可核验完成");
        after["lastBattle"]!["result"]!["win"] = false;
        check(Completed(after), "最终战斗失败后run已结束，同样可核验操作已完成");
        foreach (var field in new[] { "id", "nodo" })
        {
            var unrelated = after.DeepClone(); unrelated["lastBattle"]!["before"]![field] = "unrelated";
            check(!Completed(unrelated), "终局核验拒绝不匹配的战斗" + field);
        }
        check(!Completed(after, "another-command"), "终局核验拒绝不同命令ID的回执");
        var staleBefore = before.DeepClone(); staleBefore["lastBattle"] = after["lastBattle"]!.DeepClone();
        check(!RecoveryRules.Completed("sima.fight", new(), staleBefore, after, "command-a"), "旧战斗回执不能证明新战斗已完成");
        var missing = after.DeepClone(); missing["lastBattle"] = null;
        check(!Completed(missing), "run消失但无战斗证据时仍暂停，不猜胜负");
        missing = after.DeepClone(); missing["lastBattle"]!["result"] = new JsonObject();
        check(!Completed(missing), "战斗回执缺少胜负字段不能误确认");
        missing = after.DeepClone(); missing["sima"]!["run"] = JsonNode.Parse("""{"id":"run-b","nodo":"boss"}""");
        check(!Completed(missing), "其他深渊进度不能证明原战斗完成");
        after = before.DeepClone();
        after["lastAction"] = JsonNode.Parse("""{"id":"command-a","method":"sima.fight","ok":true,"confirmed":true}""");
        check(Completed(after), "只读刷新后仍可通过保留的原命令成功回执核验");
        check(!Completed(after, "another-command"), "原命令回执必须精确匹配命令ID");
        after["lastAction"]!["confirmed"] = false;
        check(!Completed(after), "未确认的命令回执不能解除暂停");
        foreach (var version in new[] { "0.3.1", "0.3.3", "unknown" })
            check(Bridge.NeedsUpdate(new JsonObject { ["version"] = version }), "识别过旧或未知桥接版本 " + version);
        foreach (var version in new[] { "0.3.4", "0.3.10" })
            check(!Bridge.NeedsUpdate(new JsonObject { ["version"] = version }), "不误报当前或更新桥接 " + version);

        var oldData = Paths.Data; var oldGame = Paths.GameData;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-Recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = Path.Combine(temp, "profile"); Paths.GameData = Path.Combine(temp, "game");
            Directory.CreateDirectory(Paths.Bridge);
            File.WriteAllText(Path.Combine(Paths.Bridge, "session.key"), new string('x', 64));
            var requests = new ConcurrentQueue<string>();
            var state = Fixture(); var mode = "late"; var refreshes = 0;
            var busyUntil = 0L; var busyCommand = "";
            using var stop = new CancellationTokenSource();
            void Publish() { state["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0; J.Write(Path.Combine(Paths.Bridge, "snapshot.json"), state); }
            var service = Task.Run(async () =>
            {
                var seen = "";
                while (!stop.IsCancellationRequested)
                {
                    var command = J.Read(Path.Combine(Paths.Bridge, "command.json")); var id = command.At("id").S();
                    if (id != "" && id != seen)
                    {
                        seen = id; var method = command.At("method").S(); requests.Enqueue(method);
                        var response = new JsonObject { ["id"] = id, ["ok"] = false, ["code"] = "receipt_missing", ["error"] = "fixture: original receipt lost" };
                        if (mode == "refused") { response["code"] = "invalid_action"; response["error"] = "fixture: game refused"; }
                        if (mode == "busy" && method == "sima.fight")
                        {
                            state["busy"] = true; busyCommand = id;
                            busyUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2;
                        }
                        if (method == "local.refresh")
                        {
                            refreshes++;
                            response["ok"] = mode != "refresh-failed" || refreshes > 2;
                            if ((mode == "late" && refreshes >= 4) || (mode == "refresh-failed" && refreshes > 2))
                                state["sima"]!["run"]!["hechos"] = new JsonArray("boss");
                        }
                        state["lastCommand"] = response.DeepClone(); Publish();
                        J.Write(Path.Combine(Paths.Bridge, "response.json"), response);
                    }
                    if (mode == "busy" && busyUntil != 0 && Stopwatch.GetTimestamp() >= busyUntil)
                    {
                        state["busy"] = false;
                        state["lastAction"] = new JsonObject { ["id"] = busyCommand, ["method"] = "sima.fight", ["ok"] = true, ["confirmed"] = true };
                        busyUntil = 0; Publish();
                    }
                    await Task.Delay(5);
                }
            });
            try
            {
                Publish();
                var outcome = await FastBridge().Send("sima.fight", new(), CancellationToken.None);
                check(outcome.At("recovered").B() && refreshes == 4 && requests.Count(m => m == "sima.fight") == 1, "慢同步超过旧两次核验后自动恢复，战斗仍仅派发一次");
                mode = "refresh-failed"; state = Fixture(); refreshes = 0; requests.Clear(); Publish();
                outcome = await FastBridge().Send("sima.fight", new(), CancellationToken.None);
                check(outcome.At("recovered").B() && refreshes == 3 && requests.Count(m => m == "sima.fight") == 1, "前两次只读查询失败后仍可恢复，不重复战斗");
                mode = "unknown"; state = Fixture(); refreshes = 0; requests.Clear(); Publish(); var paused = false;
                try { await FastBridge().Send("sima.fight", new(), CancellationToken.None); } catch (UnknownOutcomeException) { paused = true; }
                check(paused && refreshes == 6 && requests.Count(m => m == "sima.fight") == 1, "六次核验仍无证据时安全暂停且只发一次战斗");
                state = Fixture(); refreshes = 0; requests.Clear(); Publish(); paused = false;
                var bounded = new Bridge { RecoveryDelays = [TimeSpan.FromSeconds(1)], RecoveryTimeout = TimeSpan.FromMilliseconds(50) };
                try { await bounded.Send("sima.fight", new(), CancellationToken.None); } catch (UnknownOutcomeException) { paused = true; }
                check(paused && refreshes == 0 && requests.Count == 1, "核验总时限耗尽后停止等待，不发出超出窗口的查询");
                mode = "busy"; state = Fixture(); refreshes = 0; requests.Clear(); Publish();
                var waiting = new Bridge { RecoveryDelays = Enumerable.Repeat(TimeSpan.FromMilliseconds(100), 6).ToArray() };
                outcome = await waiting.Send("sima.fight", new(), CancellationToken.None);
                check(outcome.At("recovered").B() && refreshes == 0 && requests.Count == 1, "桥接忙时不覆盖命令邮箱，等待原命令完成后使用保留回执恢复");
                mode = "unknown";
                state = Fixture(); refreshes = 0; requests.Clear(); Publish(); using var cancel = new CancellationTokenSource();
                var bridge = FastBridge(); bridge.Notice = _ => cancel.Cancel(); var cancelled = false;
                try { await bridge.Send("sima.fight", new(), cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
                check(cancelled && refreshes == 0 && requests.Count == 1, "核验等待响应手动暂停，取消后不再查询或发出战斗");
                mode = "refused"; state = Fixture(); refreshes = 0; requests.Clear(); Publish(); paused = false;
                try { await FastBridge().Send("sima.fight", new(), CancellationToken.None); } catch (Exception e) { paused = e is not UnknownOutcomeException; }
                check(paused && refreshes == 0 && requests.Count == 1, "明确拒绝的操作立即显示真实原因，不进入慢网络恢复");
            }
            finally { stop.Cancel(); await service; }
        }
        finally { Paths.Data = oldData; Paths.GameData = oldGame; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
