using System.Text.Json.Nodes;

namespace FrostAbyss;

static class DamageBreakdown
{
    internal static bool Flag(JsonNode e, string key) => e.At(key).B() || e.At(key).N() == 1;
    // `esc`/`sesc` are shield snapshots; `relc` is a relic counter marker.
    // Neither is damage and neither can be added to the damage total.
    internal static string Kind(JsonNode e) => e.At("reaction").S() != "" ? "reaction" : e.At("rel").S() != "" ? "relic" : e.At("skill").S() != "" ? "skill" : "other";
    internal static string Id(JsonNode e) => Kind(e) switch
    {
        "reaction" => e.At("reaction").S(), "relic" => e.At("rel").S(), "skill" => e.At("skill").S(),
        _ => Flag(e, "canal") ? "channel-unmarked" : Flag(e, "tick") ? "periodic-unmarked" : "unmarked"
    };
    internal static object[] Details(JsonNode[] events, Func<string, string, string> name, Func<string, string, string>? describe = null, string skillBasis = "catalog")
        => events.GroupBy(e => (Kind: Kind(e), Id: Id(e))).Select(g =>
        {
            var hits = g.ToArray();
            return (object)new
            {
                kind = g.Key.Kind, id = g.Key.Id, name = name(g.Key.Kind, g.Key.Id),
                description = describe?.Invoke(g.Key.Kind, g.Key.Id) ?? GameText.Description(g.Key.Kind, g.Key.Id),
                descriptionBasis = g.Key.Kind == "skill" ? skillBasis : "catalog",
                damage = hits.Sum(e => e.At("dmg").N()), events = hits.Length,
                criticalEvents = hits.Count(e => e.At("crit").B()),
                criticalDamage = hits.Where(e => e.At("crit").B()).Sum(e => e.At("dmg").N()),
                minHit = hits.Min(e => e.At("dmg").N()), maxHit = hits.Max(e => e.At("dmg").N()),
                firstHit = hits.Min(e => e.At("t").N()), lastHit = hits.Max(e => e.At("t").N()),
                periodicEvents = hits.Count(e => Flag(e, "tick")),
                periodicDamage = hits.Where(e => Flag(e, "tick")).Sum(e => e.At("dmg").N()),
                channelEvents = hits.Count(e => Flag(e, "canal")),
                channelDamage = hits.Where(e => Flag(e, "canal")).Sum(e => e.At("dmg").N()),
                timings = hits.GroupBy(Timing).Select(t => new { id = t.Key, name = TimingName(t.Key), damage = t.Sum(e => e.At("dmg").N()), events = t.Count() }).ToArray(),
                states = StateTags(hits),
                kills = hits.Count(e => e.At("kill").B()),
                targets = hits.GroupBy(e => e.At("dst").S()).Select(t => new { id = t.Key, name = GameText.Text(t.Key), damage = t.Sum(e => e.At("dmg").N()), events = t.Count(), criticalEvents = t.Count(e => e.At("crit").B()), kills = t.Count(e => e.At("kill").B()) }).OrderByDescending(t => t.damage).ToArray(),
                // Explicit element only. burnEl is target state, not this hit's element.
                elements = hits.GroupBy(e => e.At("element").S()).Select(t => new { id = t.Key, name = GameText.Element(t.Key), damage = t.Sum(e => e.At("dmg").N()), events = t.Count() }).OrderByDescending(t => t.damage).ToArray(),
                tags = new[] { "skill", "reaction", "rel" }.SelectMany(key => hits.Where(e => e.At(key).S() != "").GroupBy(e => e.At(key).S()).Select(t => new { kind = key == "rel" ? "relic" : key, id = t.Key, name = name(key == "rel" ? "relic" : key, t.Key), damage = t.Sum(e => e.At("dmg").N()), events = t.Count() })).ToArray()
            };
        }).ToArray();

    // The replay distinguishes explicit canal ticks from other tick events.
    // A burn flag is a state snapshot, never proof that the damage is a burn tick.
    internal static string Timing(JsonNode e) => Flag(e, "canal") ? "channel" : Flag(e, "tick") ? "periodic" : "direct";
    internal static string TimingName(string id) => id switch { "channel" => "引导标记", "periodic" => "周期标记（未细分）", _ => "非周期标记" };
    static object[] StateTags(JsonNode[] hits) => new[] { "burn", "slow", "venom", "marks", "brkStun" }
        .Select(key => new { Key = key, Hits = hits.Where(e => Flag(e,key) || e.At(key).N() > 0).ToArray() })
        .Where(g => g.Hits.Length > 0).Select(g => (object)new {
            id = g.Key, name = g.Key switch { "burn" => "燃烧", "slow" => "减速", "venom" => "中毒", "marks" => "印记", _ => "破防眩晕" },
            damage = g.Hits.Sum(e => e.At("dmg").N()), events = g.Hits.Length
        }).ToArray();

    internal static object[] Skills(JsonNode[] sourced, Func<string, string, string> name, Func<string, string, string>? describe = null) => sourced
        .Where(e => e.At("side").S() == "ally" && e.At("skill").S() != "")
        .GroupBy(e => e.At("skill").S()).Select(g => (object)new
        {
            id = g.Key, name = name("skill", g.Key), description = describe?.Invoke("skill", g.Key) ?? GameText.Description("skill", g.Key),
            castMarkers = g.Count(e => e.At("castOnly").B()),
            damage = g.Sum(e => Math.Max(0, e.At("dmg").N())),
            events = g.Count(e => e.At("dmg").N() > 0),
            healing = g.Sum(e => Math.Max(0, -e.At("dmg").N())),
            reactionDamage = g.Where(e => e.At("reaction").S() != "").Sum(e => Math.Max(0, e.At("dmg").N())),
            relicDamage = g.Where(e => e.At("reaction").S() == "" && e.At("rel").S() != "").Sum(e => Math.Max(0, e.At("dmg").N()))
        }).ToArray();

    internal static object[] LargestHits(JsonNode[] hits, Func<string, string, string> name) => hits
        .OrderByDescending(e => e.At("dmg").N()).Take(20).Select(e => (object)new
        {
            time = e.At("t").N(), damage = e.At("dmg").N(), critical = e.At("crit").B(),
            target = GameText.Text(e.At("dst").S()), kind = Kind(e), id = Id(e), name = name(Kind(e), Id(e)),
            skill = e.At("skill").S(), reaction = e.At("reaction").S(), relic = e.At("rel").S(),
            element = GameText.Element(e.At("element").S()), periodic = Flag(e, "tick"), channel = Flag(e, "canal"), timing = TimingName(Timing(e)),
            kill = e.At("kill").B(), targetHpPct = e.At("hpPct")?.DeepClone()
        }).ToArray();
}
