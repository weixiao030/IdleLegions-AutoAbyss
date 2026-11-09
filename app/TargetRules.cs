using System.Text.Json.Nodes;
namespace FrostAbyss;

// Uses the same hero-name -> enemy-name contract as the game's target menu.
static class TargetRules
{
    internal static Choice[] Choices(JsonNode snapshot, JsonNode run, string node, bool separate)
    {
        var enemies = snapshot.At("peeks", node, "enemigos").A().Select(e => e.At("name").S()).Where(n => n != "").Distinct().ToArray();
        var members = run.At("heroes").A().ToArray();
        var heroes = (members.Length > 0 ? members.Where(h => !h.At("caido").B()) : snapshot.At("peeks", node, "legion").A())
            .Select(h => h.At("nombre").S(h.At("name").S())).Where(n => n != "").Distinct().ToArray();
        if (enemies.Length == 0 || heroes.Length == 0)
            return [new("战斗：游戏默认目标", "sima.fight", new() { ["objetivos"] = new JsonObject(), ["desafio"] = false }) { Reason = "目标预览尚不完整，使用游戏默认合法目标" }];
        var result = new List<Choice>();
        var loads = new int[enemies.Length];
        var targets = new int[heroes.Length];
        bool limited = false;
        void Enumerate(int h)
        {
            if (result.Count >= 4096) { limited = true; return; }
            if (h == heroes.Length)
            {
                if (separate && loads.Max() - loads.Min() > 1) return;
                var objectives = new JsonObject();
                for (int i = 0; i < heroes.Length; i++) objectives[heroes[i]] = enemies[targets[i]];
                result.Add(new("战斗：" + string.Join("；", heroes.Select((name,i) => name + " → " + enemies[targets[i]])), "sima.fight", new() { ["objetivos"] = objectives, ["desafio"] = false })
                { Reason = separate ? "分别锁定目标：优先分散；敌人少于队员时均衡复用目标，按模拟比较分配" : "自由目标分配：允许集火，按模拟比较所有分配" });
                return;
            }
            for (int e = 0; e < enemies.Length; e++)
            {
                if (separate && loads[e] >= (heroes.Length + enemies.Length - 1) / enemies.Length) continue;
                targets[h] = e; loads[e]++; Enumerate(h + 1); loads[e]--;
            }
        }
        Enumerate(0);
        if (limited) foreach (var choice in result) choice.Reason += "；超大菜单比较前4096个合法分配，不宣称全局最优";
        return result.ToArray();
    }
}
