using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

// A run's botin with numeric nucleos is a temporary balance, not an award.
// Only the server's itemized settlement (nucleos.enMano/alBuzon) is additive.
static class ResourceJournal
{
    internal static JsonObject[] Rounds(IEnumerable<object> receipts, IEnumerable<object> results)
    {
        var actions = receipts.Select(x => JsonSerializer.SerializeToNode(x, J.Options)!).ToArray();
        var ended = results.Select(x => JsonSerializer.SerializeToNode(x, J.Options)!)
            .Where(x => x.At("number").N() > 0).GroupBy(x => (int)x.At("number").N())
            .ToDictionary(g => g.Key, g => g.Last());
        // Older sessions can prove completion even when their round metadata is missing.
        foreach (var action in actions.Where(Terminal))
        {
            int round = (int)action.At("round").N();
            if (round > 0 && !ended.ContainsKey(round)) ended[round] = new JsonObject
            {
                ["number"] = round, ["depth"] = action.At("depth")?.DeepClone(),
                ["ended"] = action.At("time")?.DeepClone()
            };
        }
        return ended.OrderByDescending(p => p.Key).Select(pair =>
        {
            var seen = new Dictionary<string, string>();
            double hand = 0, mailed = 0, shards = 0;
            bool coresKnown = true, shardsKnown = true;
            var finalDepth = Count(pair.Value.At("depth"));
            int expected = finalDepth is { } d && d < int.MaxValue ? (int)d + 1 : 0;
            bool complete = expected > 0 && actions.Any(x => x.At("round").N() == pair.Key
                && Terminal(x) && x.At("depth").N(-1) == expected - 1);
            var depths = new HashSet<int>();
            int count = 0;
            foreach (var action in actions.Where(x => x.At("round").N() == pair.Key && Boundary(x)))
            {
                var raw = action.At("equipmentLoot", "rawEvidence");
                string id = raw.At("commandId").S();
                string fingerprint = raw?.ToJsonString() ?? "";
                if (id != "" && seen.TryGetValue(id, out var prior))
                {
                    if (prior != fingerprint) { complete = coresKnown = shardsKnown = false; }
                    continue;
                }
                if (id != "") seen[id] = fingerprint;
                var loot = raw.At("botin");
                if (!action.At("confirmed").B() || id == ""
                    || raw.At("method").S() != action.At("method").S()
                    || loot.At("nucleos") is not JsonObject)
                {
                    complete = coresKnown = shardsKnown = false;
                    continue;
                }
                count++;
                var depth = Count(action.At("depth"));
                if (depth is not { } current || current >= expected || !depths.Add((int)current))
                    complete = false;
                var received = Count(loot.At("nucleos", "enMano"));
                var sent = Count(loot.At("nucleos", "alBuzon"));
                var fragment = Count(loot.At("esquirlas"));
                if (received is { } h && sent is { } m) { hand += h; mailed += m; }
                else coresKnown = false;
                if (fragment is { } s) shards += s; else shardsKnown = false;
            }
            // Every depth P0 through the terminal depth settles once. A session
            // attached halfway through a run cannot prove the whole run's total.
            if (!complete || count != expected || depths.Count != expected)
                complete = coresKnown = shardsKnown = false;
            complete &= coresKnown && shardsKnown;
            return new JsonObject
            {
                ["round"] = pair.Key, ["depth"] = pair.Value.At("depth")?.DeepClone(),
                ["won"] = pair.Value.At("won")?.DeepClone(), ["ended"] = pair.Value.At("ended")?.DeepClone(),
                ["cores"] = coresKnown ? hand + mailed : null,
                ["mailedCores"] = coresKnown ? mailed : null,
                ["shards"] = shardsKnown ? shards : null,
                ["settlements"] = count, ["expectedSettlements"] = expected,
                ["complete"] = complete, ["source"] = "settlement-receipts"
            };
        }).ToArray();
    }

    static bool Terminal(JsonNode action) => action.At("run").S() != ""
        && action.At("nextRun") != null && action.At("nextRun").S() == ""
        && action.At("confirmed").B();

    static bool Boundary(JsonNode action) => Terminal(action)
        || action.At("method").S() == "sima.guarda";

    static double? Count(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number
        && double.TryParse(node.S(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value >= 0 && value == Math.Truncate(value) ? value : null;

    internal static string Report(IEnumerable<object> receipts, IEnumerable<object> results)
    {
        var rows = Rounds(receipts, results);
        var text = new StringBuilder("## 每轮资源总获取\n\n");
        text.AppendLine("每轮结束后统计一次服务端结算收据。核心总数包含到账及邮寄；缺少结算收据不当作零。奥波币为井内货币，不计作结算获取。\n");
        if (rows.Length == 0) return text.AppendLine("尚无已完成轮次。\n").ToString();
        text.AppendLine("| 轮次 | 最高层数 | 核心总获取 | 其中邮寄 | 碎屑总获取 | 记录状态 |\n|---:|---:|---:|---:|---:|---|");
        foreach (var row in rows)
            text.AppendLine($"| {row.At("round").N()} | {(row.At("depth") == null ? "未记录" : "P" + row.At("depth").N())} | {Value(row.At("cores"))} | {Value(row.At("mailedCores"))} | {Value(row.At("shards"))} | {(row.At("complete").B() ? "结算收据已核验" : "结算收据不完整")} |");
        return text.AppendLine().ToString();
    }

    internal static string RefreshReport(string text, IEnumerable<object> receipts, IEnumerable<object> results)
    {
        // Upgrade a restored report for download while preserving its source file.
        if (text.Contains("## 每轮资源总获取")) return text;
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"(?m)^\| 确认资源差分 \|[^\r\n]*\r?\n(?:\|[^\r\n]*\r?\n)+(?:\r?\n)*资源数值为战斗操作前后游戏快照差分；[^\r\n]*\r?\n", "");
        int section = text.IndexOf("\n## ", StringComparison.Ordinal);
        var totals = Report(receipts, results);
        return section < 0 ? text + "\n" + totals : text.Insert(section + 1, totals + "\n");
    }

    static string Value(JsonNode? value) => value == null ? "未确认" : value.N().ToString("N0");
}
