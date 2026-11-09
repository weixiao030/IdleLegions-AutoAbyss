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
    public bool Freeze, Burn, Lightning, Poison, Taunt, ConsumesStatus;
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
            var t = Normalize(c.At("desc").S()).ToLowerInvariant();
            e.Freeze |= Cards.IsCore(c, cfg);
            if (t == "") { e.Unknown.Add(c.At("id").S()); continue; }
            var before = e.Signature();
            e.Damage += Read(t, @"\+" + Num + @"% de dano(?! contra| critico| de tus reacciones)") / 100;
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
            int cleave = (int)Read(t, @"cada " + Num + @" (?:autoataques|tu golpe|saltan)");
            if (cleave > 0 && (t.Contains("salta a todos") || t.Contains("saltan a todos"))) e.Cleave = e.Cleave == 0 ? cleave : Math.Min(e.Cleave, cleave);
            e.Burn |= Regex.IsMatch(t, @"(?:autoataques|autos|golpes).*(?:queman|encienden|ardiendo|arden)");
            e.Lightning |= Regex.IsMatch(t, @"(?:autoataques|autos|golpes).*(?:electrocutan|electrifican)");
            e.Poison |= Regex.IsMatch(t, @"(?:autoataques|autos|golpes).*envenenan");
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
    string Signature() => $"{Damage},{Life},{Defense},{Speed},{Crit},{CritDamage},{Leech},{Shield},{KillHeal},{KillImmune},{ReactionDamage},{Slow},{SlowSeconds},{VsSlow},{VsStatus},{VsElite},{Execute},{ExecuteAt},{RampDefense},{RampCap},{LowDefense},{LowAt},{Cleave},{Freeze},{Burn},{Lightning},{Poison},{Taunt},{ConsumesStatus},{Mark},{MarkDeepen},{MarkCap},{VsMarked}";
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
    public static bool Forbidden(JsonNode c)
    {
        // IDs remain effective when the server omits descriptions. Match effects
        // too, so future cards and badges cannot bypass the fixed build rule.
        if (c.At("id").S() is "br_yesca" or "br_incendio" or "fu_descarga" or "fu_tormenta") return true;
        var text = Effects.Normalize(c.At("desc").S() + " " + c.At("descZh").S() + " " + c.At("description").S()).ToLowerInvariant();
        return Regex.IsMatch(text, @"(?:autoataques|autos|golpes).{0,100}(?:queman|encienden|ardiendo|arden|electrocutan|electrifican)")
            || Regex.IsMatch(text, @"(?:basic attacks?|auto[ -]?attacks?)\s+(?:(?:will|now|also|can)\s+)*(?:ignite|electrif\w*|shock\b|(?:apply|inflict|cause)\w* (?:burn|shock|electri)|leave.{0,30}burning)")
            || Regex.IsMatch(text, @"(?:普攻|普通攻击|基础攻击).{0,30}(?:点燃|燃烧|灼烧|带电|触电|通电)");
    }
    public static JsonNode[] Inherited(JsonNode run)
    {
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
    internal static (int Priority, string Reason) BuildValue(JsonNode c, Settings cfg, JsonNode? run, string method)
    {
        var baseline = method == "sima.guarda" && run != null ? Inherited(run)
            : method is "sima.reliquia" or "sima.trade" ? run.At("reliquias").A().ToArray() : Current(run);
        var held = Effects.Parse(baseline, cfg);
        var fx = Effects.Parse([c], cfg);
        bool core = IsCore(c, cfg), missingCore = !baseline.Any(x => IsCore(x, cfg));
        bool advanced = c.At("id").S() == "es_glaciar" || core && fx.Cleave is > 0 and <= 5;
        if (missingCore && core) return (advanced ? 2200 : 2000, method == "sima.guarda"
            ? "先保留普攻冻结核心，保证下一层仍能冻结；传说冻结加五次弹跳最优"
            : "先拿可继承的普攻冻结核心；传说冻结加五次弹跳最优，本层徽章不替代它");
        if (!cfg.PreferFreezeBuild) return (0, "按模拟收益比较");
        // Only actual state-consuming attacks conflict with freeze. Family labels
        // do not disqualify fire status damage or lightning cleave.
        if (cfg.AvoidFireLightning && fx.ConsumesStatus)
            return (-100, "明确消耗目标状态的效果降低优先级；普攻点燃与带电始终禁止");
        if (advanced && !baseline.Any(x => x.At("id").S() == "es_glaciar" || IsCore(x, cfg) && Effects.Parse([x], cfg).Cleave is > 0 and <= 5))
            return (2100, "升级为传说普攻冻结加每五次攻击弹跳");
        string family = Family(c);
        if (missingCore && family == "escarcha") return (1000, "缺可继承冻结核心：先积累寒冷，优先减速、减速增伤和弹跳");
        if (!missingCore && fx.Shield > 0 && held.Shield == 0)
            return (1600, "已有冻结核心：先补开局护盾并继承，防止进房间时暴毙");
        // Secure one opening shield, then build damage. Repeated shields must
        // not outrank every useful attack or bounce for the rest of the run.
        if (fx.Cleave > 0 && (held.Cleave == 0 || fx.Cleave < held.Cleave))
            return (1550, "优先补充或加快普攻弹跳，冰毒电火均可；不选普攻点燃或带电");
        if (fx.VsSlow > 0 && (!missingCore || held.Slow > 0))
            return (1450, "优先减速目标增伤，冻结也能满足模型中的减速条件");
        if (fx.VsStatus > 0)
            return (1430, "优先异常状态增伤，提高冻结期间的输出");
        if (fx.Slow > held.Slow && held.Slow == 0)
            return (1400, "补一个减速来源，已有减速后不重复抢攻击名额");
        bool markSource = held.Mark > 0 || method != "sima.guarda" && run.At("heroes").A().Any(h => h.At("marca").B());
        if (fx.Mark > 0) return (1350, "补充暗影标记，让后续攻击受益");
        if ((fx.MarkDeepen > 0 || fx.VsMarked > 0) && markSource)
            return (1340, "已有标记来源，优先标记增伤");
        if (fx.MarkDeepen > 0 || fx.VsMarked > 0) return (100, "缺少标记来源，标记专用强化暂不抢输出名额");
        if (fx.Damage > 0 || fx.Speed > 0 || fx.Crit > 0 || fx.CritDamage > 0 || fx.Execute > 0 || fx.VsElite > 0)
            return (1300, "优先攻击、攻速和双暴等输出强化，同档比较模拟收益");
        if (fx.Slow > held.Slow) return (900, "补强已有减速，优先级低于有效输出强化");
        if (fx.Shield > 0 && held.Shield > 0)
            return (fx.Shield >= .2 ? 450 : 300, "后续开局盾作为补充，优先把名额留给攻击和弹跳");
        if (core) return (-10, "已有可继承冻结核心，无需重复叠冻结；优先其他有效强化");
        // Family alone is not an offensive effect. Pure ice/poison defense
        // receives no blanket bonus once the core search stage has ended.
        return (100, "防御或其他补充效果，同级比较模拟收益");
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
        // Badge choices are permanent alternatives, not another copy of the offer table.
        return cards.Append(c.Card).ToArray();
    }
    public static (bool Allowed, double Value, bool Core) Rate(JsonNode c, Settings cfg)
    {
        // Effects, survival and interactions are evaluated by the simulator.
        // Build priority is separate from combat forecasts; do not invent affinity odds.
        return (!Forbidden(c), 0, IsCore(c, cfg));
    }
}
