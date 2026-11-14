using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.IO.Compression;

namespace FrostAbyss;

static class J
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static JsonNode? At(this JsonNode? n, params string[] keys) { foreach (var k in keys) { n = n is JsonObject o ? o[k] : null; } return n; }
    public static string S(this JsonNode? n, string fallback = "") => n is JsonValue ? n.ToString() : fallback;
    public static double N(this JsonNode? n, double fallback = 0)
    {
        // Parsed JSON numbers already have a numeric representation. Avoid allocating
        // a string and parsing it again for every event, hero and search feature.
        if (n is JsonValue number)
        {
            if (number.TryGetValue<double>(out var d)) return double.IsFinite(d) ? d : fallback;
            if (number.TryGetValue<int>(out var i)) return i;
            if (number.TryGetValue<long>(out var l)) return l;
        }
        return double.TryParse(n.S(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : fallback;
    }
    public static bool B(this JsonNode? n) => n is JsonValue value && value.TryGetValue<bool>(out var b) ? b : n?.ToString() is "true" or "True";
    public static IEnumerable<JsonNode> A(this JsonNode? n) => n is JsonArray a ? a.OfType<JsonNode>() : [];
    public static JsonNode? Read(string path)
    {
        try
        {
            using var file = OpenRead(path);
            return JsonNode.Parse(file);
        }
        catch { return null; }
    }
    internal static Stream OpenRead(string path)
    {
        if (!File.Exists(path) && File.Exists(path + ".gz")) path += ".gz";
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress) : file;
    }
    internal static T? ReadAs<T>(string path)
    {
        using var file = OpenRead(path);
        return JsonSerializer.Deserialize<T>(file, Options);
    }
    internal static string[] HistoryFiles(string directory, SearchOption option = SearchOption.TopDirectoryOnly) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json*", option)
            .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? p[..^3] : p).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    public static void Write(string path, object? value)
    {
        if (!File.Exists(path) && File.Exists(path + ".gz")) path += ".gz";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    using var gzip = new GZipStream(file, CompressionLevel.Fastest);
                    JsonSerializer.Serialize(gzip, value, Options);
                }
                else JsonSerializer.Serialize(file, value, Options);
            }
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
    public int Simulations { get; set; } = 2000;
    public int Seed { get; set; } = 73129;
    public int DelayMs { get; set; } = 1000;
    public int StartDepth { get; set; } = 0;
    public int StopDepth { get; set; } = 0;
    public int MaxRuns { get; set; } = 1;
    public bool LoopUntilRopesEmpty { get; set; } = false;
    public bool PreferFreezeBuild { get; set; } = true;
    public int StrategyId { get; set; } = 1;
    public SearchSettings Search { get; set; } = new();
    public CustomStrategySettings CustomStrategy { get; set; } = new();
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
    public bool SkipEightHitPact { get; set; } = true;
    public bool SkipDodgePact { get; set; } = true;
    public bool SkipShieldCardsEarly { get; set; } = true;
    public int ShieldSkipFloors { get; set; } = 7;
    public bool RetryTimeouts { get; set; } = true;
    public bool FarmWithFreeze { get; set; } = true;
    public int RewardFloors { get; set; } = 7;
    public bool AvoidCombatAfterRewardFloors { get; set; } = true;
    public double CoreRewardWeight { get; set; } = 8;
    public double ShardRewardWeight { get; set; } = 4;
    public double FreezeDuration { get; set; } = 1.0;
    public int PactFloors { get; set; } = 7;
    public bool AllowBasicBurn { get; set; } = true;
    public bool AllowBasicLightning { get; set; } = false;
    public bool AvoidFireLightning { get; set; } = true;
    public bool LightningConsumesFreeze { get; set; } = true;
    public bool AutoEnter { get; set; } = true;
    public bool AllowElite { get; set; } = true;
    public string[] AvoidEliteIds { get; set; } = ["sg_alferez"];
    public bool AllowAmbush { get; set; } = true;
    public bool SeparateTargets { get; set; } = true;
    public bool AllowNonIceUtility { get; set; } = true;
    public string CoreCardId { get; set; } = "es2_carambano";
    public string GamePath { get; set; } = "";
    public string[] Formation { get; set; } = FormationRules.Default();
    public static bool ValidFormation(string[]? f) => FormationRules.Valid(f);
    public void Validate()
    {
        if (StrategyId is < 1 or > 7) throw new Exception("请选择策略一至策略七。");
        (Search ?? throw new Exception("搜索配置为空，请恢复默认。")).Validate();
        // Inactive custom settings cannot change or invalidate strategies 1–3.
        if (StrategyId == 4) (CustomStrategy ?? throw new Exception("策略四配置为空，请恢复默认。")).Validate();
        if (AvoidEliteIds == null || AvoidEliteIds.Length > EliteRules.Catalog.Length || AvoidEliteIds.Any(id => id == null || !EliteRules.Catalog.Any(e => e.Id == id)) || AvoidEliteIds.Distinct().Count() != AvoidEliteIds.Length) throw new Exception("精英避让名单无效，请从列表中选择不同精英；可留空。");
        if (!ValidFormation(Formation)) throw new Exception("请选择 4 个不同职业，按前排到后排排列。");
        // No product-imposed ranges. Integer representation and finite numeric
        // values are still required by the configuration format.
        foreach (var x in new[] { RetreatHp, MinWinRate, RiskAversion, ModelUncertainty, EnemyStrength, FreezeWeight, CoreRewardWeight, ShardRewardWeight, FreezeDuration })
            if (!double.IsFinite(x)) throw new Exception("参数必须是有限数值。");
    }
}

static class Paths
{
    public static string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrostAbyss");
    public static string GameData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "Idle Legions");
    public static string Bridge => Path.Combine(GameData, "frost_abyss");
    public static string Config => Path.Combine(Data, "settings.json");
}
