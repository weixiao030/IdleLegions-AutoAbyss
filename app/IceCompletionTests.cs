using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class IceCompletionTests
{
    static JsonNode Card(string id, string family, string desc = "+1% de daño") =>
        new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    static JsonObject NewRun(params JsonNode[] cards) => new()
    {
        ["id"] = "ice-completion-fixture", ["profundidad"] = 8,
        ["reliquias"] = new JsonArray(cards.Select(c => c.DeepClone()).ToArray()),
        ["llegada"] = new JsonObject { ["reliquias"] = cards.Length }
    };
    static JsonArray Progress(JsonNode? run, Settings cfg) =>
        JsonSerializer.SerializeToNode(IceStrategyRules.Progress(run, cfg), J.Options)!.AsArray();

    public static void Run(Action<bool, string> check)
    {
        var original = Paths.Data;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-IceCompletion-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = temp;
            var cfg = new Settings { StrategyId = 3, Simulations = 0, AllowBasicBurn = true, AllowBasicLightning = false };
            var pilot = new Pilot(); pilot.Save(cfg);
            var freeze = Card("es_carambano", "escarcha", "Tus autoataques CONGELAN al objetivo");
            var low = Card("fixture_low", "umbra", "Si tu vida está por debajo del 25%, +40% de daño");
            var reaction = Card("fixture_reaction", "brasa", "+25% de daño de tus reacciones y +5% de daño");
            var status = Card("fixture_status", "ponzona", "+40% de daño contra lo que lleve un estado encima");
            var light = Card("fixture_light", "aurora");
            var steel = Card("fixture_steel", "acero");
            Choice Pick(JsonNode run, string method, params JsonNode[] cards) => Pilot.Rank(pilot.CardChoices(cards, method, run))[0];
            string Id(Choice choice) => choice.Card.At("id").S();

            // Full families must yield even their first missing preference to an
            // underfilled family. Missing bounce/crit cores remain exceptions.
            foreach (var (family, preferred) in new[] { ("umbra", low), ("brasa", reaction), ("ponzona", status) })
            {
                var full = NewRun([freeze, .. Enumerable.Range(0, 8).Select(i => Card($"held_{family}_{i}", family))]);
                check(Id(Pick(full, "sima.reliquia", preferred, light, steel)) != preferred.At("id").S(),
                    $"ICE {family}满8张后首份指定偏好不挤掉缺少的光耀或钢系");
            }
            var fullLightning = NewRun([freeze, .. Enumerable.Range(0, 8).Select(i => Card("held_fu_" + i, "fulgor"))]);
            foreach (var every in new[] { 4, 5, 6 })
            {
                var bounce = Card("fixture_bounce_" + every, "fulgor", $"Cada {every} autoataques, tu golpe salta a todos los enemigos");
                check(Id(Pick(fullLightning, "sima.reliquia", light, bounce)) == bounce.At("id").S(),
                    $"ICE电系满8仍必拿缺失的{every}次全体弹跳");
            }
            foreach (var every in new[] { 8, 10 })
            {
                var crit = Card("fixture_crit_" + every, "fulgor", $"Cada {every} ataques, el siguiente es crítico garantizado");
                check(Id(Pick(fullLightning, "sima.reliquia", light, crit)) == crit.At("id").S(),
                    $"ICE电系满8仍必拿缺失的{every}次必暴击");
            }

            var heldLow = NewRun(freeze, low);
            var anotherLow = Card("different_id_same_low", "umbra", "Si tu vida está por debajo del 25%, +40% de daño");
            check(Id(Pick(heldLow, "sima.reliquia", anotherLow, light)) == light.At("id").S(),
                "ICE不同ID同类偏好按已有效果去重，回到缺系补齐");
            var emptyShadow = NewRun(freeze);
            check(Id(Pick(emptyShadow, "sima.reliquia", low, light)) == low.At("id").S(),
                "ICE未满系列的首份指定效果仍遵守用户偏好");
            var sameSeries = NewRun(freeze, light);
            var newLight = Card("different_light", "aurora");
            check(Id(Pick(sameSeries, "sima.reliquia", light, newLight)) == newLight.At("id").S(),
                "ICE同系同效果优先未持有遗物，减少无意义重复");
            check(Id(Pick(sameSeries, "sima.reliquia", newLight, steel)) == steel.At("id").S(),
                "ICE新牌小幅加分不跨越系列缺口，0张钢优于1张光耀");
            var fourCopies = NewRun(freeze, light, light, light);
            check(!Pick(fourCopies, "sima.reliquia", light).Label.Contains("兜底"),
                "ICE本层第4张同种遗物按合法普通获取参与补齐");

            var temporary = NewRun(freeze);
            var bounce5 = Card("temporary_bounce", "fulgor", "Cada 5 autoataques, tu golpe salta a todos los enemigos");
            temporary["buffs"] = new JsonArray(low.DeepClone(), bounce5.DeepClone());
            temporary["blasones"] = new JsonArray(status.DeepClone());
            check(Id(Pick(temporary, "sima.reliquia", low, status, bounce5, light)) == bounce5.At("id").S(),
                "ICE赐福和徽章不能替代需要收集的可继承弹跳遗物");
            check(Cards.Priority(low, cfg, temporary, "sima.reliquia") > Cards.Priority(light, cfg, temporary, "sima.reliquia"),
                "ICE商店暂有效果不能冒充遗物的首份指定效果");
            var shopRepeat = Pick(temporary, "sima.trade", anotherLow);
            check(shopRepeat.Priority < Pick(temporary, "sima.trade", reaction).Priority
                && !shopRepeat.Reason.Contains("系列本层") && shopRepeat.Label.Contains("赐福"),
                "ICE商店基线含已有赐福与徽章，重复偏好降权且不发遗物补系奖励");
            check(Cards.Priority(light, cfg, temporary, "sima.trade") < Cards.Priority(light, cfg, temporary, "sima.reliquia"),
                "ICE相同家族标签的商店赐福不计遗物补齐优先级");
            check(Progress(temporary, cfg).Sum(x => x.At("count").N()) == 1,
                "ICE七系进度不计赐福或徽章，即使字段带家族名");

            var carry = NewRun(freeze, low, low, low);
            carry["reliquias"]!.AsArray().Add(light.DeepClone());
            carry["reliquias"]!.AsArray().Add(status.DeepClone());
            carry["guardaPendiente"] = new JsonObject { ["cartas"] = new JsonArray(low.DeepClone(), light.DeepClone()) };
            carry["guardaPendiente"]!["cartas"]![0]!["llevas"] = 3;
            check(Id(Pick(carry, "sima.guarda", carry.At("guardaPendiente", "cartas").A().ToArray())) == light.At("id").S(),
                "ICE带走阶段仍遵守同种最多3张，满层候选让位合法未满遗物");
            check(Id(Pick(carry, "sima.guarda", status, light)) == status.At("id").S(),
                "ICE继承判断只看已继承基线，本层取得指定效果后章末仍可优先带走首份");
            var capped = carry.At("guardaPendiente", "cartas").A().First();
            check(Pick(carry, "sima.guarda", capped).Label.Contains("兜底"),
                "ICE继承菜单只有满层候选时保持原有兜底，不因偏好暂停");
            var disabled = light.DeepClone(); disabled["available"] = false;
            check(Id(Pick(heldLow, "sima.reliquia", disabled, steel)) == steel.At("id").S(),
                "ICE补齐目标不恢复游戏标明不可用的卡牌");
            var shock = Card("fu_descarga", "fulgor", "Tus autoataques ELECTRIFICAN al objetivo");
            check(pilot.CardChoices([shock, steel], "sima.reliquia", heldLow).All(c => Id(c) != "fu_descarga"),
                "ICE未满系列也继续遵守关闭普攻带电的独立开关");

            var progressRun = NewRun(freeze, light, light);
            for (int i = 0; i < 6; i++) progressRun["reliquias"]!.AsArray().Add(light.DeepClone());
            var progress = Progress(progressRun, cfg).Single(x => x.At("family").S() == "aurora");
            check(progress.At("count").N() == 8 && progress.At("inheritedCount").N() == 2,
                "ICE进度分别显示本层8张和实际已继承2张，不将本层达标当成跨层达标");
            progressRun.Remove("llegada");
            check(Progress(progressRun, cfg).All(x => x!["inheritedCount"] == null),
                "ICE旧快照缺少继承证据时显示未知，不编造已继承张数");
            check(Progress(null, cfg).Count == 7 && Progress(null, cfg).All(x => x.At("count").N() == 0),
                "ICE无深渊时七系计数清零，不读取旧局或玩家缓存");

            // Exercise the real shop builder and exit gate using synthetic
            // snapshots only: no bridge command and no player currency used.
            var shop = NewRun(freeze);
            shop["nodo"] = "shop"; shop["mapa"] = JsonNode.Parse("{\"nodos\":{\"shop\":{\"tipo\":\"chatarrero\"},\"exit\":{\"tipo\":\"osario\"}}}");
            shop["destinos"] = new JsonArray("exit");
            shop["botin"] = new JsonObject { ["obolos"] = 10 };
            shop["tienda"] = new JsonObject { ["precio"] = 10, ["buffs"] = new JsonArray(low.DeepClone(), light.DeepClone()) };
            var snapshot = new JsonObject { ["sima"] = new JsonObject { ["run"] = shop } };
            var buys = Pilot.BeforeShopExit(pilot.Build(snapshot), null);
            check(buys.Length == 2 && buys.All(c => c.Method == "sima.trade") && Id(Pilot.Rank(buys)[0]) == low.At("id").S(),
                "ICE真实商店构建路径有预算时优先买有益赐福，再离开");
            shop["compras"] = new JsonArray("shop:buff:" + low.At("id").S(), "shop:buff:" + light.At("id").S());
            check(pilot.Build(snapshot).All(c => c.Method != "sima.trade"), "ICE商店同房已购赐福不重复购买");
            shop.Remove("compras"); shop["botin"]!["obolos"] = 9;
            check(pilot.Build(snapshot).All(c => c.Method != "sima.trade"), "ICE商店预算不足时正常离开，不派发超预算购买");

            // Sufficient offered supply is a controlled assumption, not a claim
            // that random game menus will always contain the missing families.
            foreach (var method in new[] { "sima.reliquia", "sima.guarda" })
            {
                var sequence = NewRun(freeze, low);
                int selected = 0;
                while (IceStrategyRules.Families.Any(f => IceStrategyRules.CountFamily(sequence.At("reliquias").A(), f) < 8) && selected < 70)
                {
                    var missing = IceStrategyRules.Families.Where(f => IceStrategyRules.CountFamily(sequence.At("reliquias").A(), f) < 8).ToArray();
                    // Three choices, with a repeated high-rank shadow lure on
                    // every menu and at least one underfilled family available.
                    var offers = new[] { anotherLow, Card($"next_{selected}", missing[^1]), Card($"second_{selected}", missing[0]) };
                    var before = sequence.ToJsonString();
                    var choice = Pick(sequence, method, offers);
                    if (sequence.ToJsonString() != before) throw new Exception("ICE排序修改了原快照");
                    if (IceStrategyRules.CountFamily(sequence.At("reliquias").A(), Cards.Family(choice.Card)) >= 8)
                        throw new Exception("ICE连续选择仍抢已满系列");
                    sequence["reliquias"]!.AsArray().Add(choice.Card!.DeepClone());
                    sequence["llegada"]!["reliquias"] = sequence.At("reliquias").A().Count();
                    selected++;
                }
                check(selected == 54 && Progress(sequence, cfg).All(x => x.At("count").N() == 8),
                    $"ICE {method}连续54次在供应充足夹具中补齐七系，重复暗影偏好不再饿死缺系");
            }
        }
        finally { Paths.Data = original; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
