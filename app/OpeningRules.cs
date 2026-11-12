using System.Text.Json.Nodes;

namespace FrostAbyss;

// All strategies share one opening. Only confirmed arrival/carry relics count;
// badges and temporary effects never satisfy the four-relic collection goal.
static class OpeningRules
{
    internal static JsonNode[] InheritedBuild(JsonNode? run)
    {
        if (run == null) return [];
        if (CardRules.ArrivalBaseline(run) is { } arrival) return arrival;
        return run.At("guardaPendiente", "cartas").A().Any() ? Cards.Inherited(run) : [];
    }
    internal static bool Freeze(IEnumerable<JsonNode> cards, Settings cfg) => cards.Any(c => Cards.IsCore(c, cfg));
    internal static bool Bounce(IEnumerable<JsonNode> cards) => cards.Any(c => CardRules.AllTargetEvery(c) > 0);
    internal static bool Complete(IEnumerable<JsonNode> relics, Settings cfg) =>
        relics.Any(c => SynergySelection.Glacier(c, cfg))
        || relics.Count(c => Cards.Family(c) == "escarcha") >= SynergySelection.Target;
    internal static bool Complete(JsonNode? run, Settings cfg) => run != null && Complete(InheritedBuild(run), cfg);
    internal static bool UsesSearch(Settings cfg, JsonNode? run) => cfg.StrategyId >= 5 && Complete(run, cfg);
    internal static bool UsesLegacy(Settings cfg, JsonNode? run) => cfg.StrategyId == 2 && Complete(run, cfg);

    internal static Choice[] Choices(JsonNode[] offered, string method, JsonNode run, Settings cfg)
    {
        var legal = offered.Where(CardRules.Available).ToArray();
        if (method == "sima.guarda")
        {
            var uncapped = legal.Where(c => c.At("llevas").N() < 3
                && Cards.Inherited(run).Count(x => x.At("id").S() == c.At("id").S()) < 3).ToArray();
            if (uncapped.Length > 0) legal = uncapped;
        }
        var ice = legal.Where(c => Cards.Family(c) == "escarcha").ToArray();
        bool fallback = ice.Length == 0;
        if (!fallback) legal = ice;
        else if (method == "sima.trade") return [];
        if (legal.Length == 0)
        {
            if (method == "sima.trade") return [];
            throw new Exception("游戏菜单没有实际可选的合法卡牌。");
        }
        // The opening is independent of strategy-four custom preferences.
        var normal = legal.Where(c => SynergySelection.Glacier(c, cfg)
            || SynergySelection.SkipReason(c, method, run, cfg) == null).ToArray();
        if (normal.Length > 0) legal = normal;
        else if (method == "sima.trade") return [];
        else legal = SynergySelection.PreferSoftFallback(legal, method, run, cfg);
        var held = SynergySelection.CombatBaseline(run, method);
        var relics = method == "sima.guarda" ? InheritedBuild(run) : run.At("reliquias").A().ToArray();
        bool haveGlacier = relics.Any(c => SynergySelection.Glacier(c, cfg));
        return legal.Select(c =>
        {
            bool glacier = SynergySelection.Glacier(c, cfg);
            // Plain freeze is not a separate opening target. Other ice cards
            // build the same four-card synergy and may supply additional value.
            bool plainFreeze = Cards.IsCore(c, cfg) && !glacier && CardRules.AllTargetEvery(c) == 0
                && !CardRules.HasSupplementalValue(c, cfg);
            int priority = !fallback && glacier && !haveGlacier ? 200_000 : plainFreeze ? 90_000 : 100_000;
            return new Choice((fallback ? "前置兜底：" : method == "sima.guarda" ? "前置继承：" : "冰系4卡前置：") + CardCatalog.Name(c),
                method, new() { ["id"] = c.At("id").S() }, benefit: CardValue.Marginal(c, held, cfg),
                core: Cards.IsCore(c, cfg), priority: priority, card: c)
            { Reason = "全部策略共用前置：优先冰川（单卡冻结＋全体弹跳），没有冰川就凑冰系4张，不单独追普通冻结；"
                + (fallback ? "本菜单无合法冰牌，择优过渡，下次继续补冰；" : "")
                + "确认继承冰川或4张冰系遗物后启用所选策略，徽章与赐福不计遗物张数。" };
        }).ToArray();
    }
}
