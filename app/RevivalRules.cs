using System.Text.Json.Nodes;

namespace FrostAbyss;

static class RevivalRules
{
    internal static Choice? Choice(JsonNode? run)
    {
        // Resolve mandatory reward choices before issuing a separate tear action.
        if (run == null || run.At("lagrimas").N() < 1
            || run.At("blasonPendiente", "opciones").A().Any()
            || run.At("reliquiaOferta").A().Any() || run.At("guardaPendiente", "cartas").A().Any()) return null;
        var down = run.At("heroes").A().FirstOrDefault(h => h.At("caido").B());
        if (down == null) return null;
        var id = down.At("id").S();
        if (string.IsNullOrWhiteSpace(id)) throw new Exception("倒地队员缺少英雄ID，已暂停，无法安全使用眼泪。");
        return new("使用眼泪复活 " + J.Name(down), "sima.tear", new() { ["heroId"] = id }, .8)
            { Reason = "逐个复活倒地队员，确认站起后再继续前进" };
    }
    internal static string? Error(JsonNode before, JsonNode? after, string heroId)
    {
        const string paused = "已暂停，不会自动重复消耗眼泪；请检查游戏后继续。";
        if (after == null || after.At("id").S() != before.At("id").S())
            return "复活回执的深渊状态不一致，" + paused;
        var hero = after.At("heroes").A().FirstOrDefault(h => h.At("id").S() == heroId);
        if (hero == null || hero.At("caido").B() || hero.At("hp").N() <= 0)
            return "复活请求返回成功，但目标队员尚未确认站起，" + paused;
        if (before.At("lagrimas").N() - after.At("lagrimas").N() != 1)
            return "复活后眼泪数量变化不是1，" + paused;
        return null;
    }
}
