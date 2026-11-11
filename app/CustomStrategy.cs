using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

sealed class CardPreference
{
    public int Priority { get; set; } = 999;
    public string Mode { get; set; } = "normal";
    public int TargetCopies { get; set; } = 3;
}

sealed class CustomStrategySettings
{
    public string[] FamilyOrder { get; set; } = CardCatalog.DefaultFamilies.ToArray();
    public Dictionary<string, string[]> CategoryOrder { get; set; } = [];
    public Dictionary<string, CardPreference> Cards { get; set; } = [];
    public bool AvoidOwnedEffects { get; set; } = true;

    public void Validate()
    {
        if (FamilyOrder == null || FamilyOrder.Length != 7 || FamilyOrder.Distinct().Count() != 7
            || FamilyOrder.Any(f => !CardCatalog.Families.Contains(f)))
            throw new Exception("策略四：系列顺序须包含七个不同系列。");
        if (CategoryOrder == null || CategoryOrder.Any(p => !CardCatalog.Families.Contains(p.Key) || p.Value == null
            || p.Value.Length != CardCatalog.Categories.Length || p.Value.Distinct().Count() != p.Value.Length
            || p.Value.Any(id => !CardCatalog.Categories.Contains(id))))
            throw new Exception("策略四：细分类顺序无效，请恢复默认排序后重试。");
        if (Cards == null || Cards.Count > 2000 || Cards.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 128 || p.Value == null
            || p.Value.Priority is < 1 or > 999 || p.Value.TargetCopies is < 0 or > 99 || p.Value.Mode is not ("normal" or "avoid" or "block")))
            throw new Exception("策略四：单卡优先级须为1～999，目标张数须为0～99，0表示不限。");
    }
}

static class CardCatalog
{
    internal static readonly JsonNode Data = Load();
    internal static readonly JsonNode Defaults = Load("strategy4-defaults.json");
    internal static readonly string[] DefaultFamilies = Defaults.At("familyOrder").A().Select(c => c.S()).ToArray();
    internal static readonly string[] Families = Data.At("families").A().Select(c => c.At("id").S()).ToArray();
    internal static readonly string[] Categories = Data.At("categories").A().Select(c => c.At("id").S()).ToArray();
    static readonly Dictionary<string, JsonNode> Entries = Data.At("cards").A().ToDictionary(c => c.At("id").S());
    static JsonNode Load(string name = "card-catalog.json")
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        return JsonNode.Parse(stream)!;
    }
    internal static JsonNode? Find(string id) => Entries.GetValueOrDefault(id);
    internal static string[] DefaultCategories(string family) => Defaults.At("categoryOrder", family).A().Select(c => c.S()).ToArray() is { Length: > 0 } order ? order : Categories;
    internal static CardPreference DefaultPreference(string id) => new() { Priority = (int)Defaults.At("cardPriority", id).N(999), Mode = Defaults.At("cardMode", id).S("normal") };
    internal static string Category(JsonNode card, Settings cfg)
    {
        var known = Find(card.At("id").S());
        if (known != null && known.At("verified").B()) return known.At("category").S();
        var f = Effects.Parse([card], cfg);
        var a = CardRules.Applications(card, cfg);
        if (f.Cleave > 0) return "bounce";
        if (IceStrategyRules.GuaranteedCritEvery(card) > 0) return "critical";
        if (Cards.IsCore(card, cfg)) return "freeze";
        if (f.ReactionDamage > 0 || f.Detonate) return "reaction";
        if (f.VsStatus > 0) return "statusDamage";
        if (f.Mark > 0 || f.MarkDeepen > 0 || f.VsMarked > 0) return "mark";
        if (f.Execute > 0) return "execute";
        if (a.HasFlag(BasicEffect.Burn)) return "burn";
        if (a.HasFlag(BasicEffect.Lightning)) return "lightning";
        if (a.HasFlag(BasicEffect.Poison)) return "poison";
        if (f.Slow > 0 || f.VsSlow > 0) return "slow";
        if (f.Shield > 0) return "shield";
        if (f.RampDefense > 0) return "counter";
        if (IceStrategyRules.HasDamage(f)) return "attack";
        return "survival";
    }
    internal static string FamilyName(string id) => Data.At("families").A().FirstOrDefault(c => c.At("id").S() == id).At("name").S("其他系列");
    internal static string CategoryName(string id) => Data.At("categories").A().FirstOrDefault(c => c.At("id").S() == id).At("name").S("其他效果");
    internal static bool IsCore(JsonNode card, Settings cfg) => Cards.IsCore(card, cfg) || Find(card.At("id").S()).At("core").B();
    internal static string Name(JsonNode card) => Find(card.At("id").S()).At("name").S(GameText.IsChinese(J.Name(card)) ? J.Name(card) : "新发现卡牌");
}

