using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class ResourceJournalTests
{
    internal static void Run(Action<bool, string> check)
    {
        static JsonNode Receipt(string id, int depth, int hand, int mailed, int shards, bool terminal = false) =>
            JsonSerializer.SerializeToNode(new
            {
                round = 1, depth, confirmed = true, run = "run-" + depth,
                nextRun = terminal ? "" : "run-" + (depth + 1), method = terminal ? "sima.fight" : "sima.guarda",
                equipmentLoot = new { rawEvidence = new
                {
                    commandId = id, method = terminal ? "sima.fight" : "sima.guarda",
                    botin = new { nucleos = new { enMano = hand, alBuzon = mailed }, esquirlas = shards }
                }}
            }, J.Options)!;
        var chapter = Receipt("chapter-0", 0, 2, 1, 4);
        var final = Receipt("end-1", 1, 3, 0, 2, true);
        var interim = JsonNode.Parse("""{"round":1,"depth":1,"confirmed":true,"method":"sima.move","run":"run-1","nextRun":"run-1","equipmentLoot":{"rawEvidence":{"commandId":"move","method":"sima.move","botin":{"nucleos":900,"esquirlas":800,"obolos":700}}}}""")!;
        var results = new object[] { new { number = 1, won = false, depth = 1, coreDelta = 100, shardDelta = 200 } };
        var receipts = new object[] { chapter, interim, final, chapter.DeepClone() };
        var original = chapter.ToJsonString();
        var row = ResourceJournal.Rounds(receipts, results).Single();
        check(row.At("cores").N() == 6 && row.At("mailedCores").N() == 1 && row.At("shards").N() == 6
            && row.At("settlements").N() == 2 && row.At("complete").B(),
            "整轮按真实结算相加，核心含邮寄；排除暂存余额、账号净差分和重复命令");
        check(chapter.ToJsonString() == original, "资源汇总不改写原始回执");
        check(ResourceJournal.Rounds([chapter, interim], []).Length == 0, "未结束轮次不发布整轮总获取");
        check(ResourceJournal.Rounds(receipts, []).Single().At("round").N() == 1,
            "旧记录缺少轮次元数据时可由确认的终局操作恢复整轮展示");
        var partial = ResourceJournal.Rounds([final], results).Single();
        check(partial.At("cores") == null && partial.At("expectedSettlements").N() == 2 && !partial.At("complete").B(),
            "中途接管或缺少章节结算时不把部分奖励冒充整轮总获取");
        var duplicateDepth = Receipt("another-command", 0, 1, 0, 1);
        check(ResourceJournal.Rounds([chapter, duplicateDepth, final], results).Single().At("cores") == null,
            "同层出现不同结算命令时不重复累计成完整总数");
        var missingFinal = ResourceJournal.Rounds([chapter], results).Single();
        check(missingFinal.At("cores") == null && !missingFinal.At("complete").B(),
            "缺少终局操作时不把章节小计冒充整轮总计");
        var wrong = final.DeepClone();
        wrong["equipmentLoot"]!["rawEvidence"]!["method"] = "sima.move";
        check(ResourceJournal.Rounds([chapter, wrong], results).Single().At("cores") == null,
            "错配操作的结算收据不能用于总获取");
        var missing = final.DeepClone();
        missing["equipmentLoot"]!["rawEvidence"]!["botin"]!["esquirlas"] = null;
        row = ResourceJournal.Rounds([chapter, missing], results).Single();
        check(row.At("cores").N() == 6 && row.At("shards") == null && !row.At("complete").B(),
            "单项缺失保留未知，不伪造零也不影响有完整证据的另一项");
        var invalid = final.DeepClone();
        invalid["equipmentLoot"]!["rawEvidence"]!["botin"]!["nucleos"]!["enMano"] = "3";
        check(ResourceJournal.Rounds([chapter, invalid], results).Single().At("cores") == null,
            "奖励字段类型不正确时保持未确认");
        var conflict = final.DeepClone();
        conflict["equipmentLoot"]!["rawEvidence"]!["botin"]!["esquirlas"] = 9;
        check(ResourceJournal.Rounds([chapter, final, conflict], results).Single().At("shards") == null,
            "同命令出现冲突结算不重复入账且不宣称完整");
        var zero = ResourceJournal.Rounds([Receipt("zero", 0, 0, 0, 0, true)], []).Single();
        check(zero.At("cores").N(-1) == 0 && zero.At("shards").N(-1) == 0 && zero.At("complete").B(),
            "真实零奖励与缺失结算保持可区分");
        var oldReport = "# 自动下潜报告\n\n## 逐层报告\n\n| 确认资源差分 | 核心 | 碎屑 | 奥波币 |\n|---|---:|---:|---:|\n| 第1轮 P0 | 1 | 2 | -3 |\r\n资源数值为战斗操作前后游戏快照差分；快照缺失记为未确认，不代表独立装备掉落。\n\n其他战斗数据保留。\n";
        var refreshed = ResourceJournal.RefreshReport(oldReport, receipts, results);
        check(refreshed.Contains("## 每轮资源总获取") && !refreshed.Contains("确认资源差分")
            && refreshed.Contains("其他战斗数据保留。")
            && ResourceJournal.RefreshReport(refreshed, receipts, results) == refreshed,
            "重启后的历史报告下载替换旧资源差分表，保留战斗数据且不重复插入");
        var report = ResourceJournal.Report(receipts, results);
        check(report.Contains("每轮资源总获取") && report.Contains("核心总数包含到账及邮寄") && report.Contains("最高层数") && report.Contains("| 1 | P1 | 6 | 1 | 6 |") && !report.Contains("核心增量"),
            "导出报告使用同一整轮结算统计口径");
    }
}
