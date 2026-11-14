using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class StorageTests
{
    internal static void Run(Action<bool, string> check)
    {
        string oldData = Paths.Data;
        string fixture = Path.Combine(Path.GetTempPath(), "frost-storage-" + Guid.NewGuid().ToString("N"));
        Paths.Data = fixture; Directory.CreateDirectory(fixture);
        try
        {
            string battles = Path.Combine(fixture, "battles"), corpus = Path.Combine(fixture, "learning-v1", "decisions");
            JsonNode Battle(int n)
            {
                var b = SimulationSafetyTests.Battle(n).At("lastBattle")!.DeepClone();
                b["time"] = 100 + n; b["before"]!["id"] = "run-" + n;
                return b;
            }
            void StoreBattle(int n) => J.Write(Path.Combine(battles, n + ".json"), Battle(n));
            StoreBattle(1); StoreBattle(2);
            J.Write(Path.Combine(corpus, "decision.json"), new LearningDecision { Id = "decision", EpisodeId = "finished", RunId = "run-1", Confirmed = true, Return = .6,
                Before = new JsonObject { ["large"] = new string('x', 50000) }, After = new JsonObject { ["large"] = new string('y', 50000) } });
            (Simulation Model, SearchMemory Memory) Load()
            {
                var model = new Simulation(false); var memory = new SearchMemory();
                memory.EnsureLoaded(model, CancellationToken.None); return (model, memory);
            }
            string Signature(Simulation model) => JsonSerializer.Serialize(model.CaptureState(), J.Options);
            var cold = Load(); var warm = Load();
            check(Signature(cold.Model) == Signature(warm.Model) && warm.Model.Battles == 2, "历史检查点重启恢复相同完整模型，未减少历史样本");
            check(JsonSerializer.Serialize(cold.Memory.Status(), J.Options) == JsonSerializer.Serialize(warm.Memory.Status(), J.Options), "精简决策索引保留统计、episode和校准状态");
            StoreBattle(3); var incremental = Load();
            File.Delete(Path.Combine(fixture, "learning-v1", "cache", "battle-model-v1.json"));
            var rebuilt = Load();
            check(Signature(incremental.Model) == Signature(rebuilt.Model) && incremental.Model.Battles == 3, "新增历史只追加学习，结果与全量顺序重建一致");
            var changed = Battle(1); changed["result"]!["durationSec"] = 30; J.Write(Path.Combine(battles, "1.json"), changed);
            var updated = Load(); File.Delete(Path.Combine(fixture, "learning-v1", "cache", "battle-model-v1.json"));
            check(Signature(updated.Model) == Signature(Load().Model), "改写旧战报使检查点失效，不继续使用过期拟合");
            StoreBattle(-5); var backfilled = Load(); File.Delete(Path.Combine(fixture, "learning-v1", "cache", "battle-model-v1.json"));
            check(Signature(backfilled.Model) == Signature(Load().Model), "补录更早战报按时间重建，不破坏英雄平滑顺序");
            string cacheFile = Path.Combine(fixture, "learning-v1", "cache", "battle-model-v1.json");
            File.WriteAllText(cacheFile, "broken");
            check(Load().Model.Battles == 4, "损坏的缓存自动回退权威历史并重建");
            var state = SimulationSafetyTests.Fixture(); state["sima"]!["run"]!["id"] = "live";
            var cfg = new Settings { Search = new() { AutoTrain = false } };
            var choice = new Choice("forced", "sima.move", new());
            string id = cold.Memory.Begin(state, [choice], choice, cfg); cold.Memory.Confirm(id, state);
            J.Write(Path.Combine(fixture, "settings.json"), new { keep = "settings" });
            J.Write(Path.Combine(fixture, "learning-v1", "network.json"), new { keep = "network" });
            var storage = new DataStorage(); var before = storage.Usage(true);
            long actual = Directory.GetFiles(fixture, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            check(before.TotalBytes == actual && before.TotalBytes == before.Groups.Sum(g => g.Bytes) && before.TotalGiB == actual / 1073741824d, "数据管理按实际文件字节统计总量、分类和GB");
            var sourceFiles = J.HistoryFiles(battles).Concat(J.HistoryFiles(corpus)).ToDictionary(f => f, File.ReadAllBytes);
            var stamps = HistoryCache.Scan(battles);
            storage.Run(new("compact"), CancellationToken.None);
            check(sourceFiles.All(p => { using var stream = J.OpenRead(p.Key); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return p.Value.SequenceEqual(bytes.ToArray()); }), "无损压缩后的每份权威记录逐字节一致，旧逻辑路径仍能读取");
            check(HistoryCache.Unchanged(stamps, HistoryCache.Scan(battles)), "无损压缩保持内容长度和时间戳，模型检查点可继续复用");
            check(storage.Usage(true).TotalBytes < before.TotalBytes, "重复快照压缩后实际占用减少");
            check(Load().Model.Battles == 4 && new Legacy131.Simulation().Battles == 4, "共享模型和策略二均可读取压缩历史");
            cold.Memory.FinishRun("live", 6, cfg);
            check(J.Read(Path.Combine(corpus, id + ".json")).At("return") != null, "压缩后的决策仍可回填终局标签，不丢训练数据");
            bool refused = false;
            try { storage.Run(new("delete", ["battles"]), CancellationToken.None); } catch { refused = true; }
            check(refused && J.HistoryFiles(battles).Length == 4, "删除未确认时拒绝执行，保留全部历史");
            refused = false;
            try { storage.Run(new("delete", ["../", "model"], true), CancellationToken.None); } catch { refused = true; }
            check(refused && File.Exists(Path.Combine(fixture, "learning-v1", "network.json")), "删除仅接受固定数据类别，拒绝任意路径和已训练网络");
            storage.Run(new("delete", ["battles", "decisions"], true), CancellationToken.None);
            check(J.HistoryFiles(battles).Length == 0 && J.HistoryFiles(corpus).Length == 0
                && J.Read(Path.Combine(fixture, "settings.json")).At("keep").S() == "settings"
                && J.Read(Path.Combine(fixture, "learning-v1", "network.json")).At("keep").S() == "network", "分类删除历史和学习记录，保留参数与已发布网络");
            check(Load().Model.Battles == 0, "删除证据后重建空经验模型，缓存不会复活已删战报");
        }
        finally { Paths.Data = oldData; if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }
    }
}
