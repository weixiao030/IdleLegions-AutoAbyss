using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FrostAbyss;

[Flags]
enum BasicEffect { None = 0, Freeze = 1, Burn = 2, Lightning = 4, Poison = 8, Detonate = 16, Mark = 32, Taunt = 64 }

// Ice-first acquisition lasts until freeze inheritance is confirmed.
// After that stage, independent switches apply to effects, not entire families.
// Known IDs come from saved game receipts; descriptions cover future variants.
static class CardRules
{
    static string Text(JsonNode c) => Effects.Normalize(string.Join(" ",
        c.At("desc").S(), c.At("descZh").S(), c.At("description").S())).ToLowerInvariant();

    static bool Applies(string text, string spanish, string english, string chinese) =>
        Regex.IsMatch(text, @"(?:tus\s+)?(?:autoataques|autos|golpes(?: basicos)?)\s+(?:(?:tambien|ahora)\s+)*(?:" + spanish + @")")
        || Regex.IsMatch(text, @"(?:basic attacks?|auto[ -]?attacks?)\s+(?:(?:will|now|also|can)\s+)*(?:" + english + @")")
        || Regex.IsMatch(text, @"(?:普攻|普通攻击|基础攻击)(?:会|可|将|使|令|让|对|敌人|目标|施加|造成|附加|进入|获得|攻击|有|概率|\s|[0-9%.]){0,20}(?:" + chinese + @")");

    internal static BasicEffect Applications(JsonNode c, Settings cfg)
    {
        var id = c.At("id").S(); var text = Text(c);
        BasicEffect result = Cards.IsCore(c, cfg) ? BasicEffect.Freeze : BasicEffect.None;
        if (id is "br_yesca" or "br_incendio" or "br_pira"
            || Applies(text, @"arden\b|queman\b|encienden\b|dejan.{0,30}ardiendo", @"ignite\b|burn\b|(?:apply|inflict|cause)\w* (?:burn|ignit)|leave.{0,30}burning", "点燃|燃烧|灼烧")) result |= BasicEffect.Burn;
        if (id is "fu_descarga" or "fu_tormenta"
            || Applies(text, @"electrocutan\b|electrifican\b|dejan.{0,30}electrificad", @"electrif\w*|shock\b|(?:apply|inflict|cause)\w* (?:shock|electri)", "带电|触电|通电")) result |= BasicEffect.Lightning;
        if (Applies(text, "envenenan", @"poison\b|(?:apply|inflict)\w* poison", "中毒|毒素")) result |= BasicEffect.Poison;
        if (id == "br_detonador" || Applies(text, "consumen el estado", @"consume\w* (?:the )?(?:target.?s? )?status", "消耗状态|引爆")) result |= BasicEffect.Detonate;
        if (Applies(text, "marcan", @"mark\b|apply marks?", "标记")) result |= BasicEffect.Mark;
        if (Applies(text, "fijan al objetivo", @"taunt\b", "嘲讽")) result |= BasicEffect.Taunt;
        return result;
    }

