using System.Text.Json.Nodes;

namespace FrostAbyss;

// Only positive, action-specific evidence can resolve a missing receipt.
// An unchanged state is never proof that a resource-consuming request failed.
static class RecoveryRules
{
    internal static bool Completed(string method, JsonObject args, JsonNode? before, JsonNode? after)
    {
        var b = before.At("sima", "run"); var a = after.At("sima", "run");
        if (b == null || a == null || b.At("id").S() == "" || b.At("id").S() != a.At("id").S()) return false;
        var node = b.At("nodo").S();
        bool NewlyDone() => node != "" && !b.At("hechos").A().Any(n => n.S() == node)
            && a.At("hechos").A().Any(n => n.S() == node) && a.At("nodo").S() == node;
        int Count(JsonNode run, string field, string id) => run.At(field).A().Count(c => c.At("id").S() == id);
        switch (method)
        {
            case "sima.tear":
                var hero = args.At("heroId").S();
                return hero != "" && b.At("heroes").A().Any(h => h.At("id").S() == hero && h.At("caido").B())
                    && RevivalRules.Error(b, a, hero) == null;
            case "sima.move":
                return args.At("nodoId").S() != "" && node != args.At("nodoId").S() && a.At("nodo").S() == args.At("nodoId").S();
            case "sima.fight": case "sima.open": case "sima.rest":
                return NewlyDone();
            case "sima.omen":
                return NewlyDone() || args.At("opcion").S() == "" && after.At("omen", "opciones").A().Any()
                    && a.At("nodo").S() == node && !before.At("omen", "opciones").A().Any();
            case "sima.pacto":
                return a.At("nodo").S() == node && b.At("pacto", "id").S() != ""
                    && b.At("pacto", "id").S() == a.At("pacto", "id").S()
                    && !b.At("pacto", "sellado").B() && a.At("pacto", "sellado").B();
            case "sima.trade":
                var purchase = node + ":" + args.At("oferta").S();
                return a.At("nodo").S() == node && !b.At("compras").A().Any(n => n.S() == purchase)
                    && a.At("compras").A().Any(n => n.S() == purchase);
            case "sima.reliquia": case "sima.blason":
                var field = method == "sima.reliquia" ? "reliquias" : "blasones";
                var id = args.At("id").S();
                return id != "" && a.At("nodo").S() == node && Count(a, field, id) == Count(b, field, id) + 1;
            default: return false;
        }
    }
}
