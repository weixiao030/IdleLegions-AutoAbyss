using System.Text.Json.Nodes;
using FrostAbyss;
namespace FrostAbyss.Legacy131;
static class Selection
{
    internal static Choice[] CardChoices(JsonNode[] cards, string method, JsonNode run, Settings Config)
    {
        if (!OpeningRules.Complete(run, Config)) return OpeningRules.Choices(cards, method, run, Config);
        var legal = cards.Where(CardRules.Available).ToArray();
        var baseline = method == "sima.guarda" ? FrostAbyss.Cards.Inherited(run)
            : method == "sima.blason" ? FrostAbyss.Cards.Current(run) : run.At("reliquias").A().ToArray();
        bool Redundant(JsonNode c) => CardRules.HasInheritedCore(run, Config) && FrostAbyss.Cards.IsCore(c, Config) && baseline.Any(x => FrostAbyss.Cards.IsCore(x, Config))
            && CardRules.AllTargetEvery(c) == 0 && !CardRules.HasSupplementalValue(c, Config)
            || Effects.Parse([c], Config).Slow > 0 && Effects.Parse([c], Config).Slow <= Effects.Parse(baseline, Config).Slow
                && !CardRules.HasSupplementalValue(c, Config) && CardRules.AllTargetEvery(c) == 0;
        cards = legal.Where(c => Cards.Rate(c, Config).Allowed && !Cards.SkipEarlyShield(c, run, Config) && !Redundant(c)).ToArray();
        bool fallback = cards.Length == 0;
        if (fallback) { if (method == "sima.trade") return []; cards = legal; }
        // A fully inherited stack grants no additional effect. Prefer any card
        // that can still add a stack; keep a legal fallback when all are capped.
        if (method == "sima.guarda" && cards.Any(c => c.At("llevas").N() < 3))
            cards = cards.Where(c => c.At("llevas").N() < 3).ToArray();
        var action = method == "sima.guarda" ? "带走遗物：" : method == "sima.blason" ? "选择徽章：" : "选择遗物：";
        var choices = cards.Select(c => new Choice((fallback ? "经典规则兜底：" : action) + J.Name(c), method,
            new() { ["id"] = c.At("id").S() }, core: FrostAbyss.Cards.IsCore(c, Config),
            priority: Cards.Priority(c, Config, run, method) - CardRules.RepeatPenalty(c, baseline, Config), card: c)
            { Reason = Cards.Reason(c, Config, run, method) + "；经典选牌修正：重复收益降权，继承基线按到达回执核对" }).ToArray();
        if (choices.Length == 0) throw new Exception("游戏菜单没有实际可选的卡牌，不能派发不可用选项。");
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