    internal static int AllTargetEvery(JsonNode c)
    {
        if (c.At("id").S() == "es_glaciar") return 5;
        var text = Text(c);
        var m = Regex.Match(text, @"cada\s+(\d+)\s+(?:autoataques|ataques|tu golpe|saltan)");
        if (m.Success && !Regex.IsMatch(text, @"saltan? a todos")) m = Match.Empty;
        if (!m.Success) m = Regex.Match(text, @"every\s+(\d+)\s+(?:basic |auto[ -]?)?attacks?.{0,100}(?:all enemies|all targets)");
        if (!m.Success) m = Regex.Match(text, @"每\s*(\d+)\s*次.{0,50}(?:全体|所有敌人|所有目标)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var count) ? count : 0;
    }

    internal static JsonNode[] Baseline(JsonNode? run, string method) =>
        method == "sima.guarda" && run != null ? Cards.Inherited(run)
        : method is "sima.reliquia" or "sima.trade" ? run.At("reliquias").A().ToArray() : Cards.Current(run);

    internal static JsonNode[] InheritedBaseline(JsonNode? run)
    {
        if (run == null) return [];
        if (ArrivalBaseline(run) is { } arrival) return arrival;
        if (run.At("guardaPendiente", "cartas").A().Any()) return Cards.Inherited(run);
        return run.At("reliquias").A().ToArray();
    }

    internal static JsonNode[]? ArrivalBaseline(JsonNode run)
    {
        var relics = run.At("reliquias").A().ToArray();
        // In saved receipts, llegada.reliquias is the number of relics present
        // on arrival, before this floor appends its new acquisitions.
        if (run is JsonObject obj && obj.ContainsKey("llegada"))
            return relics.Take(Math.Clamp((int)run.At("llegada", "reliquias").N(), 0, relics.Length)).ToArray();
        return null; // Older snapshots without arrival metadata.
    }

    internal static bool HasInheritedCore(JsonNode? run, Settings cfg) => InheritedBaseline(run).Any(c => Cards.IsCore(c, cfg));

    internal static bool SeekingInheritedFreeze(JsonNode? run, Settings cfg) => !HasInheritedCore(run, cfg);

    internal static bool CollectingFreeze(JsonNode card, JsonNode? run, Settings cfg) =>
        SeekingInheritedFreeze(run, cfg) && Cards.IsCore(card, cfg);

    internal static bool AvoidIceAfterCore(JsonNode card, JsonNode? run, Settings cfg)
    {
        if (!HasInheritedCore(run, cfg) || Cards.Family(card) != "escarcha" || AllTargetEvery(card) > 0) return false;
        var fx = Effects.Parse([card], cfg);
        if (Cards.IsCore(card, cfg) && HasSupplementalValue(card, cfg)) return false;
        // Feedback permits occasional ice shields; do not ban the whole family.
        return !(fx.Shield > 0 && fx.Slow == 0 && fx.VsSlow == 0);
    }

    internal static string? DisabledStatus(JsonNode card, Settings cfg)
    {
        var effect = Applications(card, cfg);
        if (!cfg.AllowBasicBurn && effect.HasFlag(BasicEffect.Burn)) return "普攻点燃已关闭；其他火系效果照常比较";
        if (!cfg.AllowBasicLightning && effect.HasFlag(BasicEffect.Lightning)) return "普攻带电已关闭；其他电系效果照常比较";
        return null;
    }

    // An independent status is boolean, but damage/shield/etc. printed on the
    // same card can still add value. Never discard the whole mixed card merely
    // because its burn/freeze/shock already exists.
    internal static bool HasSupplementalValue(JsonNode card, Settings cfg)
    {
        var f = Effects.Parse([card], cfg);
        return f.Damage > 0 || f.Life > 0 || f.Defense > 0 || f.Speed > 0 || f.Crit > 0 || f.CritDamage > 0
            || f.Leech > 0 || f.Shield > 0 || f.KillHeal > 0 || f.KillImmune > 0 || f.ReactionDamage > 0
            || f.VsSlow > 0 || f.VsStatus > 0 || f.VsElite > 0 || f.Execute > 0 || f.RampDefense > 0
            || f.LowDefense > 0 || f.MarkDeepen > 0 || f.VsMarked > 0;
    }

    internal static string? SkipReason(JsonNode card, JsonNode? run, Settings cfg, string method)
    {
        if (SeekingInheritedFreeze(run, cfg) && Cards.Family(card) != "escarcha")
            return "普攻冻结尚未继承，当前阶段优先只选冰系牌（含冰减速），避让其他系列";
        var disabled = DisabledStatus(card, cfg);
        if (disabled != null) return disabled;
        if (HasInheritedCore(run, cfg) && Cards.Family(card) == "escarcha"
            && (Effects.Parse([card], cfg).Slow > 0 || Regex.IsMatch(card.At("descZh").S(), @"(?:普攻|攻击|命中|使目标|造成).{0,16}减速")))
            return "普攻冻结已继承，优先不再选择冰减速";
        if (Cards.SkipEarlyShield(card, run, cfg)) return $"前{cfg.ShieldSkipFloors}层优先不选护盾";
        var baseline = Baseline(run, method);
        var effect = Applications(card, cfg);
        int bounce = AllTargetEvery(card);
        var held = baseline.Aggregate(BasicEffect.None, (value, c) => value | Applications(c, cfg));
        if (effect.HasFlag(BasicEffect.Detonate) && held.HasFlag(BasicEffect.Detonate))
            return "引爆器已经持有或继承，只需一份，不再重复选择或占用继承名额";
        if (effect != BasicEffect.None && (effect & ~held) == BasicEffect.None && bounce == 0 && !HasSupplementalValue(card, cfg) && !CollectingFreeze(card, run, cfg))
            return "同类普攻独立效果已经持有，且无已识别的附带增益，优先不重复选择或继承";
        var id = card.At("id").S();
        if (!CollectingFreeze(card, run, cfg) && id != "" && (baseline.Count(c => c.At("id").S() == id) >= 3
            || method == "sima.guarda" && card.At("llevas").N() >= 3))
            return "同种牌已达三层继承上限，继续拿不会增加效果";
        return null;
    }

    internal static int RepeatPenalty(JsonNode card, JsonNode[] baseline, Settings cfg)
    {
        var id = card.At("id").S();
        int copies = id == "" ? 0 : baseline.Count(c => c.At("id").S() == id);
        int bounce = AllTargetEvery(card), heldBounce = Effects.Parse(baseline, cfg).Cleave;
        var held = baseline.Aggregate(BasicEffect.None, (all, c) => all | Applications(c, cfg));
        bool addsIndependent = (Applications(card, cfg) & ~held) != BasicEffect.None;
        var applications = Applications(card, cfg);
        bool repeatedStatusWithValue = applications != BasicEffect.None && !addsIndependent && bounce == 0 && HasSupplementalValue(card, cfg);
        return copies * 350 + (!addsIndependent && bounce > 0 && heldBounce > 0 && bounce >= heldBounce ? 175 : 0)
            + (repeatedStatusWithValue ? 600 : 0);
    }

    // Only game-declared unavailability is never relaxed. Strategy filters
    // are preferences when a forced menu has no normal candidate.
    internal static bool Available(JsonNode card) => card.At("id").S() != ""
        && !(card.At("disponible") is JsonValue d && d.TryGetValue<bool>(out var yes) && !yes)
        && !(card.At("available") is JsonValue a && a.TryGetValue<bool>(out var ok) && !ok)
        && !card.At("disabled").B();

    internal static int FallbackPenalty(JsonNode card, JsonNode run, Settings cfg, string method)
    {
        var baseline = Baseline(run, method);
        var effect = Applications(card, cfg);
        var held = baseline.Aggregate(BasicEffect.None, (all, c) => all | Applications(c, cfg));
        int penalty = SeekingInheritedFreeze(run, cfg) && Cards.Family(card) != "escarcha" ? 6000 : 0;
        if (DisabledStatus(card, cfg) != null) penalty += 4000;
        if (Cards.SkipEarlyShield(card, run, cfg)) penalty += 1000;
        if (HasInheritedCore(run, cfg) && Cards.Family(card) == "escarcha" && Effects.Parse([card], cfg).Slow > 0) penalty += 1000;
        if (effect.HasFlag(BasicEffect.Detonate) && held.HasFlag(BasicEffect.Detonate)
            || effect != BasicEffect.None && (effect & ~held) == BasicEffect.None && AllTargetEvery(card) == 0 && !HasSupplementalValue(card, cfg) && !CollectingFreeze(card, run, cfg)) penalty += 2500;
        var id = card.At("id").S();
        if (!CollectingFreeze(card, run, cfg) && (baseline.Count(c => c.At("id").S() == id) >= 3 || method == "sima.guarda" && card.At("llevas").N() >= 3)) penalty += 3500;
        return penalty;
    }

    internal static bool BoostDetonator(Effects fx, Settings cfg) => cfg.AllowBasicBurn && fx.Detonate;
}
