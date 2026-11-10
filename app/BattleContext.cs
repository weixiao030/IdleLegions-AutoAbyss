using System.Text.Json.Nodes;

namespace FrostAbyss;

// Display only: account parameters observed before this command. These are not
// the server's final combat stats after abyss relics, buffs and enemy mitigation.
static class BattleContext
{
    internal static JsonNode? Hero(JsonNode? context, string id) => context.At("heroes").A().FirstOrDefault(h => h.At("id").S() == id);
    internal static JsonNode? Build(JsonNode? context, string id)
    {
        var hero = Hero(context, id);
        if (hero == null) return null;
        string profession = hero.At("defId").S();
        var stats = new JsonObject();
        foreach (var key in new[] { "atk", "crit", "critdmg", "def", "dodge", "heal", "healer", "hp", "magic", "mspd", "pmag", "spd", "spdTope" })
            if (hero.At("stats",key) is JsonNode value) stats[key] = value.DeepClone();
        var skills = new JsonArray();
        foreach (var skill in hero.At("skills").A())
        {
            string sid = skill.At("id").S();
            skills.Add(new JsonObject {
                ["id"] = sid, ["name"] = GameText.Name("skill",sid,skill.At("name").S()),
                ["description"] = skill.At("desc") == null ? "" : GameText.Description("skill",sid,skill.At("desc").S(),profession),
                ["level"] = skill.At("level")?.DeepClone(), ["cooldown"] = skill.At("cooldown")?.DeepClone(),
                ["equipped"] = skill.At("equipped")?.DeepClone(), ["element"] = GameText.Element(skill.At("element").S())
            });
        }
        return new JsonObject { ["source"] = "pre-command-account-state", ["level"] = hero.At("level")?.DeepClone(), ["skillSlots"] = hero.At("skillSlots")?.DeepClone(), ["stats"] = stats, ["skills"] = skills };
    }
}
