using System.Text.Json.Nodes;

namespace FrostAbyss;

static class BattleJournalTests
{
    public static void Run(Action<bool, string> check, string[] args)
    {
        var directory = Path.Combine(Paths.Data, "journal-fixture");
        var battle = JsonNode.Parse("""
        {
          "before": {
            "id":"fixture-run", "profundidad":4, "nodo":"boss",
            "heroes":[{"id":"hero-1","defId":"berserker","nombre":"Berserker"}],
            "reliquias":[{"id":"es_invierno","nombre":"Invierno","desc":"Ralentizas un 8%","familia":"escarcha"}]
          },
          "result": {
            "win":true, "durationSec":10, "truncated":false,
            "sima":{"dano":{"hero-1":7}},
            "events":[
              {"side":"ally","src":"Berserker","dst":"Enemy","dmg":100,"crit":true},
              {"side":"ally","src":"Berserker","dst":"Enemy","dmg":50,"skill":"strike","reaction":"shatter","rel":"es_invierno"},
              {"side":"ally","src":"Berserker","dst":"Enemy","dmg":0,"rel":"es_invierno"},
              {"side":"ally","src":"Berserker","dst":"Berserker","dmg":-5},
              {"side":"enemy","src":"Enemy","dst":"Berserker","dmg":300,"crit":true,"skill":"enemy-strike"}
            ]
          }
        }
        """)!;
        var before = JsonNode.Parse("""{"botin":{"nucleos":2,"esquirlas":8,"obolos":10}}""")!;
        var after = JsonNode.Parse("""{"sima":{"run":{"botin":{"nucleos":3,"esquirlas":10,"obolos":6}}}}""")!;
        var first = BattleJournal.Capture(battle, before, after, 1, 3, directory, 1);
        var hero = first.At("heroes").A().Single();
        check(hero.At("damage").N() == 150 && hero.At("damageTaken").N() == 300 && hero.At("hpLoss").N() == 7,
            "战报分别统计己方输出150、敌方伤害300与结算生命损失7，不混用dano");
        check(hero.At("criticalEvents").N() == 1 && hero.At("skillEvents").N() == 1 && hero.At("reactionEvents").N() == 1
            && hero.At("healingObserved").N() == 5 && hero.At("dps").N() == 15,
            "战报暴击技能反应仅归己方，负伤害独立记治疗，DPS按战斗时长计算");
        var card = first.At("cardProcs").A().Single();
        check(card.At("events").N() == 2 && card.At("damage").N() == 50 && card.At("name").S() == "Invierno",
            "卡牌统计保留零伤害rel触发和实际效果文本");
        check(first.At("resourceDelta", "cores").N() == 1 && first.At("resourceDelta", "shards").N() == 2
            && first.At("resourceDelta", "obols").N() == -4, "战报资源差分保留奖励与消耗正负号");
        var archived = J.Read(Path.Combine(directory, "run-001-P4-00001.json"));
        check(archived.At("authoritativeBattle")!.ToJsonString() == battle.ToJsonString()
            && archived.At("strategy").N() == 3, "战斗原始回执完整落盘并保留当场策略");
        var originalData = Paths.Data;
        try
        {
            Paths.Data = Path.Combine(directory, "restore-display");
            Directory.CreateDirectory(Paths.Data);
            J.Write(Path.Combine(Paths.Data, "last-session.json"), new {
                battleRecords = new[] { first },
                receipts = new[] { new { round = 1, depth = 4, cardId = "fixture-card", cardName = "已确认卡牌" } }
            });
            var restored = System.Text.Json.JsonSerializer.SerializeToNode(new Pilot().Status(), J.Options)!;
            check(restored.At("battles").A().Count() == 1 && restored.At("acquisitions").A().Single().At("cardId").S() == "fixture-card"
                && !restored.At("active").B() && !restored.At("loop", "sessionOpen").B(),
                "重启恢复战斗和取得卡牌展示，不恢复自动执行或打开旧会话");
        }
        finally { Paths.Data = originalData; }
        first["heroes"]![0]!["damage"] = 160;
        check(battle.At("result", "events").A().First().At("dmg").N() == 100,
            "汇总对象修改不污染原始战斗回执");
        var second = BattleJournal.Capture(battle, before, null, 1, 3, directory, 2);
        var third = BattleJournal.Capture(battle, before, after, 2, 1, directory, 3);
        check(second.At("resourceDelta", "cores") == null && second.At("resourceDelta", "obols") == null,
            "章末或资源快照缺失时记未确认，不虚构为归零损失");
        second["truncated"] = true;
        var report = BattleJournal.Report([first, second, third]);
        check(report.Contains("第 1 轮 · P4") && report.Contains("第 2 轮 · P4")
            && report.Contains("第 1 轮汇总") && report.Contains("全段角色汇总") && report.Contains("未确认")
            && report.Contains("明细不完整"), "战报按轮层、单轮及全段分组，并明确截断和未知资源");
        check(BattleJournal.Summarize([third, first]).Cast<JsonNode>().First().At("round").N() == 1,
            "状态接口的战斗列表按轮次深度排序");

        check(hero.At("damageSources", "reaction").N() == 50 && hero.At("damageSources", "other").N() == 100
            && hero.At("damageSources")!.AsObject().Sum(x => x.Value.N()) == 150,
            "重叠技能反应遗物标记互斥归类，来源相加等于输出");
        check(report.Contains("输出占比") && report.Contains("DPS") && report.Contains("结算生命损失")
            && report.Contains("占全队输出"), "Markdown保留角色占比、DPS、结算实损和卡牌直接触发占比");
        var moved = after.DeepClone();
        moved["sima"]!["run"]!["id"] = "new-chapter";
        var nextChapter = BattleJournal.Capture(battle, before, moved, 3, 3, directory, 4);
        check(nextChapter.At("resourceDelta", "cores") == null, "跨章暂存战利品重置不误记为资源损失");
        var rawLoot = JsonNode.Parse("""
        {"commandId":"loot-command","inventoryObserved":true,
         "inventoryAdded":[{"id":"concurrent-item","name":"并行背包物品"}],
         "botin":{"piezas":[{"id":"mythic-1","displayName":"测试神话|长剑","name":"Sword","rarity":"mythic"}],
          "miticosAlBuzon":2,"miticosPendientes":1,"nucleos":{"enMano":3,"alBuzon":4},"esquirlas":5}}
        """)!;
        var lootSnapshot = new JsonObject { ["lastLoot"] = rawLoot.DeepClone() };
        var loot = DropJournal.ForCommand(lootSnapshot, JsonNode.Parse("""{"id":"loot-command"}"""))!;
        check(loot.At("source").S() == "settlement-receipt" && loot.At("items").A().Single().At("id").S() == "mythic-1",
            "装备以匹配命令的结算收据为准，不混入并行背包新增");
        check(loot.At("items").A().Single().At("name").S() == "测试神话|长剑"
            && loot.At("items").A().Single().At("rarity").S() == "mythic"
            && loot.At("mailedMythics").N() == 2 && loot.At("pendingMythics").N() == 1,
            "装备保存游戏本地化名称稀有度与邮箱待领取数量，不猜缺失名称");
        check(loot.At("rawEvidence")!.ToJsonString() == rawLoot.ToJsonString(), "装备证据原样归档可复核");
        check(DropJournal.ForCommand(lootSnapshot, JsonNode.Parse("""{"id":"different-command"}""")) == null,
            "旧结算证据不重复归因给后续操作");
        rawLoot.AsObject().Remove("botin");
        var inventoryLoot = DropJournal.Read(rawLoot);
        check(inventoryLoot.At("source").S() == "inventory-delta" && inventoryLoot.At("items").A().Single().At("name").S() == "并行背包物品"
            && inventoryLoot.At("mailedMythics") == null, "无收据时背包新增独立标来源，缺失数量保持未知");
        check(DropJournal.Read(null).At("source").S() == "unavailable", "旧桥接没有证据不冒充零掉落");
        var dropReport = DropJournal.Report([new { round = 1, depth = 4, method = "sima.guarda", equipmentLoot = loot }]);
        check(dropReport.Contains("测试神话/长剑") && dropReport.Contains("结算收据") && dropReport.Contains("待领取 1"),
            "非战斗结算也进入报告，装备名表格转义不破坏结构");

        var legacy = new Pilot(); legacy.Config.StrategyId = 2;
        var targets = JsonNode.Parse("""
        {"sima":{"run":{"id":"legacy","nodo":"fight","heroes":[{"id":"a","name":"A"},{"id":"b","name":"B"}],
          "mapa":{"nodos":{"fight":{"tipo":"combate"}}}}},
         "peeks":{"fight":{"enemigos":[{"name":"X"},{"name":"Y"}]}}}
        """)!;
        check(legacy.Build(targets).Length == 4, "策略二保留1.3.1全部目标组合，不继承新版强制分散");
        var purchase = new Choice("old-shop", "sima.trade", new(), card: new JsonObject(), priority: -1);
        var exit = new Choice("exit", "sima.move", new());
        check(Legacy131.Selection.BeforeShopExit([purchase, exit], null).Single() == purchase
            && Pilot.BeforeShopExit([purchase, exit], null).Single() == exit,
            "策略二保留1.3.1无战斗基线时的商店逻辑，策略一仍用新版判断");

        // Optional read-only replay of a captured game receipt; never sends game commands.
        var samplePath = args.SkipWhile(x => x != "--battle-fixture").Skip(1).FirstOrDefault();
        if (samplePath == null) return;
        var sample = J.Read(samplePath) ?? throw new Exception("无法读取战斗回执夹具：" + samplePath);
        var actual = BattleJournal.Capture(sample, sample.At("before"), null, 1, 3,
            Path.Combine(Paths.Data, "observed-battle"), 1);
        J.Write(Path.Combine(Paths.Data, "observed-battle-summary.json"), actual);
        File.WriteAllText(Path.Combine(Paths.Data, "observed-battle-report.md"), BattleJournal.Report([actual]),
            new System.Text.UTF8Encoding(false));
        check(actual.At("heroes").A().Any() && actual.At("eventCount").N() == sample.At("result", "events").A().Count(),
            "已保存真实战斗回执的只读复盘报告，未调用游戏或消耗资源");
    }
}
