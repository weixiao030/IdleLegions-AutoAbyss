using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

sealed record Elite(string Id, string Name, int Hall, int Variant, string Group);

static class EliteRules
{
    public static readonly Elite[] Catalog = Load();
    static Elite[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("elites.json")!;
        return JsonSerializer.Deserialize<Elite[]>(stream, J.Options)!;
    }
    internal static Elite[] Avoided(JsonNode? preview, Settings cfg)
    {
        if (cfg.AvoidEliteIds.Length == 0) return [];
        var found = new HashSet<string>();
        foreach (var enemy in preview.At("enemigos").A())
        {
            var id = enemy.At("char").S();
            var elite = Catalog.FirstOrDefault(e => e.Id == id || e.Name == J.Name(enemy));
            // Same identity fallback as the client: hall (planta) + variant.
            // Missing identity stays unknown; a room label alone is insufficient.
            if (elite == null && id == "" && preview.At("planta") != null && enemy.At("variant") != null)
            {
                int hall = (int)preview.At("planta").N(-1), variant = (int)enemy.At("variant").N(-1);
                elite = Catalog.FirstOrDefault(e => e.Variant == variant && (e.Hall == hall || e.Hall == 10 && hall is >= 0 and < 10));
            }
            if (elite != null && cfg.AvoidEliteIds.Contains(elite.Id)) found.Add(elite.Id);
        }
        return Catalog.Where(e => found.Contains(e.Id)).ToArray();
    }
}
