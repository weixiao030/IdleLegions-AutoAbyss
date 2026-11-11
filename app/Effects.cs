using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FrostAbyss;

// Original interpretation of player-visible descriptions. No extracted game code.
// Magnitudes are read from the offered card, not guessed from rarity or family.
sealed class Effects
{
    public double Damage, Life, Defense, Speed, Crit, CritDamage, Leech, Shield, KillHeal, KillImmune, ReactionDamage;
    public double Slow, SlowSeconds, VsSlow, VsStatus, VsElite, Execute, ExecuteAt, RampDefense, RampCap, LowDefense, LowAt;
    public double Mark, MarkDeepen, VsMarked;
    public int MarkCap, Cleave;
    public bool Freeze, Burn, Lightning, Poison, Taunt, ConsumesStatus, Detonate;
    public List<string> Unknown = [];
    internal static string Normalize(string s) => new(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    static double Read(string s, string regex, int group = 1)
    {
        var m = Regex.Match(s, regex); return m.Success && double.TryParse(m.Groups[group].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
    }
    const string Num = @"([0-9]+(?:[.,][0-9]+)?)";
    public static Effects Parse(IEnumerable<JsonNode> cards, Settings cfg)
    {
        var e = new Effects();
        foreach (var c in cards)
        {
            var before = e.Signature();
            var t = Normalize(c.At("desc").S()).ToLowerInvariant();
            var applications = CardRules.Applications(c, cfg);
            e.Freeze |= applications.HasFlag(BasicEffect.Freeze);
            e.Burn |= applications.HasFlag(BasicEffect.Burn);
            e.Lightning |= applications.HasFlag(BasicEffect.Lightning);
            e.Poison |= applications.HasFlag(BasicEffect.Poison);
            e.Detonate |= applications.HasFlag(BasicEffect.Detonate);
            e.ConsumesStatus |= applications.HasFlag(BasicEffect.Detonate);
            e.Taunt |= applications.HasFlag(BasicEffect.Taunt);
            int bounce = CardRules.AllTargetEvery(c);
            if (bounce > 0) e.Cleave = e.Cleave == 0 ? bounce : Math.Min(e.Cleave, bounce);
            if (t == "") { if (e.Signature() == before) e.Unknown.Add(c.At("id").S()); continue; }
            e.Damage += Read(t, @"\+" + Num + @"% de dano(?! contra| critico| de tus reacciones| recibido)") / 100;
            e.Life += Read(t, @"\+" + Num + @"% de vida") / 100;
            e.Defense += Read(t, @"\+" + Num + @"% de defensa") / 100;
            e.Speed += Read(t, @"\+" + Num + @"% de velocidad de ataque") / 100;
            e.Crit += Read(t, @"\+" + Num + @" puntos de critico") / 100;
            e.CritDamage += Read(t, @"\+" + Num + @"% de dano critico") / 100;
            // Critical damage must not also count as unconditional damage.

            var reaction=Read(t,@"\+"+Num+@"% de dano de tus reacciones");
            e.ReactionDamage+=reaction/100;
            e.Leech += Read(t, @"\+" + Num + @"% de robo de vida") / 100;
            e.Shield += Read(t, @"escudo del " + Num + @"%") / 100;
            e.KillHeal += Read(t, @"matar te cura el " + Num + @"%") / 100;
            e.KillImmune = Math.Max(e.KillImmune,Read(t,@"matar te deja intocable " + Num + @" s"));
            e.Slow = Math.Max(e.Slow, Math.Max(Read(t, @"(?:ralentizan un |ralentizas un |objetivo un )" + Num + @"%"), Read(t, @"objetivo un " + Num + @"% mas lento")) / 100);
            if (t.Contains("lent") || t.Contains("ralent")) e.SlowSeconds = Math.Max(e.SlowSeconds, Read(t, @"(?:durante |lento durante )" + Num + @" s"));
            e.VsSlow += (Read(t, @"\+" + Num + @"% de dano contra enemigos lentos") + Read(t, @"pegas \+" + Num + @"% a lo lento")) / 100;
            e.VsStatus += (Read(t, @"\+" + Num + @"% contra lo que lleve un estado") + Read(t, @"\+" + Num + @"% de dano contra lo que lleve un estado") + Read(t, @"pegas \+" + Num + @"% a lo que lleve un estado")) / 100;
            e.VsElite += Read(t, @"\+" + Num + @"% de dano contra elites y el jefe") / 100;
            var ex = Read(t, @"\+" + Num + @"% de dano contra objetivos por debajo");
            if (ex > 0) { e.Execute += ex / 100; e.ExecuteAt = Math.Max(e.ExecuteAt, Read(t, @"por debajo del " + Num + @"%") / 100); }
            var ramp = Read(t, @"te suma " + Num + @"% de defensa");
            if (ramp > 0) { e.RampDefense += ramp / 100; e.RampCap += Read(t, @"hasta \+" + Num + @"%") / 100; }
            if (t.Contains("defensa sube")) { e.LowAt = Math.Max(e.LowAt, Read(t, @"(?:por debajo del|bajo el) " + Num + @"%") / 100); e.LowDefense += Read(t, @"defensa sube un " + Num + @"%") / 100; }
            e.ConsumesStatus |= t.Contains("consumen el estado");
            bool markSource = t.Contains("golpes marcan") || t.Contains("marcas (+");
            double mark = Read(t, @"\+" + Num + @"%(?: de dano recibido)? por marca") / 100;
            if (markSource) e.Mark = Math.Max(e.Mark, mark);
            else if (t.Contains("marcas mas hondo")) e.MarkDeepen += mark;
            if (markSource || t.Contains("marcas mas hondo")) e.MarkCap = Math.Max(e.MarkCap, (int)Read(t, @"hasta " + Num));
            e.VsMarked += Read(t, @"pegas? \+" + Num + @"% (?:a lo|al) marcado") / 100;
            e.Taunt |= t.Contains("fijan al objetivo");
            if (e.Signature() == before && !Cards.IsCore(c,cfg) || new[]{"fervor","devuelves","limpias","siguiente golpe","por cada","contra elites"}.Any(t.Contains)) e.Unknown.Add(c.At("id").S());
        }
        return e;
    }
    string Signature() => $"{Damage},{Life},{Defense},{Speed},{Crit},{CritDamage},{Leech},{Shield},{KillHeal},{KillImmune},{ReactionDamage},{Slow},{SlowSeconds},{VsSlow},{VsStatus},{VsElite},{Execute},{ExecuteAt},{RampDefense},{RampCap},{LowDefense},{LowAt},{Cleave},{Freeze},{Burn},{Lightning},{Poison},{Taunt},{ConsumesStatus},{Detonate},{Mark},{MarkDeepen},{MarkCap},{VsMarked}";
    public double Outgoing(bool slow, bool status, double hp, bool elite = false) => (1 + Damage) * (1 + (slow ? VsSlow : 0) + (status ? VsStatus : 0) + (elite ? VsElite : 0) + (hp < ExecuteAt ? Execute : 0));
}

static class Cards
{
    internal static bool SkipEarlyShield(JsonNode card, JsonNode? run, Settings cfg) =>
        cfg.SkipShieldCardsEarly && cfg.ShieldSkipFloors > 0 && run != null
        && run.At("profundidad").N() < cfg.ShieldSkipFloors
        && (Effects.Parse([card], cfg).Shield > 0
            || Regex.IsMatch(card.At("descZh").S(), @"护盾|開局盾"));

    public static string Family(JsonNode? c)
    {
        var family = c.At("familia").S(); if (family != "") return family;
        var id = c.At("id").S();
        return id.StartsWith("es") ? "escarcha" : id.StartsWith("br") ? "brasa" : id.StartsWith("fu") ? "fulgor"
            : id.StartsWith("po") ? "ponzona" : id.StartsWith("um") ? "umbra" : id.StartsWith("au") ? "aurora" : id.StartsWith("ac") ? "acero" : "";
    }
    public static bool IsCore(JsonNode? c, Settings cfg)
    {
        var id = c.At("id").S();
        if (id == (string.IsNullOrWhiteSpace(cfg.CoreCardId) ? "es2_carambano" : cfg.CoreCardId.Trim()) || id is "es_carambano" or "es2_carambano" or "es_glaciar") return true;
        if (c == null || Family(c) != "escarcha") return false;
        var effect = (c.At("desc").S() + " " + c.At("descZh").S()).ToLowerInvariant();
        return new[] { "tus autoataques congelan", "tus golpes básicos congelan", "tus autos congelan", "普攻使目标冻结", "普攻会冻结", "普攻冻结敌人" }.Any(effect.Contains);
    }
    public static JsonNode[] Current(JsonNode? run) => run.At("reliquias").A().Concat(run.At("blasones").A()).Concat(run.At("buffs").A()).ToArray();
    public static JsonNode[] Inherited(JsonNode run)
    {
        // Absence from the carry menu does not make a newly acquired relic inherited.
        if (CardRules.ArrivalBaseline(run) is { } arrival) return arrival;
        // The offer list contains this floor's candidates, not all prior relics.
        // Entries absent from it are already inherited. For repeated IDs the
        // server's llevas is the prior stack count, independent of this floor.
        var offered = run.At("guardaPendiente", "cartas").A().GroupBy(c => c.At("id").S()).ToDictionary(g => g.Key, g => g.First());
        var prior = run.At("reliquias").A().Where(c => !offered.ContainsKey(c.At("id").S())).ToList();
        foreach (var c in offered.Values) prior.AddRange(Enumerable.Repeat(c, Math.Clamp((int)c.At("llevas").N(), 0, 3)));
        return prior.ToArray();
    }
    // Acquisition and inheritance use carryable relics as the baseline.
    // A temporary badge cannot replace a core or shield that must survive the floor.
    internal static (int Priority, string Reason) BuildValue(JsonNode c, Settings cfg, JsonNode? run, string method, bool strategyFallback = false)
    {
        if (cfg.StrategyId == 2) return Legacy131.Cards.BuildValue(c, cfg, run, method);
        if (cfg.StrategyId == 3) return ScoreIceStrategy(c, cfg, run, method, strategyFallback);
        if (cfg.StrategyId == 4) return CustomStrategy.Score(c, cfg, run, method);
        var baseline = CardRules.Baseline(run, method);
        var held = Effects.Parse(baseline, cfg);
        var fx = Effects.Parse([c], cfg);
        var skip = strategyFallback ? null : CardRules.SkipReason(c, run, cfg, method);
        if (skip != null) return (-10000, skip);
        bool core = IsCore(c, cfg), missingCore = !baseline.Any(x => IsCore(x, cfg));
        bool inheritedCore = CardRules.HasInheritedCore(run, cfg);
        bool advanced = c.At("id").S() == "es_glaciar" || core && fx.Cleave is > 0 and <= 5;
        int penalty = CardRules.RepeatPenalty(c, baseline, cfg);
        (int, string) Score(int priority, string reason) => (priority - penalty,
            reason + (penalty > 0 ? "；同牌/弹跳重复或独立状态已有，按附带有效增益降权比较，优先补不同增伤" : ""));
        // Collect additional freeze offers until a next-chapter receipt confirms
        // inheritance. Current-floor copies/badges never end this stage and do
        // not incur repeat penalties; this is a strategy, not a probability claim.
        if (core && !inheritedCore) return (advanced ? 2200 : 2000, method == "sima.guarda"
            ? "尚未确认继承：优先带走一份普攻冻结；本章多拿不代表全部继承，临时徽章不能替代"
            : "尚未确认继承：继续优先多拿普攻冻结，不因本章已有冻结而去重或降权；继承概率变化尚未验证");
        // Family accumulation is the normal stage; forced menus may fall back.
        // A newly acquired freeze relic or temporary badge does not end it.
        if (!inheritedCore && Family(c) == "escarcha")
            return Score(1950, "普攻冻结尚未继承，优先只拿冰系积累系列倾向；冰减速也可拿，拿到冻结后仍等章末继承确认");
        if (CardRules.AvoidIceAfterCore(c, run, cfg))
            return Score(-500, "已继承冻结，优先避开冰伤；仅在强制选牌没有其他合法选项时补充，冰减速正常避让，无合适候选才兜底");
        if (!cfg.PreferFreezeBuild) return Score(0, "继承冻结后按模拟收益比较；继承前冰系优先、独立开关和去重正常执行，无合适候选可兜底");
        if (cfg.AvoidFireLightning && fx.ConsumesStatus && !CardRules.BoostDetonator(fx, cfg))
            return Score(-100, "消耗目标状态且未启用点燃搭配，降低优先级");
        if (fx.Cleave > 0 && (held.Cleave == 0 || fx.Cleave < held.Cleave))
            return Score(1950 - Math.Min(50, fx.Cleave), "冻结之后优先新增或更快的普攻全体弹跳；同档优先触发间隔更短，各元素弹跳均可");
        string family = Family(c);
        var applications = CardRules.Applications(c, cfg);
        var heldApplications = baseline.Aggregate(BasicEffect.None, (all, card) => all | CardRules.Applications(card, cfg));
        var newIndependent = applications & ~heldApplications & ~(BasicEffect.Freeze | BasicEffect.Detonate);
        if (inheritedCore && newIndependent != BasicEffect.None)
            return Score(1900, "弹跳之后补尚未持有的普攻独立效果，各拿一次并继承；仍遵守点燃与带电开关");
        if (inheritedCore && CardRules.BoostDetonator(fx, cfg))
            return Score(1850, "独立普攻效果之后优先引爆器，再补火增伤；引爆器已有不重复拿");
        if (family == "brasa" && (fx.Damage > 0 || fx.Speed > 0 || fx.Crit > 0 || fx.CritDamage > 0
            || fx.ReactionDamage > 0 || fx.VsStatus > 0 || fx.Execute > 0 || fx.VsElite > 0))
            return Score(1800, "弹跳、一次独立普攻和引爆器之后优先火系增伤；关闭普攻点燃不屏蔽其他火牌");
        if (fx.VsStatus > 0 || fx.VsSlow > 0 || fx.ReactionDamage > 0)
            return Score(1450, "弹跳与火系增伤之后补其他有效增伤，同档比较模拟收益");
        bool markSource = held.Mark > 0 || method != "sima.guarda" && run.At("heroes").A().Any(h => h.At("marca").B());
        if (fx.Mark > 0) return Score(1400, "补充一次暗影标记来源，为后续增伤服务");
        if ((fx.MarkDeepen > 0 || fx.VsMarked > 0) && markSource)
            return Score(1390, "已有标记来源，补标记增伤");
        if (fx.Damage > 0 || fx.Speed > 0 || fx.Crit > 0 || fx.CritDamage > 0 || fx.Execute > 0 || fx.VsElite > 0)
            return Score(1350, "补攻击、攻速与双暴等输出，重复数值牌降权，避免连续堆同一种牌");
        if (!missingCore && fx.Shield > 0 && held.Shield == 0)
            return Score(1000, "输出牌之后可补首张开局盾，不抢弹跳与增伤名额");
        if (fx.Slow > held.Slow && held.Slow == 0) return Score(700, "冻结与输出未出现时补充控制来源");
        if (fx.Shield > 0 && held.Shield > 0)
            return Score(fx.Shield >= .2 ? 300 : 150, "已有护盾后只少量补防御，优先把选择留给输出");
        return Score(100, "少量防御或其他补充，不抢弹跳与增伤名额");
    }

    static (int Priority, string Reason) ScoreIceStrategy(JsonNode card, Settings cfg, JsonNode? run, string method, bool strategyFallback)
    {
        var baseline = IceStrategyRules.SelectionBaseline(run, method);
        var effects = Effects.Parse([card], cfg);
        var skip = strategyFallback ? null : IceStrategyRules.SkipReason(card, run, cfg, method);
        if (skip != null) return (-10000, skip);
        var preference = IceStrategyRules.Preference(card, effects);
        if (CardRules.SeekingInheritedFreeze(run, cfg))
            return Cards.IsCore(card, cfg)
                ? (2500, "ICE 后期方案：普攻冻结核心尚未继承，保持冻结优先规则")
                : (Cards.Family(card) == "escarcha" ? (1950, "ICE 后期方案：冻结继承前优先冰系牌")
                    : (-5000 + preference.Rank * 30, "ICE 后期方案：冻结核心优先；本菜单兜底按指定效果比较；" + preference.Reason));

        var bounces = baseline.Select(CardRules.AllTargetEvery).Where(x => x is 4 or 5 or 6).ToHashSet();
        foreach (var target in new[] { 4, 5, 6 })
        {
            if (bounces.Contains(target)) continue;
            if (CardRules.AllTargetEvery(card) == target)
            {
                int priority = 2450 - Array.IndexOf(new[] { 4, 5, 6 }, target) * 10;
                return (priority, $"ICE 后期方案：必拿每 {target} 次普攻全体弹跳，已补 {bounces.Count}/3 档");
            }
        }

        int critEvery = IceStrategyRules.GuaranteedCritEvery(card);
        var heldCrit = baseline.Select(IceStrategyRules.GuaranteedCritEvery).ToHashSet();
        if (critEvery is 8 or 10 && !heldCrit.Contains(critEvery))
            return (critEvery == 8 ? 2420 : 2410, $"ICE 后期方案：必拿每 {critEvery} 次攻击必定暴击，分别补齐 8/10 次档");

        var family = Cards.Family(card);
        bool collectible = IceStrategyRules.CollectsRelics(method) && IceStrategyRules.Families.Contains(family);
        int count = IceStrategyRules.CountFamily(baseline, family);
        bool fullWithMissingSeries = collectible && count >= IceStrategyRules.SeriesTarget
            && IceStrategyRules.Families.Any(f => IceStrategyRules.CountFamily(baseline, f) < IceStrategyRules.SeriesTarget);
        if (preference.Rank > 0 && !IceStrategyRules.HasPreference(baseline, preference.Rank, cfg))
            return (fullWithMissingSeries ? 2000 + preference.Rank * 8 : 2300 + preference.Rank * 8,
                (fullWithMissingSeries ? "ICE 后期方案：本系已满8张，缺失指定效果暂让未满系列；" : "ICE 后期方案：补首份指定效果；") + preference.Reason);

        if (collectible)
        {
            if (count < IceStrategyRules.SeriesTarget)
                return (2200 - count * 10 + preference.Rank / 2
                    + (baseline.Any(c => c.At("id").S() == card.At("id").S()) ? 0 : 1),
                    $"ICE 后期方案：{IceStrategyRules.FamilyName(family)}系列{(method == "sima.guarda" ? "已继承" : "本层")} {count}/8，已满足的指定效果不再抢占补系名额");
        }

        if (preference.Rank > 0)
            return (1400 + preference.Rank * 8, "ICE 后期方案：此类指定效果已持有，按当前有效增益补充；" + preference.Reason);

        if (family == "brasa" && (IceStrategyRules.HasElementBonus(card) || IceStrategyRules.HasDamage(effects)))
            return (1350, "ICE 后期方案：按效果补火系增伤，优先级低于缺失关键效果与未满遗物系列");
        if (family == "escarcha" && (effects.VsSlow > 0 || effects.Slow > 0))
            return (1320, "ICE 后期方案：补冰系减速与对减速目标增伤");
        if (IceStrategyRules.HasDamage(effects))
            return (1200, "ICE 后期方案：补充可识别的输出增益");
        if (effects.Shield > 0)
            return (Effects.Parse(baseline, cfg).Shield > 0 ? 900 : 1000, "ICE 后期方案：输出与系列目标后补充护盾");
        return (100, "ICE 后期方案：其他补充效果，仍由实战推演决定");
    }
    public static int Priority(JsonNode c, Settings cfg, JsonNode? run = null, string method = "sima.reliquia") => BuildValue(c, cfg, run, method).Priority;
    public static string Reason(JsonNode c, Settings cfg, JsonNode run, string method) => BuildValue(c, cfg, run, method).Reason;
    public static string? InheritanceError(JsonNode before, JsonNode? next, JsonNode card, Settings cfg)
    {
        if (next == null) return "继承成功回执未包含下一深度状态，已暂停核对。";
        if (next.At("profundidad").N() <= before.At("profundidad").N()) return "继承回执未推进深度，已暂停核对。";
        var expected = Inherited(before).GroupBy(c => c.At("id").S()).ToDictionary(g => g.Key, g => g.Count());
        var selected = card.At("id").S();
        expected[selected] = Math.Min(3, expected.GetValueOrDefault(selected) + 1);
        var actual = next.At("reliquias").A().GroupBy(c => c.At("id").S()).ToDictionary(g => g.Key, g => g.Count());
        foreach (var pair in expected)
            if (actual.GetValueOrDefault(pair.Key) < pair.Value) return $"下一深度缺少预期继承遗物 {pair.Key}（应有{pair.Value}张），已暂停且不会重复派发。";
        if (Inherited(before).Append(card).Any(c => IsCore(c, cfg)) && !next.At("reliquias").A().Any(c => IsCore(c, cfg)))
            return "下一深度遗物栏缺少普攻冻结核心，徽章不能代替它，已暂停且不会重复派发。";
        return null;
    }
    public static JsonNode[] Project(JsonNode run, Choice c)
    {
        var cards = c.Method == "sima.guarda" ? Inherited(run) : Current(run);
        if (c.Card == null) return cards;
        // Badges affect this floor only. Inheritance projects relics only, never badges or buffs.
        if (c.Method == "sima.guarda" && cards.Count(x => x.At("id").S() == c.Card.At("id").S()) >= 3) return cards;
        return cards.Append(c.Card).ToArray();
    }
    public static (bool Allowed, double Value, bool Core) Rate(JsonNode c, Settings cfg)
    {
        // Effects, survival and interactions are evaluated by the simulator.
        // Build priority is separate from combat forecasts; do not invent affinity odds.
        return (CardRules.DisabledStatus(c, cfg) == null, 0, IsCore(c, cfg));
    }
}
