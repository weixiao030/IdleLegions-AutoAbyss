using System.Text.Json;

namespace FrostAbyss;

sealed record LoopRequest(string Mode, int Count = 1);

static class LoopPolicy
{
    public static Settings Configure(Settings original, LoopRequest request)
    {
        var cfg = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(original, J.Options), J.Options)!;
        if (request.Mode is not ("single" or "count" or "ropes")) throw new Exception("未知循环模式。");
        if (request.Mode == "count" && (request.Count < 1 || request.Count > 1000)) throw new Exception("循环次数须为 1～1000 的整数。");
        cfg.LoopUntilRopesEmpty = request.Mode == "ropes";
        cfg.MaxRuns = request.Mode == "count" ? request.Count : 1;
        cfg.AutoEnter = true;
        if (cfg.LoopUntilRopesEmpty) cfg.ReserveRopes = 0;
        cfg.Validate();
        return cfg;
    }
    public static string? Stop(Settings cfg, int completed, double ropes)
    {
        if (!cfg.LoopUntilRopesEmpty && completed >= cfg.MaxRuns) return $"已完成 {completed}/{cfg.MaxRuns} 次下潜，已结算并生成报告。";
        if (ropes <= (cfg.LoopUntilRopesEmpty ? 0 : cfg.ReserveRopes))
            return cfg.LoopUntilRopesEmpty ? $"绳索已用完，共完成 {completed} 次下潜，已结算并生成报告。" : $"已达到保留绳索数量，共完成 {completed}/{cfg.MaxRuns} 次下潜。";
        return null;
    }
}
