using System.Text.Json.Nodes;

namespace FrostAbyss;

// Collection goals use relic counts. Badges supply real combat effects but
// never count as relics; acquisition and the single carry slot have distinct
// baselines, so temporary progress cannot masquerade as inherited progress.
static class SynergySelection
{
    internal const int Target = 4;
    internal static bool Uses(Settings cfg) => cfg.StrategyId is 1 or 3 or 4;
    internal static JsonNode[] Baseline(JsonNode? run, string method) => method == "sima.guarda" && run != null
        ? Cards.Inherited(run) : run.At("reliquias").A().ToArray();
    internal static bool Glacier(JsonNode c, Settings cfg) => Cards.IsCore(c, cfg) && CardRules.AllTargetEvery(c) is > 0 and <= 5;
    internal static bool OpeningComplete(IEnumerable<JsonNode> relics, Settings cfg) =>
        relics.Any(c => Glacier(c, cfg)) || relics.Count(c => Cards.Family(c) == "escarcha") >= Target;
    internal static bool OpeningComplete(JsonNode? run, Settings cfg, string method = "sima.guarda") => OpeningComplete(Baseline(run, method), cfg);
    internal static bool Complete(JsonNode? run, Settings cfg, string method) =>
        IceStrategyRules.Families.All(f => Baseline(run, method).Count(c => Cards.Family(c) == f) >= Target);
    internal static bool LightKeeper(JsonNode c) => c.At("id").S() is "au_fervor" or "au_letania"
        || Cards.Family(c) == "aurora" && System.Text.RegularExpressions.Regex.IsMatch(
            Effects.Normalize(c.At("desc").S() + " " + c.At("descZh").S() + " " + c.At("description").S()).ToLowerInvariant(),
            @"(?:cada (?:autoataque|golpe basico)|(?:every|each) (?:basic |auto[ -]?)?attack|每(?:次)?普攻).{0,30}(?:fervor|狂热)");
    internal static bool LightPreset(Settings cfg) => cfg.StrategyId == 4 && cfg.CustomStrategy.LightFervorOnly;
    internal static JsonNode[] CombatBaseline(JsonNode run, string method) => method == "sima.guarda"
        ? Cards.Inherited(run).Concat(Cards.RetainedBadges(run, Cards.Inherited(run))).ToArray() : Cards.Current(run);
    internal static string? SkipReason(JsonNode c, string method, JsonNode run, Settings cfg)
    {
        var disabled = CardRules.DisabledStatus(c, cfg); if (disabled != null) return disabled;
        if (Cards.SkipEarlyShield(c, run, cfg)) return "前期护盾软避让";
        var baseline = CombatBaseline(run, method);
        if (method == "sima.guarda" && (c.At("llevas").N() >= 3 || Cards.Inherited(run).Count(x => x.At("id").S() == c.At("id").S()) >= 3)) return "同种遗物已达继承上限";
        var app = CardRules.Applications(c, cfg);
        var held = baseline.Aggregate(BasicEffect.None, (a, x) => a | CardRules.Applications(x, cfg));
        if (app != BasicEffect.None && (app & ~held) == BasicEffect.None && CardRules.AllTargetEvery(c) == 0 && !CardRules.HasSupplementalValue(c, cfg))
            return "独立效果已有，未识别到额外增益";
        return null;
    }

    internal static int SoftPenalty(JsonNode c, string method, JsonNode run, Settings cfg)
    {
        if (method == "sima.guarda" && (c.At("llevas").N() >= 3 || Cards.Inherited(run).Count(x => x.At("id").S() == c.At("id").S()) >= 3)) return 100_000;
        if (CardRules.DisabledStatus(c, cfg) != null) return 10_000;
        if (Cards.SkipEarlyShield(c, run, cfg)) return 100;
        return SkipReason(c, method, run, cfg) != null ? 200 : 0;
    }
    internal static JsonNode[] PreferSoftFallback(JsonNode[] cards, string method, JsonNode run, Settings cfg)
    {
        if (cards.Length == 0) return cards;
        int penalty = cards.Min(c => SoftPenalty(c, method, run, cfg));
        return cards.Where(c => SoftPenalty(c, method, run, cfg) == penalty).ToArray();
    }

