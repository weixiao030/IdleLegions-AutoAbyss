using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

sealed record Profession(string Id, string Name);
sealed record FormationRequest(string[]? Formation);

static class FormationRules
{
    // One catalog for the selector, validation, and Chinese status/error labels.
    public static readonly Profession[] Professions = LoadProfessions();
    static readonly Dictionary<string, string> Names = Professions.ToDictionary(p => p.Id, p => p.Name);
    static Profession[] LoadProfessions()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("professions.json")!;
        return JsonSerializer.Deserialize<Profession[]>(stream, J.Options)!;
    }
    public static string[] Default() => ["berserker", "mago", "arquera", "paladin"];
    public static string Name(string id) => Names.GetValueOrDefault(id, "未知职业");
    public static bool Valid(string[]? formation) => formation is { Length: 4 }
        && formation.All(id => id != null && Names.ContainsKey(id))
        && formation.Distinct(StringComparer.Ordinal).Count() == 4;
    public static JsonNode[] ResolveHeroes(string[] formation, JsonNode snapshot)
    {
        if (!Valid(formation)) throw new Exception("请选择 4 个不同职业，按前排到后排排列。");
        var heroes = snapshot.At("heroes").A().ToArray();
        return formation.Select(id => heroes.FirstOrDefault(h => h.At("defId").S() == id)
            ?? throw new Exception($"当前账号缺少{Name(id)}，请在策略参数中更换职业。" )).ToArray();
    }
}
