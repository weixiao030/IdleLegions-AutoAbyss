using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

// Settlement receipts are authoritative. Inventory additions are observations,
// never proof that an individual monster dropped an item.
static class DropJournal
{
    internal static JsonNode? ForCommand(JsonNode? snapshot, JsonNode? outcome)
    {
        var raw = snapshot.At("lastLoot");
        return outcome.At("id").S() != "" && raw.At("commandId").S() == outcome.At("id").S()
            ? Read(raw) : null;
    }

    internal static JsonObject Read(JsonNode? raw)
    {
        var receipt = raw.At("botin");
        bool authoritative = receipt is JsonObject;
        var source = authoritative ? "settlement-receipt" : raw.At("inventoryObserved").B() ? "inventory-delta" : "unavailable";
        var items = (authoritative ? receipt.At("piezas") : raw.At("inventoryAdded")).A()
            .Where(x => x is JsonObject).Select(x => new
            {
                id = x.At("id").S(),
                name = x.At("displayName").S(x.At("nameZh").S(x.At("nombre").S(x.At("name").S(x.At("nameEn").S(x.At("nameEs").S("名称未提供")))))),
                rarity = x.At("rarity").S(receipt.At("rareza").S()),
                source
            }).ToArray();
        return new JsonObject
        {
            ["rawEvidence"] = raw?.DeepClone(),
            ["source"] = source,
            ["items"] = JsonSerializer.SerializeToNode(items, J.Options),
            ["mailedMythics"] = receipt.At("miticosAlBuzon")?.DeepClone(),
            ["pendingMythics"] = receipt.At("miticosPendientes")?.DeepClone(),
            ["shards"] = receipt.At("esquirlas")?.DeepClone(),
            ["coresInHand"] = receipt.At("nucleos", "enMano")?.DeepClone(),
            ["coresMailed"] = receipt.At("nucleos", "alBuzon")?.DeepClone()
        };
    }

    internal static object[] Records(IEnumerable<object> receipts) => receipts
        .Select(x => JsonSerializer.SerializeToNode(x, J.Options)!)
        .Where(x => x.At("equipmentLoot") is JsonObject)
        .Select(x => (object)new { round = x.At("round").N(), depth = x.At("depth").N(), method = x.At("method").S(), evidence = x.At("equipmentLoot")!.DeepClone() })
        .ToArray();

    internal static string Report(IEnumerable<object> receipts)
    {
        var rows = Records(receipts).Select(x => JsonSerializer.SerializeToNode(x, J.Options)!).ToArray();
        var text = new StringBuilder("## 装备与结算收据\n\n");
        text.AppendLine("结算收据列出实际提供的装备名称与稀有度；背包差分只证明该操作期间新增，不能定位到某只怪物。邮寄及待领取只有数量时不猜名称。旧桥接或旧回执缺字段记为未确认。\n");
        if (rows.Length == 0) return text.AppendLine("本段没有装备结算证据；升级至桥接0.3.3后可采集新操作。\n").ToString();
        text.AppendLine("| 轮次 | 深度 | 操作 | 装备 | 稀有度 | 来源 |\n|---:|---:|---|---|---|---|");
        var notes = new List<string>();
        foreach (var row in rows)
        {
            var e = row.At("evidence");
            foreach (var item in e.At("items").A())
                text.AppendLine($"| {row.At("round").N()} | P{row.At("depth").N()} | {row.At("method").S()} | {Cell(item.At("name").S())} (`{Cell(item.At("id").S())}`) | {Cell(item.At("rarity").S())} | {(e.At("source").S() == "settlement-receipt" ? "结算收据" : "背包新增，归因未确认")} |");
            if (!e.At("items").A().Any())
                text.AppendLine($"| {row.At("round").N()} | P{row.At("depth").N()} | {row.At("method").S()} | {(e.At("source").S() == "unavailable" ? "未确认" : "未列出新增装备")} | | {e.At("source").S()} |");
            if (e.At("mailedMythics") != null || e.At("pendingMythics") != null)
                notes.Add($"第{row.At("round").N()}轮 P{row.At("depth").N()}：寄往邮箱神话 {Count(e.At("mailedMythics"))}；待领取 {Count(e.At("pendingMythics"))}，名称未提供。");
        }
        text.AppendLine();
        foreach (var note in notes) text.AppendLine(note + "\n");
        return text.AppendLine().ToString();
    }

    static string Count(JsonNode? n) => n == null ? "未确认" : n.N().ToString("N0");
    static string Cell(string s) => s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Replace("`", "'");
}