// This class is entered only with StrategyId == 4. No custom preference is
// consulted by any of the three existing selection implementations.
static class CustomStrategy
{
    internal const string Description = "策略四：开局优先寒冰和普攻冰冻；继承有普攻冰冻必选，没有则择优带走过渡遗物，下一层继续寻找。实际回执确认冰冻已继承后才启用自定义。数字越小越先选；软避让和拉黑逐级兜底，不因选牌偏好暂停。徽章和商店赐福不能代替遗物继承。";
    static CardPreference Preference(JsonNode c, Settings cfg) => cfg.CustomStrategy.Cards.GetValueOrDefault(c.At("id").S()) ?? CardCatalog.DefaultPreference(c.At("id").S());
    static JsonNode[] Baseline(JsonNode? run, string method) => method == "sima.guarda" && run != null ? Cards.Inherited(run)
        : method == "sima.reliquia" ? run.At("reliquias").A().ToArray() : Cards.Current(run);
    // Only arrival relics confirm inheritance. Old or incomplete snapshots stay
    // in the fixed opening stage instead of treating a new relic as inherited.
    internal static bool Inherited(JsonNode? run, Settings cfg) => run != null
        && CardRules.ArrivalBaseline(run) is { } arrival && arrival.Any(c => CardCatalog.IsCore(c, cfg));

    internal static string? SoftReason(JsonNode c, JsonNode? run, Settings cfg, string method)
    {
        var pref = Preference(c, cfg);
        if (pref.Mode == "avoid") return "手动软避让";
        var baseline = Baseline(run, method);
        var copyBaseline = method == "sima.guarda" ? baseline : run.At(method == "sima.blason" ? "blasones" : method == "sima.trade" ? "buffs" : "reliquias").A();
        int copies = copyBaseline.Count(x => x.At("id").S() == c.At("id").S());
        if (pref.TargetCopies > 0 && copies >= pref.TargetCopies) return "已达到设定目标张数";
        if (!cfg.CustomStrategy.AvoidOwnedEffects) return null;
        var apps = CardRules.Applications(c, cfg);
        var held = baseline.Aggregate(BasicEffect.None, (a, x) => a | CardRules.Applications(x, cfg));
        int bounce = CardRules.AllTargetEvery(c), heldBounce = Effects.Parse(baseline, cfg).Cleave;
        if (bounce > 0 && heldBounce > 0 && bounce >= heldBounce) return "已有同档或更快全体弹跳";
        int crit = IceStrategyRules.GuaranteedCritEvery(c);
        if (crit > 0 && baseline.Select(IceStrategyRules.GuaranteedCritEvery).Any(x => x > 0 && x <= crit)) return "已有同档或更快必定暴击";
        if (apps != BasicEffect.None && (apps & ~held) == BasicEffect.None && bounce == 0 && !CardRules.HasSupplementalValue(c, cfg))
            return "普攻独立效果已有，且无额外增益";
        return null;
    }

