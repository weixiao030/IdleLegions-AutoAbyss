using System.Text;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class DamageReport
{
    static string Safe(string value) => value.Replace("|", "/").Replace("\n", " ").Replace("\r", " ");
    static string Name(JsonNode d) => Safe(GameText.Name(d.At("kind").S(), d.At("id").S(), d.At("name").S()));
    static string Metric(JsonNode[] rows, string key, bool max = false, bool min = false) => rows.Any(d => d.At(key) == null) ? "未记录" :
        (max ? rows.Max(d => d.At(key).N()) : min ? rows.Min(d => d.At(key).N()) : rows.Sum(d => d.At(key).N())).ToString("N0");

    internal static void Write(StringBuilder text, IEnumerable<JsonNode> heroRows)
    {
        var heroes = heroRows.ToArray();
        foreach (var group in heroes.SelectMany(h => h.At("damageDetails").A()).GroupBy(d => (Kind:d.At("kind").S(), Id:d.At("id").S())).OrderByDescending(g => g.Sum(d => d.At("damage").N())))
        {
            var rows = group.ToArray();
            double damage = rows.Sum(d => d.At("damage").N()), count = rows.Sum(d => d.At("events").N());
            text.AppendLine($"##### {Name(rows.Last())} · 详细构成\n");
            foreach (var variant in rows.GroupBy(r => (Basis:r.At("descriptionBasis").S("catalog"), Text:r.At("description").S())))
            {
                var desc = group.Key.Kind == "skill" && variant.Key.Basis == "not-recorded" ? "本场未采集技能参数；不以基础倍率补填。" : GameText.Description(group.Key.Kind,group.Key.Id,variant.Key.Text);
                string basis = group.Key.Kind != "skill" ? "效果说明" : variant.Key.Basis == "battle-context" ? "战前实际技能说明" : "基础说明（不代表本场等级）";
                if (desc != "") text.AppendLine($"{basis} · {variant.Count()} 场：{Safe(desc)}\n");
            }
            text.AppendLine("| 平均命中 | 最低命中 | 最高命中 | 暴击命中 | 暴击伤害 | 周期标记伤害（含引导） | 周期命中 | 击杀标记 |\n|---:|---:|---:|---:|---:|---:|---:|---:|");
            text.AppendLine($"| {(count > 0 ? damage / count : 0):N0} | {Metric(rows,"minHit",min:true)} | {Metric(rows,"maxHit",max:true)} | {Metric(rows,"criticalEvents")} | {Metric(rows,"criticalDamage")} | {Metric(rows,"periodicDamage")} | {Metric(rows,"periodicEvents")} | {Metric(rows,"kills")} |\n");
            if (rows.Any(d => d.At("targets") == null)) text.AppendLine("部分旧记录没有目标、元素与详细命中统计；以下拆分仅覆盖已记录部分。\n");
            var targets = rows.SelectMany(d => d.At("targets").A()).GroupBy(t => t.At("id").S()).OrderByDescending(g => g.Sum(t => t.At("damage").N())).ToArray();
            if (targets.Length > 0)
            {
                text.AppendLine("| 命中目标 | 伤害 | 占该项输出 | 命中 / 暴击 / 击杀 |\n|---|---:|---:|---:|");
                foreach (var target in targets)
                {
                    double value = target.Sum(t => t.At("damage").N());
                    text.AppendLine($"| {Safe(GameText.Text(target.Key))} | {value:N0} | {(damage > 0 ? value/damage : 0):P1} | {target.Sum(t=>t.At("events").N()):N0} / {target.Sum(t=>t.At("criticalEvents").N()):N0} / {target.Sum(t=>t.At("kills").N()):N0} |");
                }
                text.AppendLine();
            }
            var elements = rows.SelectMany(d => d.At("elements").A()).GroupBy(t => t.At("id").S());
            text.AppendLine("元素构成：" + string.Join("；", elements.Select(g => $"{GameText.Element(g.Key)} {g.Sum(t=>t.At("damage").N()):N0}")) + "。\n");
        }
        var contexts = heroes.Where(h => h.At("loadout") != null).Select(h => h.At("loadout")!).GroupBy(c => c.ToJsonString()).ToArray();
        text.AppendLine($"##### 战前构筑与实际技能参数\n\n{contexts.Length} 组账号属性快照；{heroes.Count(h => h.At("loadout") == null)} 场未采集。账号面板不等于深渊加成及敌方减伤后的最终战斗属性。\n");
        foreach (var context in contexts)
        {
            var c = context.First();
            text.AppendLine($"相同构筑 {context.Count()} 场 · 等级 {c.At("level").S("未记录")}\n");
            text.AppendLine("| 技能 | 等级 | 冷却（秒） | 装备状态 | 本场说明 |\n|---|---:|---:|---|---|");
            foreach (var s in c.At("skills").A())
                text.AppendLine($"| {Safe(s.At("name").S())} | {s.At("level").S("未记录")} | {s.At("cooldown").S("未记录")} | {(s.At("equipped") == null ? "未记录" : s.At("equipped").B() ? "已装备" : "未装备")} | {Safe(s.At("description").S("本场未提供"))} |");
            text.AppendLine();
        }
        var skills = heroes.SelectMany(h => h.At("skillActivity").A()).GroupBy(s => s.At("id").S()).ToArray();
        if (skills.Length > 0)
        {
            text.AppendLine("##### 技能施法与参与伤害\n\n技能参与伤害包含已计入主表的反应及遗物伤害，不能再次相加。施法标记仅为明确的纯施法事件，0不表示没有施放。\n");
            text.AppendLine("| 技能 | 施法标记 | 伤害命中 | 参与伤害 | 其中反应 / 遗物 | 治疗记录 |\n|---|---:|---:|---:|---:|---:|");
            foreach (var skill in skills)
                text.AppendLine($"| {Safe(GameText.Name("skill",skill.Key,skill.Last().At("name").S()))} | {Metric(skill.ToArray(),"castMarkers")} | {Metric(skill.ToArray(),"events")} | {Metric(skill.ToArray(),"damage")} | {Metric(skill.ToArray(),"reactionDamage")} / {Metric(skill.ToArray(),"relicDamage")} | {Metric(skill.ToArray(),"healing")} |");
            text.AppendLine();
        }
        var hits = heroes.SelectMany(h => h.At("largestHits").A()).OrderByDescending(h => h.At("damage").N()).Take(20).ToArray();
        if (hits.Length > 0)
        {
            text.AppendLine("##### 最高单次命中 · 前20条\n\n时间为各场战斗内时间；完整事件保留在原始战斗回执中。\n\n| 技能或效果 | 目标 | 伤害 | 时间（秒） | 元素 / 标记 |\n|---|---|---:|---:|---|");
            foreach (var hit in hits)
                text.AppendLine($"| {Name(hit)} | {Safe(GameText.Text(hit.At("target").S()))} | {hit.At("damage").N():N0} | {hit.At("time").N():F2} | {Safe(hit.At("element").S())}{(hit.At("critical").B()?" · 暴击":"")}{(hit.At("channel").B()?" · 引导":hit.At("periodic").B()?" · 周期":"")}{(hit.At("kill").B()?" · 击杀":"")} |");
            text.AppendLine();
        }
    }
}
