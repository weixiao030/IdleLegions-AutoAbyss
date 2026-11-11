using System.Text.Json.Nodes;

namespace FrostAbyss;

static class IceStrategyRules
{
    internal const int SeriesTarget = 8;
    internal static readonly string[] Families = ["escarcha", "brasa", "fulgor", "ponzona", "umbra", "aurora", "acero"];
    static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["escarcha"] = "冰", ["brasa"] = "火", ["fulgor"] = "电",
        ["ponzona"] = "毒", ["umbra"] = "暗影", ["aurora"] = "光耀", ["acero"] = "钢"
    };

    internal static string Name(int id) => id switch
    {
        1 => "策略一 · 当前默认",
        2 => "策略二 · 1.3.1 原版",
        3 => "策略三 · ICE 后期",
        4 => "策略四 · 自定义优先级",
        _ => "未知策略"
    };

    internal static string FamilyName(string family) => Names.GetValueOrDefault(family, family);

    // Relics are the collection/carry target. Shop favours and badge choices
    // compare their combat effects against all currently active cards instead.
    internal static bool CollectsRelics(string method) => method is "sima.reliquia" or "sima.guarda";
    internal static JsonNode[] SelectionBaseline(JsonNode? run, string method) =>
        method == "sima.guarda" && run != null ? Cards.Inherited(run)
        : method == "sima.reliquia" ? run.At("reliquias").A().ToArray() : Cards.Current(run);

    internal static string? SkipReason(JsonNode card, JsonNode? run, Settings cfg, string method)
    {
        var disabled = CardRules.DisabledStatus(card, cfg);
        if (disabled != null) return disabled;
        if (CardRules.SeekingInheritedFreeze(run, cfg)
            && !Cards.IsCore(card, cfg) && Cards.Family(card) != "escarcha")
            return "ICE 后期方案：冻结核心继承前优先普攻冻结与冰系牌";

        var baseline = SelectionBaseline(run, method);
        var id = card.At("id").S();
        if (method == "sima.guarda" && id != "" && (baseline.Count(c => c.At("id").S() == id) >= 3
            || card.At("llevas").N() >= 3))
            return "同种牌已达三层继承上限";
        return null;
    }

    internal static int FallbackPenalty(JsonNode card, JsonNode? run, Settings cfg, string method)
    {
        int penalty = CardRules.SeekingInheritedFreeze(run, cfg)
            && !Cards.IsCore(card, cfg) && Cards.Family(card) != "escarcha" ? 6000 : 0;
        if (CardRules.DisabledStatus(card, cfg) != null) penalty += 4000;
        var baseline = SelectionBaseline(run, method);
        var id = card.At("id").S();
        if (method == "sima.guarda" && id != "" && (baseline.Count(c => c.At("id").S() == id) >= 3
            || card.At("llevas").N() >= 3)) penalty += 3500;
        return penalty;
    }

    internal static bool HasPreference(IEnumerable<JsonNode> cards, int rank, Settings cfg) => rank > 0
        && cards.Any(c => Preference(c, Effects.Parse([c], cfg)).Rank == rank);

    internal static int GuaranteedCritEvery(JsonNode card)
    {
        var text = Effects.Normalize(card.At("desc").S() + " " + card.At("descZh").S() + " " + card.At("description").S()).ToLowerInvariant();
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(?:cada\s+(\d+)\s+(?:golpes|autoataques|ataques).{0,50}critico|critico\s+cada\s+(\d+)\s+(?:golpes|autoataques|ataques)|every\s+(\d+)\s+attacks?.{0,30}(?:guaranteed critical|guaranteed crit)|每\s*(\d+)\s*次.{0,25}(?:必暴|必爆|必定暴击))");
        return match.Success ? match.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1).Select(g => int.TryParse(g.Value, out var value) ? value : 0).FirstOrDefault(x => x > 0) : 0;
    }

    // Preferences describe effects, never positions in the three-card menu.
    // Missing preferences follow missing freeze/bounce/critical cores. Each
    // effect category gets its first-copy bonus only once per baseline.
    // Badges and shop favours never add relic family counts.
    internal static (int Rank, string Reason) Preference(JsonNode card, Effects fx)
    {
        var family = Cards.Family(card);
        var text = Effects.Normalize(card.At("desc").S() + " " + card.At("descZh").S()
            + " " + card.At("description").S()).ToLowerInvariant();
        bool Match(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
        bool lowLifeShadow = family == "umbra" && Match(@"25\s*%") && Match(@"40\s*%")
            && Match(@"(?:tu vida|tu salud|estas|estando|生命低于|血量低于)") && Match(@"dano|伤害");
        bool status = fx.VsStatus > 0 || Match(@"(?:带有状态|异常状态|状态目标).{0,20}(?:伤害|增伤).{0,8}\+?\d+%");
        if (lowLifeShadow || family == "umbra" && (Match(@"(?:(?:vida|salud|estas|estando).{0,40}25\s*%|25\s*%.{0,15}(?:vida|salud)).{0,60}40\s*%")
            || Match(@"(?:生命|血量)低于\s*25\s*%.{0,15}伤害\s*\+?40\s*%")))
            return (11, "暗影低血增伤：自身生命低于25%时伤害+40%，优先于标记加深");
        if (family == "ponzona" && (fx.VsStatus >= .3999 || status && Match(@"(?:伤害|增伤)\s*\+?40\s*%")))
            return (10, "瘴气：对带有状态目标伤害+40%，优先于标记及燃烧反应牌");
        if (family == "ponzona" && (fx.Execute >= .2499 && fx.ExecuteAt <= .3001 && fx.ExecuteAt > 0
            || Match(@"(?:生命|血量)低于\s*30\s*%.{0,15}伤害\s*\+?25\s*%")))
            return (9, "腐肉：对生命低于30%的目标伤害+25%，优先于灰烬与先锋");
        if (family == "brasa" && (fx.ReactionDamage > 0 || Match(@"(?:元素)?反应伤害\s*\+?\d+\s*%")))
            return (8, "火系反应增伤，优先于火系状态增伤与普攻燃烧");
        if (family == "brasa" && status)
            return (7, "火系状态增伤（火星/灰烬），优先于普攻燃烧与先锋");
        if (family == "ponzona" && status)
            return (6, "毒系状态增伤（坏疽），优先于麻痹与普攻中毒");
        if (fx.RampDefense > 0 || Match(@"每承受一次攻击防御.{0,10}4\s*%")
            || Match(@"cada (?:golpe|ataque).{0,40}defensa.{0,10}4\s*%"))
            return (5, "先锋：承伤叠防与反击，次于必暴击和指定增伤，优先于低血防御净化");
        if (family == "ponzona" && (fx.Slow > 0 || Match(@"(?:减速|更慢)\s*10\s*%")))
            return (4, "麻痹：攻击减速，次于坏疽、优先于普攻中毒");
        if (family == "umbra" && (fx.MarkDeepen > 0 || Match(@"marcas mas hondo|标记更深|标记加深")))
            return (3, "暗影标记加深：没有低血增伤时优先");
        return (0, "");
    }

    internal static string Description(int strategy) => strategy switch
    {
        4 => CustomStrategy.Description,
        2 => "使用 v1.3.1 原始选牌、商店判断、目标组合及模拟器；独立于策略一和 ICE 的新增偏好。",
        3 => "普攻冻结确认继承前优先冰系；继承后依次补齐每4/5/6次全体弹跳与每8/10次必暴击。七个系列各凑8张遗物，徽章及商店赐福不计数；先补缺失的指定效果，每类已有一张后回到系列补齐。本系已满8张的指定效果让位给未满系列，缺失弹跳与必暴击仍必拿。暗影低血增伤优先、标记加深次选；瘴气40%状态增伤、腐肉、火反应增伤、火状态增伤、坏疽、先锋、麻痹按效果比较。同种遗物可在本层继续获取，跨层继承仍最多3张；同系同效果优先补未持有牌；商店及徽章按当前效果选择。菜单左右换位不影响排序。",
        _ => "普攻冻结确认继承前优先冰系（含减速），本章冻结及临时徽章不能替代继承；确认继承后依次补新增或更快全体弹跳、独立普攻各一份、引爆器一份、火增伤与其他输出，优先避开冰减速，防御少量补充。无合适候选时择优兜底。"
    };

    internal static bool HasElementBonus(JsonNode card) => System.Text.RegularExpressions.Regex.IsMatch(
        Effects.Normalize(card.At("desc").S() + " " + card.At("descZh").S()).ToLowerInvariant(),
        @"reacciones elementales|dano elemental|元素(?:反应|伤害|加成)");

    internal static bool HasDamage(Effects fx) => fx.Damage > 0 || fx.Speed > 0 || fx.Crit > 0
        || fx.CritDamage > 0 || fx.VsSlow > 0 || fx.VsStatus > 0 || fx.VsElite > 0
        || fx.Execute > 0 || fx.ReactionDamage > 0 || fx.VsMarked > 0;

    internal static object[] Progress(JsonNode? run, Settings cfg)
    {
        var cards = run.At("reliquias").A().ToArray();
        var inherited = run == null ? [] : CardRules.InheritedBaseline(run);
        bool known = run == null || CardRules.ArrivalBaseline(run) != null
            || run.At("guardaPendiente", "cartas").A().Any();
        return Families.Select(f => new
        {
            family = f,
            name = FamilyName(f),
            count = CountFamily(cards, f),
            inheritedCount = known ? (int?)CountFamily(inherited, f) : null,
            target = cfg.StrategyId == 3 ? SeriesTarget : 0
        }).ToArray();
    }

    internal static int CountFamily(IEnumerable<JsonNode> cards, string family) =>
        cards.Count(c => Cards.Family(c) == family);
}

static class LegacyStrategySource
{
    internal const string SourceHash = "94f60887ee43cb104213411415a988469dbcb0f67b3475d20f36d09cd1535507";
}
