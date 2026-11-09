using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;

namespace FrostAbyss;

static class J
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static JsonNode? At(this JsonNode? n, params string[] keys) { foreach (var k in keys) { n = n is JsonObject o ? o[k] : null; } return n; }
    public static string S(this JsonNode? n, string fallback = "") => n is JsonValue ? n.ToString() : fallback;
    public static double N(this JsonNode? n, double fallback = 0) => double.TryParse(n.S(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : fallback;
    public static bool B(this JsonNode? n) => n?.ToString() is "true" or "True";
    public static IEnumerable<JsonNode> A(this JsonNode? n) => n is JsonArray a ? a.OfType<JsonNode>() : [];
    public static JsonNode? Read(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(file);
        }
        catch { return null; }
    }
    public static void Write(string path, object? value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
            // Godot briefly holds command.json without delete sharing. Retry only
            // this unpublished atomic rename, never a dispatched game operation.
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temp, path, true); break; }
                catch (Exception e) when (attempt < 20 && (e is IOException || e is UnauthorizedAccessException)) { Thread.Sleep(50); }
            }
        }
        finally { if (File.Exists(temp)) try { File.Delete(temp); } catch { } }
    }
    public static string Name(JsonNode? n) => n.At("nombreZh").S(n.At("nameZh").S(n.At("nombre").S(n.At("name").S(n.At("texto").S(n.At("id").S())))));
    public static string Text(JsonNode? n) => (n?.ToJsonString() ?? "").ToLowerInvariant();
}

sealed class Settings
{
    public int Simulations { get; set; } = 4096;
    public int Seed { get; set; } = 73129;
    public int DelayMs { get; set; } = 2200;
    public int StartDepth { get; set; } = 0;
    public int StopDepth { get; set; } = 0;
    public int MaxRuns { get; set; } = 1;
    public bool LoopUntilRopesEmpty { get; set; } = false;
    public bool PreferFreezeBuild { get; set; } = true;
    public int ClearFloors { get; set; } = 0;
    public int ReserveRopes { get; set; } = 2;
    public double RetreatHp { get; set; } = 0;
    public double MinWinRate { get; set; } = 0;
    public double RiskAversion { get; set; } = .7;
    public double FreezeWeight { get; set; } = 2.8;
    public double EnemyStrength { get; set; } = 1.0;
    public double ModelUncertainty { get; set; } = .45;
    public bool PrepareSkills { get; set; } = false;
    public bool SkipBattleReplay { get; set; } = true;
    public bool AutoPactWithFreeze { get; set; } = true;
    public bool FarmWithFreeze { get; set; } = true;
    public int RewardFloors { get; set; } = 7;
    public bool AvoidCombatAfterRewardFloors { get; set; } = true;
    public double CoreRewardWeight { get; set; } = 8;
    public double ShardRewardWeight { get; set; } = 4;
    public double FreezeDuration { get; set; } = 1.0;
    public int PactFloors { get; set; } = 7;
    public bool AvoidFireLightning { get; set; } = true;
    public bool AutoEnter { get; set; } = true;
    public bool AllowElite { get; set; } = true;
    public string[] AvoidEliteIds { get; set; } = [];
    public bool AllowAmbush { get; set; } = false;
    public bool AllowNonIceUtility { get; set; } = true;
    public string CoreCardId { get; set; } = "es2_carambano";
    public string GamePath { get; set; } = "";
    public string[] Formation { get; set; } = FormationRules.Default();
    public static bool ValidFormation(string[]? f) => FormationRules.Valid(f);
    public void Validate()
    {
        if (Simulations < 2000 || Simulations > 50000 || DelayMs < 1000 || DelayMs > 60000 || MaxRuns < 1 || MaxRuns > 1000 || StartDepth < 0 || StopDepth < 0 || StopDepth > 0 && StopDepth <= StartDepth || StopDepth > 1000 || ReserveRopes < 0 || ReserveRopes > 100) throw new Exception("参数超出范围：推演 2000～50000 次，间隔 1000～60000 毫秒。停止深度 0 表示不限。");
        if (AvoidEliteIds == null || AvoidEliteIds.Length > EliteRules.Catalog.Length || AvoidEliteIds.Any(id => id == null || !EliteRules.Catalog.Any(e => e.Id == id)) || AvoidEliteIds.Distinct().Count() != AvoidEliteIds.Length) throw new Exception("精英避让名单无效，请从列表中选择不同精英；可留空。");
        if (!ValidFormation(Formation)) throw new Exception("请选择 4 个不同职业，按前排到后排排列。");
        if (PactFloors < 0 || PactFloors > 1000) throw new Exception("契约层数须为 0～1000，0 表示关闭。前 7 层指 P0～P6。");
        if (RewardFloors < 0 || RewardFloors > 1000 || !double.IsFinite(CoreRewardWeight) || CoreRewardWeight < 0 || CoreRewardWeight > 30 || !double.IsFinite(ShardRewardWeight) || ShardRewardWeight < 0 || ShardRewardWeight > 30) throw new Exception("奖励阶段层数须为 0～1000，核心及碎屑权重须为 0～30。");
        if (!double.IsFinite(FreezeDuration) || FreezeDuration < .2 || FreezeDuration > 5) throw new Exception("模型冻结时长假设须为 0.2～5 秒。");
        if (ClearFloors < 0 || ClearFloors > 1000) throw new Exception("通关层数须为 0～1000，0 表示连续下潜。");
        foreach (var x in new[] { RetreatHp, MinWinRate, RiskAversion, ModelUncertainty }) if (!double.IsFinite(x) || x < 0 || x > 1) throw new Exception("比例参数须在 0～1 之间。");
        if (!double.IsFinite(EnemyStrength) || EnemyStrength < .1 || EnemyStrength > 10 || !double.IsFinite(FreezeWeight) || FreezeWeight < 0 || FreezeWeight > 10) throw new Exception("敌人强度须为 0.1～10，冻结权重须为 0～10。");
    }
}

static class Paths
{
    public static string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrostAbyss");
    public static string GameData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "Idle Legions");
    public static string Bridge => Path.Combine(GameData, "frost_abyss");
    public static string Config => Path.Combine(Data, "settings.json");
}