    internal static Choice[] Opening(JsonNode[] offered, string method, JsonNode run, Settings cfg)
    {
        var legal = offered.Where(CardRules.Available).ToArray();
        if (method == "sima.guarda")
        {
            var free = legal.Where(c => c.At("llevas").N() < 3 && Cards.Inherited(run).Count(x => x.At("id").S() == c.At("id").S()) < 3).ToArray();
            if (free.Length > 0) legal = free;
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
        var normal = legal.Where(c => SkipReason(c, method, run, cfg) == null).ToArray();
        if (normal.Length > 0) legal = normal;
        else if (method == "sima.trade") return [];
        else legal = PreferSoftFallback(legal, method, run, cfg);
        var held = CombatBaseline(run, method);
        return legal.Select(c => new Choice((fallback ? "冰系过渡兜底：" : method == "sima.guarda" ? "优先继承冰系：" : "冰系4张前置：") + CardCatalog.Name(c),
            method, new() { ["id"] = c.At("id").S() }, benefit: CardValue.Marginal(c, held, cfg),
            core: Cards.IsCore(c, cfg), priority: !fallback && Glacier(c, cfg) ? 200_000 : 100_000, card: c)
            { Reason = (fallback ? "本菜单无合法冰牌，择优过渡，下次继续补冰系；" : "冰川优先，否则先补冰系至4张；普通冻结不重复置顶，按实际新增收益比较；")
                + (method == "sima.guarda" ? "仅按已继承遗物核对进度" : "本层收集进度与章末继承分别核对") }).ToArray();
    }

    internal static Choice[] Apply(Choice[] choices, string method, JsonNode run, Settings cfg)
    {
        if (choices.Length == 0) return choices;
        var baseline = Baseline(run, method); var held = CombatBaseline(run, method);
        if (method == "sima.guarda" && LightPreset(cfg))
        {
            var keep = choices.Where(c => Cards.Family(c.Card) != "aurora" || LightKeeper(c.Card!)
                && !baseline.Any(x => x.At("id").S() == c.Card.At("id").S())).ToArray();
            if (keep.Length > 0) choices = keep;
            else
            {
                var fervor = choices.Where(c => LightKeeper(c.Card!)).ToArray();
                if (fervor.Length > 0) choices = fervor;
                foreach (var c in choices) c.Reason = "光系特殊继承：没有其他可继承牌，合法过渡；" + c.Reason;
            }
        }
        bool relic = method is "sima.reliquia" or "sima.guarda";
        bool complete = Complete(run, cfg, method);
        bool customFamily = cfg.StrategyId == 4 && cfg.CustomStrategy.SelectionMode == "family";
        return choices.Select(c =>
        {
            if (c.Card == null) return c;
            string family = Cards.Family(c.Card);
            int count = baseline.Count(x => Cards.Family(x) == family);
            double gain = CardValue.Marginal(c.Card, held, cfg);
            int priority = c.Priority;
            if (cfg.StrategyId is 1 or 3) priority = 0;
            string stage = "";
            if (relic && !complete)
            {
                bool needed = IceStrategyRules.Families.Contains(family) && count < Target;
                priority = needed ? 10_000_000 + (customFamily ? c.Priority : count * 100) : 0;
                stage = needed ? $"补齐{IceStrategyRules.FamilyName(family)}系 {count}/4，已满系列让位；" : "该系已满4张，优先补齐其他系列；";
                if (method == "sima.guarda" && LightPreset(cfg) && LightKeeper(c.Card)
                    && !baseline.Any(x => x.At("id").S() == c.Card.At("id").S()))
                { priority = 20_000_000; stage = "光系特殊继承：优先保留首份普攻狂热牌，其他光牌留本层凑羁绊；"; }
            }
            else if (relic && complete)
            { priority = 0; stage = "七系均已达到4张，忽略系列排序，仅比较单卡新增收益；"; }
            // Do not let the old fixed fire/ice priorities decide between full
            // families. Sampling, when enabled, still compares final forecasts.
            if (relic && (complete || priority == 0)) priority = 0;
            return new Choice(c.Label, c.Method, c.Args, benefit: gain, risk: c.Risk, core: c.Core, priority: priority, card: c.Card)
                { Reason = stage + c.Reason };
        }).ToArray();
    }
}
