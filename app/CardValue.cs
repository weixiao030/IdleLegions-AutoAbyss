using System.Text.Json.Nodes;

namespace FrostAbyss;

// A finite, family-neutral marginal estimate for selection without sampling.
// These are utility estimates, never server damage or drop probabilities.
static class CardValue
{
    internal static double Marginal(JsonNode card, IEnumerable<JsonNode> baseline, Settings cfg)
    {
        var cards = baseline.ToArray();
        var held = Effects.Parse(cards, cfg); var next = Effects.Parse(cards.Append(card), cfg);
        double Ratio(double a, double b) => Math.Log(Math.Max(.001, 1 + b) / Math.Max(.001, 1 + a));
        double output = Ratio(held.Damage, next.Damage) + Ratio(held.Speed, next.Speed)
            + Ratio(held.VsStatus + held.VsSlow, next.VsStatus + next.VsSlow)
            + Ratio(held.CritDamage * .5 + held.Crit, next.CritDamage * .5 + next.Crit)
            + Ratio(held.ReactionDamage, next.ReactionDamage) * .3;
        double survival = Ratio(held.Life, next.Life) + Ratio(held.Defense, next.Defense) * .5
            + Ratio(held.Shield, next.Shield) * .25 + Math.Max(0, next.Leech - held.Leech);
        double novelty = !held.Freeze && next.Freeze ? .5 : 0;
        if (next.Cleave > 0) novelty += 2 * (1d / next.Cleave - (held.Cleave > 0 ? 1d / held.Cleave : 0));
        if (held.Slow == 0 && next.Slow > 0) novelty += next.Slow * .2;
        return output + survival * cfg.RiskAversion + novelty;
    }
}
