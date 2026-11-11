using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

// Read the frozen 1.3.1 implementation as an oracle. Never patch that source.
static class Strategy4DefaultsGenerator
{
    static void Main(string[] args)
    {
        var originals = JsonNode.Parse(File.ReadAllText(args[0]))!.AsArray().ToDictionary(c => c!.At("id").S(), c => c!);
        var cfg = new Settings { StrategyId = 2 };
        var core = new JsonObject { ["id"] = "es_carambano", ["familia"] = "escarcha", ["desc"] = "Tus autoataques CONGELAN al objetivo" };
        var run = new JsonObject { ["reliquias"] = new JsonArray(core), ["blasones"] = new JsonArray(), ["buffs"] = new JsonArray() };
        var rows = CardCatalog.Data.At("cards").A().Select(card =>
        {
            string id = card.At("id").S();
            var raw = originals.GetValueOrDefault(id) ?? new JsonObject { ["id"] = id, ["familia"] = card.At("family").S(), ["descZh"] = card.At("description").S() };
            var fx = Legacy131.Effects.Parse([raw], cfg);
            bool blocked = Legacy131.Cards.Forbidden(raw);
            int score = card.At("verified").B() ? Legacy131.Cards.BuildValue(raw, cfg, run, "sima.reliquia").Priority : -1000;
            double potency = fx.Damage + fx.Speed + fx.Crit + fx.CritDamage + fx.Shield + fx.VsSlow + fx.VsStatus + fx.Execute + fx.Mark;
            if (fx.Cleave > 0) potency += 10.0 / fx.Cleave;
            return new { id, family = card.At("family").S(), category = card.At("category").S(), score = blocked ? -2000 : score, potency, blocked };
        }).ToArray();
        var families = CardCatalog.Families.OrderByDescending(f => rows.Where(c => c.family == f).Max(c => c.score)).ThenBy(f => Array.IndexOf(CardCatalog.Families, f)).ToArray();
        var categories = families.ToDictionary(f => f, f => CardCatalog.Categories.OrderByDescending(cat => rows.Where(c => c.family == f && c.category == cat).Select(c => c.score).DefaultIfEmpty(-3000).Max()).ThenBy(cat => Array.IndexOf(CardCatalog.Categories, cat)).ToArray());
        var priorities = new Dictionary<string, int>();
        foreach (var f in families)
        {
            var sorted = rows.Where(c => c.family == f).OrderBy(c => Array.IndexOf(categories[f], c.category)).ThenByDescending(c => c.score).ThenByDescending(c => c.potency).ThenBy(c => c.id).ToArray();
            for (int i = 0; i < sorted.Length; i++) priorities[sorted[i].id] = i + 1;
        }
        var result = new { schemaVersion = 1, reference = "1.3.1 冻结实现：已继承普攻冰冻、尚无开局盾时的静态参考；按系列、细分类、单卡三层排序。普攻点燃与带电默认拉黑，可自行解除。", familyOrder = families, categoryOrder = categories, cardPriority = priorities, cardMode = rows.Where(c => c.blocked).ToDictionary(c => c.id, c => "block") };
        var temp = args[1] + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n", new System.Text.UTF8Encoding(false));
        File.Move(temp, args[1], true);
        Console.WriteLine($"Generated strategy 4 defaults for {priorities.Count} cards; frozen strategy unchanged.");
    }
}
