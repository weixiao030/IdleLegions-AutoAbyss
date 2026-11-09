using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FrostAbyss;

// IDs and short visible descriptions were observed in historical game receipts.
// This only filters new room pacts; it never cancels an already signed pact.
static class PactRules
{
    internal static string? SkipReason(JsonNode? pact, Settings cfg)
    {
        if (pact is not JsonObject || pact.At("sellado").B()) return null;
        var id = pact.At("id").S();
        var text = Effects.Normalize(pact.At("desc").S() + " " + pact.At("descZh").S()).ToLowerInvariant();
        if (cfg.SkipEightHitPact && (id == "costra"
            || text.Contains("los 8 primeros golpes") && text.Contains("no le hacen nada")
            || Regex.IsMatch(text, @"前\s*8\s*次.{0,20}(?:无效|免疫|免控|不受控制)")
            || Regex.IsMatch(text, @"first 8 hits.{0,40}(?:no effect|nothing|immune)")))
            return "前8次受击免疫契约已避让";
        if (cfg.SkipDodgePact && (id == "escurridizos"
            || Regex.IsMatch(text, @"esquivan? un 15\s*%|15\s*%\s*(?:闪避|dodge)|(?:闪避|dodge).{0,15}15\s*%")))
            return "15%闪避契约已避让";
        return null;
    }
}
