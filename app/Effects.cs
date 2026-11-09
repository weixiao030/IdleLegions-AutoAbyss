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
    public int Cleave;
    public bool Freeze, Burn, Lightning, Poison, Taunt;
    public List<string> Unknown = [];
    static string Normalize(string s) => new(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
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
            if (t == "") { e.Unknown.Add(c.At("id").S()); continue; }
            var before = e.Signature();
            e.Freeze |= Cards.IsCore(c, cfg);
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
            e.Shield += Read(t, @"escudo del " + Num + @"% de tu vida") / 100;
            e.KillHeal += Read(t, @"matar te cura el " + Num + @"%") / 100;
            e.KillImmune = Math.Max(e.KillImmune,Read(t,@"matar te deja intocable " + Num + @" s"));
            e.Slow = Math.Max(e.Slow, Read(t, @"(?:ralentizan un |ralentizas un |objetivo un )" + Num + @"%") / 100);
            if (t.Contains("lent") || t.Contains("ralent")) e.SlowSeconds = Math.Max(e.SlowSeconds, Read(t, @"(?:durante |lento durante )" + Num + @" s"));
            e.VsSlow += (Read(t, @"\+" + Num + @"% de dano contra enemigos lentos") + Read(t, @"pegas \+" + Num + @"% a lo lento")) / 100;
            e.VsStatus += (Read(t, @"\+" + Num + @"% contra lo que lleve un estado") + Read(t, @"\+" + Num + @"% de dano contra lo que lleve un estado") + Read(t, @"pegas \+" + Num + @"% a lo que lleve un estado")) / 100;
            e.VsElite += Read(t, @"\+" + Num + @"% de dano contra elites y el jefe") / 100;
            var ex = Read(t, @"\+" + Num + @"% de dano contra objetivos por debajo");
            if (ex > 0) { e.Execute += ex / 100; e.ExecuteAt = Math.Max(e.ExecuteAt, Read(t, @"por debajo del " + Num + @"%") / 100); }
            var ramp = Read(t, @"te suma " + Num + @"% de defensa");
            if (ramp > 0) { e.RampDefense += ramp / 100; e.RampCap += Read(t, @"hasta \+" + Num + @"%") / 100; }
            if (t.Contains("defensa sube")) { e.LowAt = Math.Max(e.LowAt, Read(t, @"(?:por debajo del|bajo el) " + Num + @"%") / 100); e.LowDefense += Read(t, @"defensa sube un " + Num + @"%") / 100; }
            int cleave = (int)(Read(t, @"cada " + Num + @" autoataques") + Read(t, @"cada " + Num + @" tu golpe salta"));
            if (cleave > 0 && t.Contains("salta a todos")) e.Cleave = e.Cleave == 0 ? cleave : Math.Min(e.Cleave, cleave);
            e.Burn |= Regex.IsMatch(t, @"(?:autoataques|autos|golpes).*(?:queman|encienden)");
            e.Lightning |= Regex.IsMatch(t, @"(?:autoataques|autos|golpes).*(?:electrocutan|electrifican)");
            e.Poison |= Regex.IsMatch(t, @"(?:autoataques|autos|golpes).*envenenan");
            e.Taunt |= t.Contains("fijan al objetivo");
            if (e.Signature() == before && !Cards.IsCore(c,cfg) || new[]{"fervor","devuelves","limpias","siguiente golpe","por cada","contra elites"}.Any(t.Contains)) e.Unknown.Add(c.At("id").S());
        }
        return e;
    }
    string Signature() => $"{Damage},{Life},{Defense},{Speed},{Crit},{CritDamage},{Leech},{Shield},{KillHeal},{KillImmune},{ReactionDamage},{Slow},{SlowSeconds},{VsSlow},{VsStatus},{VsElite},{Execute},{ExecuteAt},{RampDefense},{RampCap},{LowDefense},{LowAt},{Cleave},{Freeze},{Burn},{Lightning},{Poison},{Taunt}";
    public double Outgoing(bool slow, bool status, double hp, bool elite = false) => (1 + Damage) * (1 + (slow ? VsSlow : 0) + (status ? VsStatus : 0) + (elite ? VsElite : 0) + (hp < ExecuteAt ? Execute : 0));
}

static class Cards
{
    public static string Family(JsonNode? c)
    {
        var family = c.At("familia").S(); if (family != "") return family;
        var id = c.At("id").S();
        return id.StartsWith("es") ? "escarcha" : id.StartsWith("br") ? "brasa" : id.StartsWith("fu") ? "fulgor" : "";
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
        // The offer list contains this floor's candidates, not all prior relics.
        // Entries absent from it are already inherited. For repeated IDs the
        // server's llevas is the prior stack count, independent of this floor.
        var offered = run.At("guardaPendiente", "cartas").A().GroupBy(c => c.At("id").S()).ToDictionary(g => g.Key, g => g.First());
        var prior = run.At("reliquias").A().Where(c => !offered.ContainsKey(c.At("id").S())).ToList();
        foreach (var c in offered.Values) prior.AddRange(Enumerable.Repeat(c, Math.Clamp((int)c.At("llevas").N(), 0, 3)));
        return prior.ToArray();
    }
    public static int Priority(JsonNode c, Settings cfg, JsonNode? run = null, string method = "sima.reliquia")
    {
        var baseline = method == "sima.guarda" && run != null ? Inherited(run)
            : method is "sima.reliquia" or "sima.trade" ? run.At("reliquias").A().ToArray() : Current(run);
        bool missingCore = !baseline.Any(x => IsCore(x, cfg));
        if (IsCore(c, cfg) && missingCore) return 2000;
        if (!cfg.PreferFreezeBuild) return 0;
        string family = Family(c);
        if (cfg.AvoidFireLightning && family is "brasa" or "fulgor") return -100;
        if (missingCore && family == "escarcha") return 1000;
        var fx = Effects.Parse([c], cfg);
        var desc = c.At("desc").S().ToLowerInvariant() + c.At("descZh").S();
        if (fx.Crit > 0 || fx.CritDamage > 0 || desc.Contains("hace crítico") || desc.Contains("hace critico") || desc.Contains("暴击") || desc.Contains("暴伤")) return 800;
        bool damage = fx.Damage > 0 || fx.VsSlow > 0 || fx.VsStatus > 0 || fx.VsElite > 0 || fx.Execute > 0;
        if (damage && fx.Cleave > 0) return 600;
        if (fx.Cleave > 0) return 500;
        if (damage) return 400;
        return family == "escarcha" ? 200 : 100;
    }
    public static string Reason(JsonNode c, Settings cfg, JsonNode run, string method) => Priority(c, cfg, run, method) switch
    {
        2000 => method == "sima.guarda" ? "下一层缺冻结核心：优先占用唯一继承名额" : method == "sima.reliquia" ? "优先取得可继承的普攻冻结牌，本层徽章不替代它" : "优先取得缺失的普攻冻结核心",
        1000 => "缺少可保留的冻结核心：优先积累寒冷系列",
        800 => "冻结基础已具备：优先暴击与暴击伤害",
        600 => "伤害与弹跳组合",
        500 => "弹跳或群体普攻",
        400 => "伤害加成",
        200 => "其他寒冷效果，同级比较模拟收益",
        -100 => "火雷系列回避，只有缺少其他选择时使用",
        _ => "同级比较模拟收益"
    };
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
        return (true, 0, IsCore(c, cfg));
    }
}