    internal static (int Priority, string Reason) Score(JsonNode c, Settings cfg, JsonNode? run, string method)
    {
        if (!Inherited(run, cfg))
        {
            if (CardCatalog.IsCore(c, cfg))
                return (20_000_000, "固定前置：优先取得并继承普攻冰冻，本章徽章及新获得遗物不算继承确认");
            if (method != "sima.guarda" && Cards.Family(c) == "escarcha")
                return (19_000_000, "固定前置：冰冻尚未确认继承，优先寒冰系列");
            var transition = Legacy131.Cards.BuildValue(c, cfg, run, method);
            return (1_000_000 + transition.Priority, (method == "sima.guarda"
                ? "冰冻继承前过渡：本次没有可继承普攻冰冻，择优带走遗物继续下潜，下一层继续寻找；"
                : "冰冻继承前过渡：本菜单无可用寒冰，择优补强并继续寻找；") + transition.Reason);
        }
        var settings = cfg.CustomStrategy;
        string family = Cards.Family(c), category = CardCatalog.Category(c, cfg);
        int familyRank = Array.IndexOf(settings.FamilyOrder, family);
        if (familyRank < 0) familyRank = 7;
        var categories = settings.CategoryOrder.GetValueOrDefault(family) ?? CardCatalog.DefaultCategories(family);
        int categoryRank = Array.IndexOf(categories, category);
        if (categoryRank < 0) categoryRank = categories.Length;
        var preference = Preference(c, cfg);
        int priority = (8 - familyRank) * 1_000_000 + (categories.Length - categoryRank) * 20_000 + (1000 - preference.Priority) * 10;
        return (priority, $"自定义：{CardCatalog.FamilyName(family)}第{familyRank + 1}位 → {CardCatalog.CategoryName(category)}第{categoryRank + 1}位 → 单卡优先级{preference.Priority}；同优先级比较推演收益");
    }

    internal static Choice[] Choices(JsonNode[] offered, string method, JsonNode run, Settings cfg)
    {
        bool ready = Inherited(run, cfg);
        var legal = offered.Where(CardRules.Available).ToArray();
        bool cappedFallback = false;
        if (method == "sima.guarda")
        {
            var uncapped = legal.Where(c => c.At("llevas").N() < 3 && Cards.Inherited(run).Count(x => x.At("id").S() == c.At("id").S()) < 3).ToArray();
            cappedFallback = uncapped.Length == 0 && legal.Length > 0;
            if (uncapped.Length > 0) legal = uncapped;
        }
        if (legal.Length == 0)
        {
            if (method == "sima.trade") return [];
            // A malformed/unavailable game menu is a state error, not a preference.
            throw new Exception("游戏菜单没有可执行的合法卡牌，无法派发选牌操作。");
        }
        var available = ready ? legal.Where(c => Preference(c, cfg).Mode != "block").ToArray() : legal;
        bool blockedFallback = available.Length == 0;
        if (blockedFallback)
        {
            if (method == "sima.trade") return [];
            available = legal;
        }
        if (!ready && method == "sima.guarda")
        {
            var cores = available.Where(c => CardCatalog.IsCore(c, cfg)).ToArray();
            if (cores.Length > 0) available = cores;
        }
        bool softFallback = false;
        if (!ready && method != "sima.guarda")
        {
            var ice = available.Where(c => CardCatalog.IsCore(c, cfg) || Cards.Family(c) == "escarcha").ToArray();
            if (ice.Length > 0) available = ice;
            else if (method == "sima.trade") return [];
        }
        else if (ready)
        {
            var normal = available.Where(c => SoftReason(c, run, cfg, method) == null).ToArray();
            softFallback = normal.Length == 0;
            if (!softFallback) available = normal;
            else if (method == "sima.trade") return []; // Optional spending needs no forced-menu fallback.
        }
        return available.Select(c =>
        {
            var score = Score(c, cfg, run, method);
            var reason = softFallback ? "软避让兜底：" + SoftReason(c, run, cfg, method) + "；" + score.Reason : score.Reason;
            if (blockedFallback) reason = "拉黑末位兜底：强制菜单全部被拉黑，择优继续，避免选牌偏好卡住；" + reason;
            if (cappedFallback) reason = "继承上限过渡：全部达到三张上限，选择游戏允许的遗物推进，不增加继承张数；" + reason;
            string verb = method == "sima.guarda" ? "继承遗物：" : method == "sima.blason" ? "本章徽章：" : method == "sima.trade" ? "商店赐福：" : "选择遗物：";
            return new Choice(verb + CardCatalog.Name(c), method, new() { ["id"] = c.At("id").S() }, core: CardCatalog.IsCore(c, cfg), priority: score.Priority, card: c) { Reason = reason };
        }).ToArray();
    }
}
