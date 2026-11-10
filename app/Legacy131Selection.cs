using System.Text.Json.Nodes;
using FrostAbyss;
namespace FrostAbyss.Legacy131;
static class Selection
{
    internal static Choice[] CardChoices(JsonNode[] cards, string method, JsonNode run, Settings Config)
    {
        bool skippedShield = cards.Any(c => Cards.Rate(c, Config).Allowed && Cards.SkipEarlyShield(c, run, Config));
        cards = cards.Where(c => Cards.Rate(c, Config).Allowed && !Cards.SkipEarlyShield(c, run, Config)).ToArray();
        // A fully inherited stack grants no additional effect. Prefer any card
        // that can still add a stack; keep a legal fallback when all are capped.
        if (method == "sima.guarda" && cards.Any(c => c.At("llevas").N() < 3))
            cards = cards.Where(c => c.At("llevas").N() < 3).ToArray();
        var action = method == "sima.guarda" ? "带走遗物：" : method == "sima.blason" ? "选择徽章：" : "选择遗物：";
        var choices = cards.Select(c => (Card: c, Rating: Cards.Rate(c, Config))).Where(x => x.Rating.Allowed).Select(x => new Choice((x.Rating.Core ? "普攻冻结核心：" : action) + J.Name(x.Card), method, new() { ["id"] = x.Card.At("id").S() }, x.Rating.Value, x.Rating.Value < 0 ? 1 : 0, x.Rating.Core, Cards.Priority(x.Card, Config, run, method), x.Card) { Reason = Cards.Reason(x.Card, Config, run, method) }).ToArray();
        if (choices.Length == 0) throw new Exception(skippedShield
            ? "当前选项只剩前几层暂不选择的护盾或禁止的普攻点燃/带电牌，已暂停；请手动处理或关闭前期护盾避让后继续。"
            : "当前选项全部是禁止的普攻点燃/带电牌，已暂停，不会自动选择；请在游戏中处理后继续。");
        return choices;
    }
internal static Choice[] BeforeShopExit(Choice[] candidates, Forecast? baseline)
    {
        var buys = candidates.Where(c => c.Method == "sima.trade" && (c.Card == null || c.Priority > 0 || baseline == null ||
            (c.Forecast?.Score ?? 0) >= baseline.Score - .0001)).ToArray();
        return buys.Length > 0 ? buys : candidates.Where(c => c.Method != "sima.trade").ToArray();
    }
    internal static Choice[] FightChoices(JsonNode s, JsonNode run, string id)
    {
        var enemies = s.At("peeks", id, "enemigos").A().ToArray();
        // The live run is authoritative after revival. A cached preview can
        // still omit a revived hero or contain one who has since fallen.
        var members = run.At("heroes").A().ToArray();
        var heroes = members.Length > 0 ? members.Where(x => !x.At("caido").B()).ToArray()
            : s.At("peeks", id, "legion").A().ToArray();
        if (enemies.Length == 0) return [new("战斗：默认目标", "sima.fight", new() { ["objetivos"] = new JsonObject(), ["desafio"] = false })];
        var result = new List<Choice>();
        void Enumerate(int i, JsonObject obj, List<string> labels)
        {
            if (i >= heroes.Length) { result.Add(new("战斗：" + string.Join("；", labels), "sima.fight", new() { ["objetivos"] = obj.DeepClone(), ["desafio"] = false })); return; }
            foreach (var e in enemies) { var name = heroes[i].At("name").S(heroes[i].At("nombre").S()); obj[name] = e.At("name").S(); labels.Add(name + " → " + e.At("name").S()); Enumerate(i + 1, obj, labels); labels.RemoveAt(labels.Count - 1); }
        }
        if (Math.Pow(enemies.Length, heroes.Length) > 4096) throw new Exception("目标组合超过 4096 个，暂不自动战斗，请手动处理该房间。");
        Enumerate(0, new(), []); return result.ToArray();
    }
    internal static Choice[] ShopCards(JsonNode run, Settings Config, HashSet<string> completed)
    {
        var shop = new List<Choice>();
        var id = run.At("nodo").S();
        var bought = run.At("compras").A().Select(x => x.S()).ToHashSet();
        foreach (var card in run.At("tienda", "buffs").A())
        {
            if (Cards.SkipEarlyShield(card, run, Config)) continue;
            var bid = "buff:" + card.At("id").S(); var rated = Cards.Rate(card, Config);
            if (!rated.Allowed || rated.Value < 0 || bought.Contains(id + ":" + bid) || completed.Contains(run.At("id").S() + ":" + id + ":" + bid)) continue;
            shop.Add(new("商店：" + J.Name(card), "sima.trade", new() { ["oferta"] = bid }, rated.Value, 0, rated.Core, Cards.Priority(card, Config, run, "sima.trade"), card) { Reason = Cards.Reason(card, Config, run, "sima.trade") });
        }
        return shop.ToArray();
    }
}
