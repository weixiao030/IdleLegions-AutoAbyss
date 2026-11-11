using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class BattleJournal
{
    internal static JsonObject Capture(JsonNode? battleNode, JsonNode? runBefore, JsonNode? snapshotAfter,
        int round, int strategy, string directory, int number)
    {
        var battle = battleNode?.DeepClone() as JsonObject ?? new JsonObject();
        var before = battle.At("before");
        var result = battle.At("result");
        int depth = (int)before.At("profundidad").N(runBefore.At("profundidad").N());
        string runId = before.At("id").S(runBefore.At("id").S());
        string node = before.At("nodo").S(runBefore.At("nodo").S());
        var heroes = before.At("heroes").A().Select(x => x.DeepClone()).ToArray();
        var events = result.At("events").A().Select(x => x.DeepClone()).ToArray();
        var resourceBefore = (runBefore.At("botin") ?? before.At("botin"))?.DeepClone();
        var resourceAfter = snapshotAfter.At("sima", "run", "botin")?.DeepClone();
        var afterRunId = snapshotAfter.At("sima", "run", "id").S();
        if (runId != "" && afterRunId != "" && runId != afterRunId) resourceAfter = null;
        var resourceDelta = ResourceDelta(resourceBefore, resourceAfter);
        var cards = before.At("reliquias").A().Concat(before.At("blasones").A()).Concat(before.At("buffs").A())
            .Select(c => new { id = c.At("id").S(), name = GameText.Name("relic", c.At("id").S(), J.Name(c)), description = GameText.Description("relic", c.At("id").S(), c.At("descZh").S(c.At("desc").S())) })
            .ToArray();
        Directory.CreateDirectory(directory);
        var fileName = $"run-{round:D3}-P{depth}-{number:D5}.json";
        J.Write(Path.Combine(directory, fileName), new
        {
            round, strategy, depth, runId, node,
            capturedAt = DateTimeOffset.Now,
            roster = heroes,
            cards,
            resourceBefore,
            resourceAfter,
            resourceDelta,
            authoritativeBattle = battle
        });
        var sourceFile = Path.GetRelativePath(Paths.Data, Path.Combine(directory, fileName)).Replace('\\', '/');
        return new JsonObject
        {
            ["round"] = round,
            ["strategy"] = strategy,
            ["depth"] = depth,
            ["runId"] = runId,
            ["node"] = node,
            ["won"] = result.At("win").B(),
            ["truncated"] = result.At("truncated").B(),
            ["eventCount"] = events.Length,
            ["durationSec"] = result.At("durationSec").N(),
            ["sourceFile"] = sourceFile,
            ["resourceDelta"] = JsonSerializer.SerializeToNode(resourceDelta, J.Options),
            ["heroes"] = JsonSerializer.SerializeToNode(HeroStats(result, heroes, events, battle.At("labels"), cards, battle.At("context")), J.Options),
            ["cardProcs"] = JsonSerializer.SerializeToNode(CardProcs(cards, events), J.Options),
            ["loot"] = DropJournal.Read(battle.At("lootEvidence"))
        };
    }

    // Recompute display-only details from archived game events. No game commands,
    // resource changes, or edits to the authoritative receipt are involved.
    internal static JsonObject Upgrade(JsonObject record)
    {
        var copy = (JsonObject)record.DeepClone();
        if (record.At("heroes").A().All(h => h.At("detailVersion").N() >= 3)) return copy;
        try
        {
            var root = Path.GetFullPath(Path.Combine(Paths.Data, "battles")) + Path.DirectorySeparatorChar;
            var source = Path.GetFullPath(Path.Combine(Paths.Data, record.At("sourceFile").S()));
            if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return copy;
            var archived = J.Read(source);
            var battle = archived.At("authoritativeBattle");
            if (battle.At("result", "events") is not JsonArray || battle.At("before", "heroes") is not JsonArray
                || archived.At("round").N() != record.At("round").N()
                || archived.At("depth").N() != record.At("depth").N()
                || archived.At("runId").S() != record.At("runId").S()
                || archived.At("node").S() != record.At("node").S()) return copy;
            var before = battle.At("before");
            var events = battle.At("result", "events").A().ToArray();
            var cards = before.At("reliquias").A().Concat(before.At("blasones").A()).Concat(before.At("buffs").A())
                .Select(c => (object)new { id = c.At("id").S(), name = GameText.Name("relic",c.At("id").S(),J.Name(c)), description = GameText.Description("relic",c.At("id").S(),c.At("descZh").S(c.At("desc").S())) }).ToArray();
            copy["heroes"] = JsonSerializer.SerializeToNode(HeroStats(battle.At("result"), before.At("heroes").A().ToArray(), events, battle.At("labels"), cards, battle.At("context")), J.Options);
            copy["cardProcs"] = JsonSerializer.SerializeToNode(CardProcs(cards,events),J.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return copy;
    }

    internal static object[] Summarize(IEnumerable<JsonObject> records) => records
        .Select(x => x.DeepClone())
        .OrderBy(x => x.At("round").N())
        .ThenBy(x => x.At("depth").N())
        .Select(x => (object)x)
        .ToArray();

    internal static string Report(IEnumerable<JsonObject> records)
    {
        var all = records.Select(x => x.DeepClone()).OrderBy(x => x.At("round").N()).ThenBy(x => x.At("depth").N()).ToArray();
        var text = new System.Text.StringBuilder();
        text.AppendLine("## 逐层战斗统计\n");
        if (all.Length == 0)
        {
            text.AppendLine("本段没有收到可核验的真实战斗回执。\n");
            return text.ToString();
        }

        foreach (var group in all.GroupBy(x => (Round: (int)x.At("round").N(), Depth: (int)x.At("depth").N())))
        {
            text.AppendLine($"### 第 {group.Key.Round} 轮 · P{group.Key.Depth}\n");
            text.AppendLine(HeroHeader);
            WriteHeroRows(text, group.ToArray());
            text.AppendLine($"\n战斗数：{group.Count()}；胜利：{group.Count(x => x.At("won").B())}；");
            text.AppendLine($"事件：{group.Sum(x => x.At("eventCount").N()):N0}；日志截断：{(group.Any(x => x.At("truncated").B()) ? "是，明细不完整" : "否")}。\n");
            WriteCardRows(text, group.ToArray());
            var files = group.Select(x => x.At("sourceFile").S()).Where(x => x != "");
            text.AppendLine("原始战斗回执：" + string.Join("、", files) + "。\n");
        }

        foreach (var group in all.GroupBy(x => (int)x.At("round").N()))
        {
            text.AppendLine($"### 第 {group.Key} 轮汇总\n");
            text.AppendLine(HeroHeader);
            WriteHeroRows(text, group.ToArray());
            WriteInsights(text, group.ToArray());
            text.AppendLine();
        }

        text.AppendLine("### 全段角色汇总\n");
        text.AppendLine(HeroHeader);
        WriteHeroRows(text, all);
        WriteInsights(text, all);
        WriteDamageDetails(text, all);
        text.AppendLine("\n### 卡牌与伤害归因\n\n战斗事件中的 `rel` 触发（包括零伤害效果触发）会单独统计；遗物的常驻攻击、攻速、减速及异常增伤没有独立伤害事件时，不能从总伤害中准确拆出贡献比例。表中“触发占比”仅占已标记的 `rel` 直接伤害，不代表卡牌完整增伤。\n");
        WriteCardRows(text, all);
        text.AppendLine("输出、承受伤害来自战斗事件。敌方事件伤害可能包含护盾、免疫和过量伤害，不等于实际扣血；`result.sima.dano` 为结算生命损失。暴击、技能及反应统计是命中事件数，不代表技能施放次数。事件截断时所有事件统计均为不完整下界。\n");
        return text.ToString();
    }

    internal static string AcquisitionReport(IEnumerable<object> receipts)
    {
        var rows = receipts.Select(x => JsonSerializer.SerializeToNode(x, J.Options)!)
            .Where(x => x.At("cardId").S() != "")
            .GroupBy(x => ((int)x.At("round").N(), (int)x.At("depth").N()));
        var text = new System.Text.StringBuilder("## 已确认取得的卡牌\n\n| 轮次 | 深度 | 卡牌 | 效果文本 |\n|---:|---:|---|---|\n");
        int count = 0;
        foreach (var group in rows.OrderBy(x => x.Key.Item1).ThenBy(x => x.Key.Item2))
        foreach (var card in group)
        {
            count++;
            text.AppendLine($"| {group.Key.Item1} | P{group.Key.Item2} | {GameText.Name("relic",card.At("cardId").S(),card.At("cardName").S())} (`{card.At("cardId").S()}`) | {GameText.Description("relic",card.At("cardId").S(),card.At("cardDescription").S()).Replace("|", "/")} |");
        }
        if (count == 0) return "## 已确认取得的卡牌\n\n本段尚无已确认的卡牌选择回执。\n";
        return text.ToString();
    }

    const string HeroHeader = "| 角色 | 输出 | 输出占比 | 每秒输出 | 承受事件伤害 | 结算生命损失（已知/场数） | 暴击命中 | 技能命中 | 反应命中 | 反应/遗物/技能/其他输出 |\n|---|---:|---:|---:|---:|---:|---:|---:|---:|---|";

    static void WriteHeroRows(System.Text.StringBuilder text, JsonNode[] battles)
    {
        var stats = battles.SelectMany(x => x.At("heroes").A()).GroupBy(x => x.At("name").S())
            .Select(g => new
            {
                Name = g.Key,
                Damage = g.Sum(x => x.At("damage").N()),
                Duration = battles.Where(b => b.At("heroes").A().Any(h => h.At("name").S() == g.Key)).Sum(b => b.At("durationSec").N()),
                Taken = g.Sum(x => x.At("damageTaken").N()),
                HpLoss = g.Sum(x => x.At("hpLoss").N()),
                HpKnown = g.Count(x => x.At("hpLoss") != null),
                Count = g.Count(),
                Sources = g.All(x => x.At("damageSources") == null) ? "未记录" : string.Join(" / ", new[] { "reaction", "relic", "skill", "other" }.Select(k => g.Sum(x => x.At("damageSources", k).N()).ToString("N0"))),
                Critical = g.Sum(x => x.At("criticalEvents").N()),
                Skills = g.Sum(x => x.At("skillEvents").N()),
                Reactions = g.Sum(x => x.At("reactionEvents").N())
            }).OrderByDescending(x => x.Damage).ToArray();
        double total = stats.Sum(x => x.Damage);
        foreach (var x in stats)
            text.AppendLine($"| {x.Name} | {x.Damage:N0} | {(total > 0 ? x.Damage / total : 0):P1} | {(x.Duration > 0 ? (x.Damage / x.Duration).ToString("N1") : "未确认")} | {x.Taken:N0} | {(x.HpKnown > 0 ? x.HpLoss.ToString("N0") : "未确认")} ({x.HpKnown}/{x.Count}) | {x.Critical:N0} | {x.Skills:N0} | {x.Reactions:N0} | {x.Sources} |");
        text.AppendLine("\n输出来源按反应 → 遗物 → 技能 → 其他的顺序互斥归类，不重复相加；其他包含无来源标记的普通伤害。DPS按角色出现的战斗时长加权，截断时是下界。旧记录缺少来源细分时不做反推。\n");
    }

    static void WriteInsights(System.Text.StringBuilder text, JsonNode[] battles)
    {
        if (battles.Any(b => b.At("truncated").B()))
            text.AppendLine("\n分析提示：部分日志截断，先比较未截断的同层战斗，再评估装备或策略收益。\n");
        var losses = battles.Where(b => !b.At("won").B()).SelectMany(b => b.At("heroes").A())
            .Where(h => h.At("hpLoss").N() > 0).GroupBy(h => h.At("name").S())
            .Select(g => new { Name = g.Key, Loss = g.Sum(h => h.At("hpLoss").N()) }).OrderByDescending(x => x.Loss).FirstOrDefault();
        if (losses != null)
            text.AppendLine($"\n分析提示：战败场中{losses.Name}结算生命损失合计 {losses.Loss:N0}，建议优先核对该角色的站位、生存装备与护盾覆盖；这些记录不能单独证明最优配装。\n");
    }

    static void WriteCardRows(System.Text.StringBuilder text, JsonNode[] battles)
    {
        var procs = battles.SelectMany(x => x.At("cardProcs").A())
            .GroupBy(x => x.At("id").S(), StringComparer.Ordinal)
            .Select(g => new
            {
                Id = g.Key,
                Name = g.First().At("name").S(),
                Effect = g.First().At("description").S(),
                Damage = g.Sum(x => x.At("damage").N()),
                Events = g.Sum(x => x.At("events").N()),
                Total = battles.Sum(x => x.At("cardProcs").A().Sum(p => p.At("damage").N()))
            }).Where(x => x.Id != "").OrderByDescending(x => x.Damage).ToArray();
        if (procs.Length == 0)
        {
            text.AppendLine("没有收到带 `rel` 标记的遗物触发事件；卡牌效果仍列于每场原始回执。\n");
            return;
        }
        double totalDamage = battles.Sum(b => b.At("heroes").A().Sum(h => h.At("damage").N()));
        text.AppendLine("| 遗物 | 已观测效果 | `rel` 触发伤害 | 已标记触发伤害占比 | 占全队输出 | 事件数 |\n|---|---|---:|---:|---:|---:|");
        foreach (var x in procs)
        {
            double pct = x.Total > 0 ? x.Damage / x.Total : 0;
            text.AppendLine($"| {x.Name} (`{x.Id}`) | {x.Effect.Replace("|", "/")} | {x.Damage:N0} | {pct:P1} | {(totalDamage > 0 ? x.Damage / totalDamage : 0):P1} | {x.Events:N0} |");
        }
        text.AppendLine();
    }

    static void WriteDamageDetails(System.Text.StringBuilder text, JsonNode[] battles)
    {
        text.AppendLine("### 具体技能与伤害明细\n\n按反应 → 遗物 → 技能 → 无来源标记互斥归类。命中事件数不是施放次数；没有名称映射时保留原始 ID，旧记录不反推技能。占比以该角色全部输出为分母。\n");
        foreach (var heroes in battles.SelectMany(b => b.At("heroes").A()).GroupBy(h => h.At("name").S()))
        {
            text.AppendLine($"#### {heroes.Key.Replace("|", "/")}\n\n| 来源 | 技能 / 效果 | 输出 | 占角色输出 | 命中事件 |\n|---|---|---:|---:|---:|");
            double total = heroes.Sum(h => h.At("damage").N());
            var details = heroes.SelectMany(h => h.At("damageDetails").A())
                .GroupBy(d => (Kind: d.At("kind").S(), Id: d.At("id").S()))
                .OrderByDescending(g => g.Sum(d => d.At("damage").N()));
            foreach (var group in details)
            {
                double damage = group.Sum(d => d.At("damage").N());
                var named = group.LastOrDefault(d => d.At("name").S() != "" && d.At("name").S() != group.Key.Id) ?? group.Last();
                text.AppendLine($"| {DetailKind(group.Key.Kind)} | {GameText.Name(group.Key.Kind, group.Key.Id, named.At("name").S(group.Key.Id)).Replace("|", "/")} (`{group.Key.Id.Replace("|", "/")}`) | {damage:N0} | {(total > 0 ? damage / total : 0):P1} | {group.Sum(d => d.At("events").N()):N0} |");
            }
            double legacy = heroes.Where(h => h.At("damageDetails") == null).Sum(h => h.At("damage").N());
            if (legacy > 0) text.AppendLine($"| 未细分 | 旧记录未细分 | {legacy:N0} | {(total > 0 ? legacy / total : 0):P1} | 未确认 |");
            text.AppendLine();
            DamageReport.Write(text, heroes);
        }
    }

    static string DetailKind(string kind) => kind switch { "reaction" => "反应", "relic" => "遗物", "skill" => "技能", _ => "无来源标记" };

    static object[] HeroStats(JsonNode? result, JsonNode[] roster, JsonNode[] events, JsonNode? labels, object[] cards, JsonNode? context)
    {
        var damageById = result.At("sima", "dano");
        var cardMap = cards.Select(c => JsonSerializer.SerializeToNode(c, J.Options)!).ToArray();
        return roster.Select(hero =>
        {
            string id = hero.At("id").S();
            string role = FormationRules.Name(hero.At("defId").S());
            if (string.IsNullOrEmpty(role)) role = hero.At("nombre").S(id);
            string name = hero.At("nombre").S(hero.At("name").S(role));
            var aliases = new HashSet<string>(new[] { id, name, role }.Where(x => x != ""), StringComparer.OrdinalIgnoreCase);
            var sourced = events.Where(e => aliases.Contains(e.At("src").S())).ToArray();
            var damageEvents = sourced.Where(e => e.At("side").S() == "ally" && e.At("dmg").N() > 0).ToArray();
            string Source(JsonNode e) => DamageBreakdown.Kind(e);

            string DetailName(string kind, string key) => GameText.Name(kind, key, kind switch
            {
                "skill" => labels.At("skills", id, key).S(hero.At("skills").A().FirstOrDefault(s => s.At("id").S() == key).At("name").S(key)),
                "reaction" => labels.At("reactions", key).S(key),
                "relic" => cardMap.FirstOrDefault(c => c.At("id").S() == key).At("name").S(key),
                _ => key == "channel-unmarked" ? "引导命中（来源未标记）" : key == "periodic-unmarked" ? "周期命中（来源未标记）" : "无来源标记的命中"
            });
            var observed = BattleContext.Hero(context, id);
            string Describe(string kind, string key) => kind == "skill"
                ? observed.At("skills").A().FirstOrDefault(s => s.At("id").S() == key).At("desc") is JsonNode desc
                    ? GameText.Description(kind,key,desc.S(),hero.At("defId").S()) : ""
                : GameText.Description(kind,key);
            var damageDetails = DamageBreakdown.Details(damageEvents, DetailName, Describe, observed == null ? "not-recorded" : "battle-context");
            var incoming = events.Where(e => e.At("side").S() == "enemy" && aliases.Contains(e.At("dst").S()) && e.At("dmg").N() > 0).ToArray();
            double? hpLoss = damageById is JsonObject map && map.TryGetPropertyValue(id, out var total) ? total.N() : null;
            double outgoing = sourced.Where(e => e.At("side").S() == "ally").Sum(e => Math.Max(0, e.At("dmg").N()));
            return new
            {
                id,
                name = role == "" ? name : role,
                defId = hero.At("defId").S(),
                damage = outgoing,
                hpLoss,
                dps = result.At("durationSec").N() > 0 ? outgoing / result.At("durationSec").N() : 0,
                durationSec = result.At("durationSec").N(),
                damageSources = new[] { "reaction", "relic", "skill", "other" }.ToDictionary(k => k, k => damageEvents.Where(e => Source(e) == k).Sum(e => e.At("dmg").N())),
                damageDetails,
                detailVersion = 3,
                loadout = BattleContext.Build(context, id),
                skillActivity = DamageBreakdown.Skills(sourced, DetailName, Describe),
                largestHits = DamageBreakdown.LargestHits(damageEvents, DetailName),
                damageSource = "ally-battle-events",
                damageTaken = incoming.Sum(e => e.At("dmg").N()),
                criticalEvents = sourced.Count(e => e.At("side").S() == "ally" && e.At("crit").B()),
                skillEvents = sourced.Count(e => e.At("side").S() == "ally" && e.At("skill").S() != "" && e.At("dmg").N() > 0),
                reactionEvents = sourced.Count(e => e.At("side").S() == "ally" && e.At("reaction").S() != "" && e.At("dmg").N() > 0),
                healingObserved = sourced.Where(e => e.At("side").S() == "ally" && e.At("dmg").N() < 0).Sum(e => Math.Abs(e.At("dmg").N()))
            };
        }).Cast<object>().ToArray();
    }

    static object[] CardProcs(object[] cards, JsonNode[] events)
    {
        var cardMap = cards.Select(x => JsonSerializer.SerializeToNode(x, J.Options)!).ToArray();
        double allCardDamage = events.Where(e => e.At("side").S() == "ally" && e.At("rel").S() != "" && e.At("dmg").N() > 0).Sum(e => e.At("dmg").N());
        return events.Where(e => e.At("side").S() == "ally" && e.At("rel").S() != "")
            .GroupBy(e => e.At("rel").S(), StringComparer.Ordinal)
            .Select(g =>
            {
                var card = cardMap.FirstOrDefault(x => x.At("id").S() == g.Key);
                return (object)new
                {
                    id = g.Key,
                    name = card.At("name").S(g.Key),
                    description = card.At("description").S("效果描述未包含在本次回执中"),
                    damage = g.Sum(e => Math.Max(0, e.At("dmg").N())),
                    events = g.Count(),
                    triggerUnits = g.Sum(e => Math.Max(1, e.At("relN").N(1))),
                    battleDamage = allCardDamage
                };
            }).ToArray();
    }

    static double? Difference(JsonNode? before, JsonNode? after, string key) =>
        before.At(key) == null || after.At(key) == null ? null : after.At(key).N() - before.At(key).N();

    static object ResourceDelta(JsonNode? before, JsonNode? after) => new
    {
        cores = Difference(before, after, "nucleos"),
        shards = Difference(before, after, "esquirlas"),
        obols = Difference(before, after, "obolos")
    };
}
