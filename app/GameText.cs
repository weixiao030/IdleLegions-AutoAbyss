using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FrostAbyss;

// Display-only catalog: game locale names plus reviewed current-skill translations.
// Never rewrite source receipts or feed translated text back into strategy scoring.
static class GameText
{
    static readonly JsonNode Catalog = Load();
    static readonly Dictionary<string,string> Terms = Catalog.At("terms")!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>());
    static readonly Regex TermPattern = new(@"(?<![\p{L}\p{N}_])(?:" + string.Join("|", Terms.Keys.OrderByDescending(x => x.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])", RegexOptions.Compiled);
    static string Display(string text) => TermPattern.Replace(text, m => Terms[m.Value]);
    static JsonNode Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("game-text-zh.json")!;
        return JsonNode.Parse(stream)!;
    }
    internal static bool IsChinese(string text) => Regex.IsMatch(text, @"[\u3400-\u9fff]");
    internal static string Text(string text)
    {
        var exact = Catalog.At("texts", text).S();
        if (exact != "") return exact;
        if (!text.StartsWith('§')) return Display(text);
        var parts = text.Split('§').Skip(1).ToArray();
        var template = Text(parts[0]);
        foreach (var arg in parts.Skip(1))
        {
            var at = template.IndexOf("%s", StringComparison.Ordinal);
            if (at < 0) break;
            // Game's nested plural/time template; don't expose its wire encoding.
            var fields = arg.StartsWith('¦') ? arg.Split('¦').Skip(1).ToArray() : [];
            var value = fields.Length >= 2 ? Text(fields[0]).Replace("%s", Text(fields[1])) : Text(fields.Length == 1 ? fields[0] : arg);
            template = template[..at] + value + template[(at + 2)..];
        }
        return template;
    }
    internal static string Name(string kind, string id, string original = "") =>
        IsChinese(original) ? Text(original) : Catalog.At(kind == "relic" ? "cards" : kind + "s", id, "name").S(Text(original == "" ? id : original));
    internal static string Description(string kind, string id, string original = "", string profession = "") =>
        Text(original != "" ? original :
            (kind == "skill" ? Catalog.At("skillsByProfession", profession, id, "description") : null)
            .S(Catalog.At(kind == "relic" ? "cards" : kind + "s", id, kind == "reaction" ? "desc" : "description").S()));
    internal static string Element(string id) => Catalog.At("elements", id).S(id == "" ? "未标记元素" : id);
}
