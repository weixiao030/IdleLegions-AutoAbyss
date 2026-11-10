using System.Text.Json.Nodes;

namespace FrostAbyss;

static class DamageBreakdownTests
{
    internal static void Run(Action<bool, string> check, string[] args)
    {
        check(GameText.Name("reaction", "vaporizacion") == "蒸发" && GameText.Name("reaction", "eclipse") == "月蚀"
            && GameText.Name("skill", "marca_cazadora") == "女猎手印记", "旧桥接仅给ID时仍显示游戏中文技能与反应名");
        check(GameText.Text("Costra de hielo") == "冰痂" && GameText.Description("skill", "tajo_ascendente").Contains("130%")
            && !GameText.Description("skill", "tajo_ascendente").Contains('§'), "游戏原生翻译与技能参数模板转换为可读中文");
        check(GameText.Name("skill", "not-known") == "not-known" && GameText.Name("skill", "furia", "玩家自定义中文") == "玩家自定义中文", "未知ID保留且已有中文名不被覆盖");
        check(GameText.Description("skill", "juicio", "", "arquera") != GameText.Description("skill", "juicio", "", "paladin")
            && GameText.Description("skill", "juicio") == "", "同ID不同职业技能按职业取说明，无职业时不猜效果");
        var events = JsonNode.Parse("""
        [
          {"side":"ally","src":"A","dst":"E1","dmg":100,"t":1,"crit":true,"skill":"marca_cazadora","reaction":"eclipse","rel":"es_invierno","element":"ice","kill":true},
          {"side":"ally","src":"A","dst":"E2","dmg":40,"t":2,"skill":"marca_cazadora","element":"ice"},
          {"side":"ally","src":"A","dst":"E1","dmg":20,"t":3,"tick":1,"burnEl":"fire"},
          {"side":"ally","src":"A","dst":"E1","dmg":10,"t":4},
          {"side":"ally","src":"A","dst":"A","dmg":0,"t":0,"skill":"furia","castOnly":true},
          {"side":"ally","src":"A","dst":"A","dmg":-15,"t":5,"skill":"furia"}
        ]
        """)!.A().ToArray();
        string Name(string kind, string id) => GameText.Name(kind, id);
        var details = DamageBreakdown.Details(events.Where(e => e.At("dmg").N() > 0).ToArray(), Name)
            .Select(x => System.Text.Json.JsonSerializer.SerializeToNode(x, J.Options)!).ToArray();
        check(details.Sum(x => x.At("damage").N()) == 170 && details.Sum(x => x.At("events").N()) == 4,
            "详细来源互斥守恒，不因关联技能遗物重复计伤");
        var reaction = details.Single(d => d.At("kind").S() == "reaction");
        check(reaction.At("criticalDamage").N() == 100 && reaction.At("maxHit").N() == 100 && reaction.At("kills").N() == 1
            && reaction.At("tags").A().Count() == 3, "暴击伤害、最高命中、击杀与重叠标签均来自原始事件");
        check(details.All(d => d.At("targets").A().Sum(t => t.At("damage").N()) == d.At("damage").N()
            && d.At("elements").A().Sum(t => t.At("damage").N()) == d.At("damage").N()), "每个来源的目标及元素拆分都与该来源伤害守恒");
        var tick = details.Single(d => d.At("id").S() == "periodic-unmarked");
        check(tick.At("periodicDamage").N() == 20 && tick.At("elements").A().Single().At("id").S() == "",
            "数值tick标志识别周期命中，不把目标燃烧属性冒充本次攻击元素");
        var skills = DamageBreakdown.Skills(events, Name).Select(x => System.Text.Json.JsonSerializer.SerializeToNode(x, J.Options)!).ToArray();
        check(skills.Single(s => s.At("id").S() == "furia").At("castMarkers").N() == 1
            && skills.Single(s => s.At("id").S() == "furia").At("healing").N() == 15
            && skills.Single(s => s.At("id").S() == "furia").At("damage").N() == 0, "无伤害施法与治疗仍可见，且不虚构技能输出");
        check(skills.Single(s => s.At("id").S() == "marca_cazadora").At("reactionDamage").N() == 100,
            "技能标记视角保留反应参与量并与互斥来源分开");

        var archiveFixture = JsonNode.Parse("""{"before":{"id":"upgrade","profundidad":1,"nodo":"n","heroes":[{"id":"a","defId":"arquera","nombre":"A"}]},"result":{"durationSec":10,"events":[]}}""")!;
        archiveFixture["result"]!["events"] = new JsonArray(events.Select(e => e.DeepClone()).ToArray());
        var old = BattleJournal.Capture(archiveFixture, null, null, 1, 3, Path.Combine(Paths.Data,"battles","upgrade-check"), 1);
        old["heroes"]![0]!.AsObject().Remove("detailVersion");
        old["heroes"]![0]!.AsObject().Remove("skillActivity");
        var oldText = old.ToJsonString();
        var rawFile = Path.Combine(Paths.Data, old.At("sourceFile").S());
        var rawBytes = File.ReadAllBytes(rawFile);
        var upgraded = BattleJournal.Upgrade(old);
        check(upgraded.At("heroes").A().First().At("detailVersion").N() == 3
            && upgraded.At("heroes").A().First().At("damage").N() == 170
            && old.ToJsonString() == oldText && rawBytes.SequenceEqual(File.ReadAllBytes(rawFile)), "旧战报只读重算中文与构成，原摘要及原始回执均不变");
        var outside = (JsonObject)old.DeepClone(); outside["sourceFile"] = "../unrelated.json";
        check(BattleJournal.Upgrade(outside).ToJsonString() == outside.ToJsonString(), "旧报表不读取战斗归档目录之外的文件");

        var channel = JsonNode.Parse("""{"tick":1,"canal":1,"dmg":30,"burn":true}""")!;
        check(DamageBreakdown.Id(channel) == "channel-unmarked" && DamageBreakdown.Timing(channel) == "channel", "引导命中优先独立归类，燃烧状态不改变来源");
        var observed = archiveFixture.DeepClone();
        observed["context"] = JsonNode.Parse("""{"heroes":[{"id":"a","defId":"berserker","level":100,"stats":{"atk":26726},"skills":[{"id":"tajo_ascendente","name":"Tajo ascendente","desc":"§Arranca las dos hachas desde abajo y lo levanta del corte: %s de daño.§310%","level":10,"cooldown":6.8,"equipped":true}]}]}""");
        observed["result"]!["events"] = JsonNode.Parse("""[{"side":"ally","src":"A","dst":"E","dmg":300,"skill":"tajo_ascendente"}]""");
        var actual = BattleJournal.Capture(observed,null,null,1,3,Path.Combine(Paths.Data,"battles","context-check"),1).At("heroes").A().First();
        check(actual.At("damageDetails").A().Single().At("description").S().Contains("310%")
            && !actual.At("damageDetails").A().Single().At("description").S().Contains("130%")
            && actual.At("loadout","skills").A().Single().At("cooldown").N() == 6.8, "战前实际310%倍率和6.8秒冷却保留，不用基础130%替代");
        check(upgraded.At("heroes").A().First().At("loadout") == null, "旧记录缺战前构筑时不从当前快照补填");

        var directory = args.SkipWhile(x => x != "--battle-fixture-dir").Skip(1).FirstOrDefault();
        if (directory == null) return;
        var summaries = new List<JsonObject>();
        foreach (var file in Directory.EnumerateFiles(directory, "run-*.json", SearchOption.AllDirectories))
        {
            var raw = J.Read(file)!;
            var battle = raw.At("authoritativeBattle")!;
            var summary = BattleJournal.Capture(battle, battle.At("before"), null, (int)raw.At("round").N(1), 3,
                Path.Combine(Paths.Data, "live-replay"), summaries.Count + 1);
            foreach (var hero in summary.At("heroes").A())
            {
                var ds = hero.At("damageDetails").A().ToArray();
                check(ds.Sum(d => d.At("damage").N()) == hero.At("damage").N(), "实战回放角色详细输出守恒");
                check(ds.All(d => d.At("targets").A().Sum(t => t.At("damage").N()) == d.At("damage").N()
                    && d.At("elements").A().Sum(t => t.At("damage").N()) == d.At("damage").N()), "实战回放目标与元素拆分守恒");
            }
            summaries.Add(summary);
        }
        J.Write(Path.Combine(Paths.Data, "live-replay-summaries.json"), summaries);
        File.WriteAllText(Path.Combine(Paths.Data, "live-replay-report.md"), BattleJournal.Report(summaries), new System.Text.UTF8Encoding(false));
        check(summaries.Count > 0, "真实战斗回执目录成功离线重放，不发送游戏命令");
    }
}
