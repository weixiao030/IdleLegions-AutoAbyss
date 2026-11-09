using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class ResilienceTests
{
    static JsonNode Parse(string text) => JsonNode.Parse(text)!;
    static JsonObject Fixture() => Parse("""{"loggedIn":true,"sima":{"run":{"id":"fixture-run","nodo":"room","profundidad":0,"lagrimas":2,"hechos":["room"],"destinos":["next"],"mapa":{"nodos":{"room":{"tipo":"combate"},"next":{"tipo":"santuario"}}},"heroes":[{"id":"mago","nombre":"Mago","caido":true,"hp":0,"hpMax":100},{"id":"arquera","nombre":"Arquera","caido":true,"hp":0,"hpMax":100}]}}} """).AsObject();
    public static async Task Run(Action<bool, string> check)
    {
        var oldData = Paths.Data; var oldGame = Paths.GameData;
        var temp = Path.Combine(Path.GetTempPath(), "FrostAbyss-Resilience-" + Guid.NewGuid().ToString("N"));
        try
        {
            Paths.Data = Path.Combine(temp, "profile"); Paths.GameData = Path.Combine(temp, "game");
            Directory.CreateDirectory(Paths.Bridge);
            File.WriteAllText(Path.Combine(Paths.Bridge, "session.key"), new string('x', 64));
            check(new Settings() is { SkipEightHitPact: true, SkipDodgePact: true, RetryTimeouts: true }, "新增契约避让与超时恢复默认开启");
            var legacy = JsonSerializer.Deserialize<Settings>("{}", J.Options)!;
            check(legacy is { SkipEightHitPact: true, SkipDodgePact: true, RetryTimeouts: true }, "旧配置缺少新字段时保留默认避让和恢复");
            var pilot = new Pilot();
            pilot.Save(new Settings { SkipEightHitPact = false, SkipDodgePact = false, RetryTimeouts = false });
            check(new Pilot().Config is { SkipEightHitPact: false, SkipDodgePact: false, RetryTimeouts: false }, "三个开关关闭后保存及重载不丢失");
            var cfg = new Settings { Simulations = 0, DelayMs = 0, SkipShieldCardsEarly = false };
            pilot.Save(cfg);
            var fight = Fixture();
            var run = fight.At("sima", "run")!;
            run["lagrimas"] = 0; run["heroes"] = new JsonArray(); run["hechos"] = new JsonArray();
            run["reliquias"] = Parse("""[{"id":"es_carambano"},{"id":"au_alba"}]""");
            foreach (var id in new[] { "costra", "escurridizos" })
            {
                run["pacto"] = new JsonObject { ["id"] = id, ["sellado"] = false };
                var pick = pilot.Build(fight).Single();
                check(pick.Method == "sima.fight" && pick.Reason.Contains("不缔约"), id + " 避让后生产入口继续战斗并显示原因");
                run["mapa"]!["nodos"]!["room"]!["tipo"] = "osario";
                check(pilot.Build(fight).Single().Method == "sima.open", id + " 避让后正常打开骨库");
                run["mapa"]!["nodos"]!["room"]!["tipo"] = "combate";
                cfg.SkipEightHitPact = id != "costra"; cfg.SkipDodgePact = id != "escurridizos";
                pilot.Save(cfg);
                check(pilot.Build(fight).Single().Method == "sima.pacto", id + " 只关闭对应开关即恢复自动缔约");
                cfg.SkipEightHitPact = cfg.SkipDodgePact = true;
                pilot.Save(cfg);
            }
            run["pacto"] = new JsonObject { ["id"] = "horda", ["sellado"] = false };
            check(pilot.Build(fight).Single().Method == "sima.pacto", "其他契约仍正常签订");
            run["pacto"]!["sellado"] = true;
            check(pilot.Build(fight).Single().Method == "sima.fight", "已签契约不再次缔约");
            run["pacto"]!["sellado"] = false; run["profundidad"] = 7;
            check(pilot.Build(fight).Single().Method == "sima.fight", "超过缔约层数仍正常战斗");
            run["profundidad"] = 0; run["reliquias"] = new JsonArray();
            check(!Pilot.ShouldPact(run, cfg), "无冻结核心不自动缔约");
            check(PactRules.SkipReason(Parse("""{"desc":"Los 8 primeros golpes que recibe cada enemigo no le hacen nada"}"""), cfg) != null
                && PactRules.SkipReason(Parse("""{"desc":"Los enemigos esquivan un 15% de vuestros golpes"}"""), cfg) != null, "缺ID时识别真实客户端契约描述");
            run["reliquias"] = Parse("""[{"id":"es_carambano"}]""");
            run["mapa"]!["nodos"]!["room"]!["tipo"] = "jefe";
            run["pacto"]!["sellado"] = false;
            run["profundidad"] = 6;
            check(pilot.Build(fight).Single().Method == "sima.pacto", "Boss P6在默认前7层范围内先缔约再战斗");
            run["pacto"]!["sellado"] = true;
            check(pilot.Build(fight).Single().Method == "sima.fight", "Boss已签约后进入战斗，不重复签约");
            run["pacto"]!["sellado"] = false; run["profundidad"] = 7;
            check(pilot.Build(fight).Single().Method == "sima.fight", "Boss P7超出前7层不自动缔约");
            cfg.PactFloors = 9; pilot.Save(cfg);
            check(pilot.Build(fight).Single().Method == "sima.pacto", "Boss按自定义契约层数9而不是硬编码7执行");
            run["pacto"]!["id"] = "costra";
            check(pilot.Build(fight).Single().Method == "sima.fight", "Boss契约同样遵守前8次受击免疫避让");
            run["pacto"]!["id"] = "escurridizos";
            check(pilot.Build(fight).Single().Method == "sima.fight", "Boss契约同样遵守15%闪避避让");
            cfg.PactFloors = 7; pilot.Save(cfg);
            var core = Parse("""{"id":"es_carambano"}""");
            var shield = Parse("""{"id":"au_alba","desc":"Empiezas cada sala con un escudo del 25% de tu vida durante 3 s"}""");
            var defense = Parse("""{"id":"defense","familia":"ponzona","desc":"+20% de vida"}""");
            var attack = Parse("""{"id":"attack","desc":"+20% de daño"}""");
            var bounce = Parse("""{"id":"fu_cadena","desc":"Cada 5 autoataques, tu golpe salta a todos"}""");
            var held = new JsonObject { ["reliquias"] = new JsonArray(core.DeepClone(), shield.DeepClone()) };
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda" })
            {
                check(Pilot.Rank(pilot.CardChoices([defense, shield, attack, bounce], method, held))[0].Card.At("id").S() == "fu_cadena", method + " 弹跳优于攻击及重复盾、防御");
                check(Pilot.Rank(pilot.CardChoices([defense, shield, attack], method, held))[0].Card.At("id").S() == "attack", method + " 普通攻击优于冰毒纯防御及重复盾");
            }
            var firstShield = new JsonObject { ["reliquias"] = new JsonArray(core.DeepClone()) };
            check(Pilot.Rank(pilot.CardChoices([attack, shield, bounce], "sima.reliquia", firstShield))[0].Card.At("id").S() == "au_alba", "首个开局盾仍先补齐，避免完全放弃生存");
            var shop = Fixture(); var sr = shop.At("sima", "run")!;
            sr["heroes"] = new JsonArray(); sr["reliquias"] = held["reliquias"]!.DeepClone(); sr["lagrimas"] = 0;
            sr["mapa"]!["nodos"]!["room"]!["tipo"] = "chatarrero";
            sr["botin"] = new JsonObject { ["obolos"] = 100 };
            sr["tienda"] = new JsonObject { ["precio"] = 1, ["buffs"] = new JsonArray(defense.DeepClone(), shield.DeepClone(), attack.DeepClone(), bounce.DeepClone()) };
            check(Pilot.Rank(pilot.Build(shop).Where(c => c.Method == "sima.trade").ToArray())[0].Card.At("id").S() == "fu_cadena", "生产商店入口弹跳优于重复盾与纯防御");

            check(new Settings() is { SkipShieldCardsEarly: true, ShieldSkipFloors: 7 }, "前期不选护盾默认开启且前7层");
            check(legacy is { SkipShieldCardsEarly: true, ShieldSkipFloors: 7 }, "旧配置自动获得前7层不选护盾默认值");
            cfg.SkipShieldCardsEarly = true; cfg.ShieldSkipFloors = 7; pilot.Save(cfg);
            foreach (var method in new[] { "sima.reliquia", "sima.blason", "sima.guarda" })
            {
                foreach (var depth in new[] { 0, 6 })
                {
                    var early = new JsonObject { ["profundidad"] = depth, ["reliquias"] = new JsonArray(core.DeepClone()) };
                    var opts = pilot.CardChoices([shield, attack], method, early);
                    check(opts.Length == 1 && opts[0].Card.At("id").S() == "attack", method + " P" + depth + "排除首盾，只拿攻击");
                }
                var late = new JsonObject { ["profundidad"] = 7, ["reliquias"] = new JsonArray(core.DeepClone()) };
                check(Pilot.Rank(pilot.CardChoices([shield, attack], method, late))[0].Card.At("id").S() == "au_alba", method + " P7解除护盾避让，可补首盾");
            }
            check(!pilot.Build(shop).Any(c => c.Card.At("id").S() == "au_alba"), "前7层商店不买护盾");
            sr["profundidad"] = 7;
            check(pilot.Build(shop).Any(c => c.Card.At("id").S() == "au_alba"), "P7商店恢复护盾购买");
            var onlyShield = false; try { pilot.CardChoices([shield], "sima.reliquia", firstShield); } catch (Exception e) { onlyShield = e.Message.Contains("护盾"); }
            check(onlyShield, "强制选牌只剩护盾时明确暂停，不偷偷违反避让");
            cfg.ShieldSkipFloors = 0; pilot.Save(cfg);
            check(pilot.CardChoices([shield], "sima.reliquia", firstShield).Length == 1, "护盾跳过层数0关闭过滤");
            cfg.ShieldSkipFloors = 2; pilot.Save(cfg);
            firstShield["profundidad"] = 2;
            check(pilot.CardChoices([shield], "sima.reliquia", firstShield).Length == 1, "护盾使用自定义X层边界");
            cfg.SkipShieldCardsEarly = false; pilot.Save(cfg); firstShield["profundidad"] = 0;
            check(pilot.CardChoices([shield], "sima.reliquia", firstShield).Length == 1 && !new Pilot().Config.SkipShieldCardsEarly && new Pilot().Config.ShieldSkipFloors == 2, "关闭护盾过滤立即恢复选盾，开关和层数保存重载");
            var state = Fixture(); var requests = new List<string>();
            using var stop = new CancellationTokenSource();
            var mode = "revive"; var delayedAction = "";
            var service = Task.Run(async () =>
            {
                var seen = "";
                while (!stop.IsCancellationRequested)
                {
                    var command = J.Read(Path.Combine(Paths.Bridge, "command.json"));
                    var id = command.At("id").S();
                    if (id != "" && id != seen)
                    {
                        seen = id; var method = command.At("method").S(); requests.Add(method + ":" + command.At("args", "heroId").S());
                        var response = new JsonObject { ["id"] = id, ["ok"] = true, ["confirmed"] = true };
                        if (method == "sima.tear" && mode is "revive" or "lost")
                        {
                            var hero = state.At("sima", "run", "heroes").A().Single(h => h.At("id").S() == command.At("args", "heroId").S());
                            hero["caido"] = false; hero["hp"] = 50;
                            state["sima"]!["run"]!["lagrimas"] = state.At("sima", "run", "lagrimas").N() - 1;
                        }
                        if (mode == "lost" && method == "sima.tear" || mode == "unknown" && method == "sima.tear" || mode == "readonly" && requests.Count == 1)
                            response = new JsonObject { ["id"] = id, ["ok"] = false, ["code"] = "receipt_missing", ["error"] = "fixture response lost" };
                        if (mode == "unsent" && requests.Count == 1)
                            response = new JsonObject { ["id"] = id, ["ok"] = false, ["retryable"] = true, ["error"] = "fixture not sent" };
                        if (mode == "battle" && method == "sima.fight")
                        {
                            delayedAction = method;
                            response = new JsonObject { ["id"] = id, ["ok"] = false, ["code"] = "receipt_missing", ["error"] = "fixture battle timeout" };
                        }
                        if (mode == "battle" && method == "local.refresh" && delayedAction != "") state["sima"]!["run"]!["hechos"] = new JsonArray("room");
                        state["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                        state["lastCommand"] = response.DeepClone();
                        J.Write(Path.Combine(Paths.Bridge, "snapshot.json"), state);
                        J.Write(Path.Combine(Paths.Bridge, "response.json"), response);
                    }
                    await Task.Delay(10);
                }
            });
            void Publish() { state["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0; J.Write(Path.Combine(Paths.Bridge, "snapshot.json"), state); }
            async Task Step(Pilot p) => await (Task)typeof(Pilot).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(p, [false, CancellationToken.None])!;
            try
            {
                Publish(); pilot = new Pilot(); pilot.Save(cfg);
                await Step(pilot); await Step(pilot);
                check(requests.SequenceEqual(["sima.tear:mago", "sima.tear:arquera"]) && state.At("sima", "run", "lagrimas").N() == 0, "真实Pilot.Step两倒地两眼泪依次复活不同英雄，不发预览请求阻塞");
                check(pilot.Build(state).Single().Method == "sima.move" && state.At("sima", "run", "heroes").A().All(h => !h.At("caido").B()), "复活完成后恢复正常前进");
                var stale = Fixture(); stale["peeks"] = Parse("""{"room":{"legion":[{"nombre":"Mago"}],"enemigos":[{"name":"enemy"}]}}""");
                foreach (var h in stale.At("sima", "run", "heroes").A()) { h["caido"] = false; h["hp"] = 50; }
                check(pilot.FightChoices(stale, stale.At("sima", "run")!, "room").Single().Args.At("objetivos")!.AsObject().Count == 2, "过时预览不遗漏已复活队员的战斗目标");
                var b = Fixture().At("sima", "run")!;
                check(RevivalRules.Error(b, b, "mago") != null && RevivalRules.Choice(state.At("sima", "run")) == null, "成功回执但未站起时拒绝继续，无眼泪不派复活");
                mode = "lost"; state = Fixture(); requests.Clear(); Publish();
                var recovered = await new Bridge().Send("sima.tear", new() { ["heroId"] = "mago" }, CancellationToken.None);
                check(recovered.At("recovered").B() && requests.SequenceEqual(["sima.tear:mago", "local.refresh:"]) && state.At("sima", "run", "lagrimas").N() == 1, "复活已成功但回执丢失：只重查一次，核验后继续，不重复花眼泪");
                mode = "unknown"; state = Fixture(); requests.Clear(); Publish(); var paused = false;
                try { await new Bridge().Send("sima.tear", new() { ["heroId"] = "mago" }, CancellationToken.None); } catch (Exception e) { paused = e.Message.Contains("恢复2次"); }
                check(paused && requests.Count == 3 && requests.Count(r => r.StartsWith("sima.tear")) == 1 && state.At("sima", "run", "lagrimas").N() == 2, "未知复活结果最多重查两次，仍不明确暂停且不重发消耗操作");
                mode = "battle"; state = Fixture(); state["sima"]!["run"]!["hechos"] = new JsonArray(); requests.Clear(); Publish();
                recovered = await new Bridge().Send("sima.fight", new(), CancellationToken.None);
                check(recovered.At("recovered").B() && requests.SequenceEqual(["sima.fight:", "local.refresh:"]), "战斗超时后查询发现房间已结算，自动继续且不重复战斗");
                mode = "unsent"; requests.Clear(); Publish();
                await new Bridge().Send("sima.tear", new() { ["heroId"] = "mago" }, CancellationToken.None);
                check(requests.Count == 2 && requests.All(r => r == "sima.tear:mago"), "桥接明确证明尚未发出的操作允许自动重试");
                mode = "readonly"; requests.Clear(); Publish();
                await new Bridge().Send("sima.peek", new() { ["nodoId"] = "next" }, CancellationToken.None);
                check(requests.SequenceEqual(["sima.peek:", "sima.peek:"]), "只读战斗预览超时自动重试后恢复");
                mode = "unknown"; requests.Clear(); Publish(); paused = false;
                try { await new Bridge { RetryTimeouts = false }.Send("sima.tear", new() { ["heroId"] = "mago" }, CancellationToken.None); } catch { paused = true; }
                check(paused && requests.Count == 1, "关闭超时恢复后立即暂停，没有后台重试");
                var after = Fixture(); after["sima"]!["run"]!["id"] = "unrelated-run";
                check(!RecoveryRules.Completed("sima.tear", new() { ["heroId"] = "mago" }, Fixture(), after), "不同深渊状态不能误判为超时操作成功");
            }
            finally { stop.Cancel(); await service; }
        }
        finally { Paths.Data = oldData; Paths.GameData = oldGame; if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
