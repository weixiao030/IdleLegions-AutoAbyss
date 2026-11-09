using System.Text.Json.Nodes;

namespace FrostAbyss;

static class EventRules
{
    internal static Choice[] Choices(JsonNode snapshot)
    {
        var offered = snapshot.At("omen", "opciones").A().ToArray();
        if (offered.Length == 0)
            return [new("查看迷雾事件", "sima.omen", new() { ["opcion"] = "" })];
        // The client defaults a missing disponible to true. A false value is
        // authoritative for soul/HP/coin and other server-side prerequisites.
        var available = offered.Where(o => o.At("disponible") == null || o.At("disponible").B())
            .Where(o => !string.IsNullOrWhiteSpace(o.At("id").S())).ToArray();
        if (available.Length == 0)
            throw new Exception("事件当前没有可用选项，已暂停等待状态更新；不会重复派发无法支付的选择。");
        return available.Select(o =>
        {
            var t = J.Text(o); bool pass = o.At("id").S() == "pasar";
            double risk = new[] { "vida", "daño", "pierde", "sacrific", "combate", "muerte", "生命", "失去", "战斗" }.Any(t.Contains) ? .7 : .1;
            return new Choice("事件：" + J.Name(o), "sima.omen", new() { ["opcion"] = o.At("id").S() }, pass ? .2 : .5, pass ? 0 : risk)
            {
                Reason = pass && available.Length < offered.Length
                    ? "魂力、生命或货币条件不足的选项已排除，选择可用的径直前行"
                    : "只比较客户端允许选择的事件选项，不尝试无法支付的交易"
            };
        }).ToArray();
    }
}
