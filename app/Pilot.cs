using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

sealed class Pilot
{
    public readonly Bridge Bridge = new();
    public Settings Config { get; private set; }
    Simulation sim = new(false);
    SearchMemory learning = new();
    Lazy<Legacy131.Simulation> legacySim = new(() => new());
    readonly DataStorage storage = new();
    int ModelBattles => Config.StrategyId == 2 ? (legacySim.IsValueCreated ? legacySim.Value.Battles : 0) : sim.Battles;
    double ModelLastLoss => Config.StrategyId == 2 ? (legacySim.IsValueCreated ? legacySim.Value.LastLoss : .18) : sim.LastLoss;
    void Learn(JsonNode? snapshot) { sim.Learn(snapshot); if (Config.StrategyId == 2) legacySim.Value.Learn(snapshot); learning.Observe(snapshot.At("sima", "run")); }
    void Evaluate(Choice[] candidates, JsonNode snapshot, Settings config, CancellationToken ct) { if (OpeningRules.UsesLegacy(config, snapshot.At("sima", "run"))) legacySim.Value.Evaluate(candidates, snapshot, config, ct); else sim.Evaluate(candidates, snapshot, config, ct); }
    readonly SemaphoreSlim operation = new(1, 1);
    readonly ConcurrentQueue<object> logs = new();
    CancellationTokenSource? cancel;
    Task? worker;
    volatile string phase = "待命";
    volatile string message = "连接游戏后可先查看推演，再开始自动下潜。";
    volatile Choice[] choices = [];
    readonly HashSet<string> completed = [];
    int runs;
    int clearedFloors;
    int maxDepth, wins, losses;
    bool coreAcquired;
    double initialCores, initialShards, finalCores, finalShards;
    readonly List<double> predictionErrors = [];
    readonly HashSet<string> sessionBattles = [];
    long decisions;
    readonly ConcurrentQueue<object> receipts = new();
    readonly ConcurrentQueue<JsonObject> battleRecords = new();
    readonly List<object> roundResults = [];
    readonly List<object> chapterReviews = [];
    double roundInitialCores, roundInitialShards;
    string reportPath = "";
    DateTimeOffset started = DateTimeOffset.Now;
    int running;
    bool sessionOpen;
    readonly HashSet<string> skippedReplays = [];
    public bool Active => Volatile.Read(ref running) == 1;
    public object StorageStatus() => storage.Status();
    public void Maintain(StorageRequest request)
    {
        if (learning.Neural.TrainingActive) throw new Exception("网络正在训练，请等待训练完成后维护数据。");
        storage.Validate(request);
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) throw new Exception("请先暂停下潜，等待当前操作结束后维护数据。");
        cancel = new();
        worker = Task.Run(() =>
        {
            try
            {
                phase = "数据维护中";
                message = storage.Run(request, cancel.Token);
                phase = "维护完成"; Note(message);
            }
            catch (OperationCanceledException) { phase = "已暂停"; message = "数据维护已暂停，已处理的文件保留。"; }
            catch (Exception e) { phase = "需要处理"; message = e.Message; }
            finally
            {
                if (request.Action == "delete" && request.Groups?.Any(g => g is "battles" or "decisions") == true)
                {
                    // Recreate all indexes after deletions, including a partially
                    // completed deletion. Published neural weights remain on disk.
                    sim = new(false); learning = new(); legacySim = new(() => new());
                }
                if (request.Action == "delete" && request.Groups?.Contains("session") == true) { receipts.Clear(); battleRecords.Clear(); roundResults.Clear(); }
                if (request.Action == "delete" && request.Groups?.Contains("reports") == true) reportPath = "";
                Volatile.Write(ref running, 0);
            }
        });
    }
    public object LatestReport() => reportPath != "" && File.Exists(reportPath) ? new { name = Path.GetFileName(reportPath), text = ResourceJournal.RefreshReport(File.ReadAllText(reportPath), receipts, roundResults.ToArray()) } : throw new Exception("尚未生成报告。");
    public Pilot()
    {
        Directory.CreateDirectory(Paths.Data);
        Config = LoadConfig();
        // Restore display data only. Starting automation creates a new session;
        // old receipts never resume commands or mark operations completed.
        var previous = J.Read(Path.Combine(Paths.Data, "last-session.json"));
        foreach (var row in previous.At("battleRecords").A().OfType<JsonObject>())
            battleRecords.Enqueue(BattleJournal.Upgrade(row));
        foreach (var row in previous.At("receipts").A()) receipts.Enqueue(row.DeepClone());
        foreach (var row in previous.At("roundResults").A()) roundResults.Add(row.DeepClone());
        runs = (int)previous.At("runs").N();
        Bridge.RetryTimeouts = Config.RetryTimeouts;
        Bridge.Notice = text => { phase = "超时恢复中"; message = text; Note(text); };
        var reports = Path.Combine(Paths.Data, "reports");
        if (Directory.Exists(reports)) reportPath = Directory.GetFiles(reports, "*.md").Order().LastOrDefault() ?? "";
    }
    Settings LoadConfig() { try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.Config), J.Options)!; if (!Settings.ValidFormation(s.Formation)) s.Formation = new Settings().Formation; s.Validate(); return s; } catch { return new(); } }
    public void Save(Settings cfg) { if (Active) throw new Exception("请暂停后再修改参数。"); cfg.Validate(); J.Write(Paths.Config, cfg); Config = cfg; Bridge.RetryTimeouts = cfg.RetryTimeouts; Note("参数已保存"); }
    public void SaveFormation(string[]? formation)
    {
        if (!Settings.ValidFormation(formation)) throw new Exception("请选择 4 个不同职业，按前排到后排排列。");
        var next = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Config, J.Options), J.Options)!;
        next.Formation = formation!.ToArray();
        Save(next);
        Note("站位已保存：" + string.Join(" → ", formation!.Select(FormationRules.Name)) + "；下次入场生效。");
    }
    public void Note(string text, string level = "info")
    {
        var entry = new { time = DateTimeOffset.Now, text = GameText.Text(text), level }; logs.Enqueue(entry);
        while (logs.Count > 160) logs.TryDequeue(out _);
        try { File.AppendAllText(Path.Combine(Paths.Data, "pilot-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl"), JsonSerializer.Serialize(entry, J.Options) + "\n"); } catch { }
    }
    public object Status()
    {
        var s = Bridge.Snapshot;
        var slim = s?.DeepClone();
        if (slim is JsonObject root)
        {
            root.Remove("lastBattle"); root.Remove("lastLoot"); root.Remove("tacticasCatalogo"); root.Remove("defs");
            foreach (var h in root.At("heroes").A()) foreach (var skill in h.At("skills").A()) if (skill is JsonObject sk) { sk.Remove("conds"); sk.Remove("focusOpts"); sk.Remove("pvpDesc"); }
        }
        var runState = s.At("sima", "run");
        return new { appVersion = Branding.Version, bridge = new { version = s.At("version").S(), bundledVersion = FrostAbyss.Bridge.BundledVersion, updateRequired = FrostAbyss.Bridge.NeedsUpdate(s) }, cardState = new { iceOnly = !OpeningRules.Complete(runState, Config), openingComplete = OpeningRules.Complete(runState, Config), inheritedIceCount = OpeningRules.InheritedBuild(runState).Count(c => Cards.Family(c) == "escarcha"), glacierInherited = OpeningRules.InheritedBuild(runState).Any(c => SynergySelection.Glacier(c, Config)), bounceInherited = OpeningRules.Bounce(OpeningRules.InheritedBuild(runState)), freezeInherited = OpeningRules.Freeze(OpeningRules.InheritedBuild(runState), Config), retainedFreeze = runState != null && Cards.Inherited(runState).Concat(Cards.RetainedBadges(runState, Cards.Inherited(runState))).Any(c => Cards.IsCore(c, Config)), inheritedCount = CardRules.InheritedBaseline(runState).Length, series = IceStrategyRules.Progress(runState, Config), targetSeries = SynergySelection.Uses(Config) ? SynergySelection.Target : 0 }, active = Active, phase, message = GameText.Text(message), connected = Bridge.Fresh(s) && s.At("loggedIn").B(), bridgeConnected = Bridge.Fresh(s), config = Config, snapshot = slim, battles = BattleJournal.Summarize(battleRecords), resourceRounds = ResourceJournal.Rounds(receipts, roundResults.ToArray()), equipment = DropJournal.Records(receipts), acquisitions = receipts.Select(x => JsonSerializer.SerializeToNode(x, J.Options)).Where(x => x.At("cardId").S() != "").ToArray(), choices = choices.Select(c => new { Label = GameText.Text(c.Label), c.Method, c.Args, c.Forecast, c.Core, c.Priority, c.Reason, c.Search }), learning = learning.Status(), searchHardware = SearchHardware.Current, logs = logs.ToArray().Reverse(), runs, loop = new { mode = Config.LoopUntilRopesEmpty ? "ropes" : "count", target = Config.MaxRuns, completed = runs, sessionOpen, rounds = roundResults.ToArray() }, decisions, reportPath, model = new { battles = ModelBattles, lastLoss = ModelLastLoss, kind = "游戏回执 + 本地推演", caveat = "逐角色输出以服务端战斗回执为准；伤害来源、掉落与装备仅在回执实际提供时统计。预测区间不包含模型误差。" }, dataPath = Paths.Data, recordedBattles = battleRecords.Count, skillBackup = File.Exists(Path.Combine(Paths.Data, "skills-backup.json")) };
    }
    public void Pause() { if (!Active) return; cancel?.Cancel(); phase = "正在暂停"; message = "等待当前操作回执后停止，不会再派发新操作。"; }
    public void Start(bool preview = false, LoopRequest? loop = null)
    {
        if (Active) throw new Exception("已有任务正在运行或等待回执。");
        var s = Bridge.Snapshot;
        if (!Bridge.Fresh(s) || !s.At("loggedIn").B()) throw new Exception("请先安装桥接、启动并登录游戏。");
        var nextConfig = loop == null ? Config : LoopPolicy.Configure(Config, loop);
        nextConfig.Validate();
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) throw new Exception("已有任务正在执行。");
        if (loop != null)
        {
            if (sessionOpen) { Report("切换循环设置，已保留当前深渊进度。", false); sessionOpen = false; }
            Config = nextConfig;
            J.Write(Paths.Config, Config);
        }
        cancel = new();
        if (!preview && !sessionOpen) { runs = 0; clearedFloors = 0; maxDepth = wins = losses = 0; coreAcquired = false; receipts.Clear(); battleRecords.Clear(); roundResults.Clear(); chapterReviews.Clear(); sessionBattles.Clear(); skippedReplays.Clear(); predictionErrors.Clear(); started = DateTimeOffset.Now; initialCores = s.At("sima","nucleos").N(); initialShards = s.At("sima","esquirlas").N(); finalCores = initialCores; finalShards = initialShards; roundInitialCores = initialCores; roundInitialShards = initialShards; sessionOpen = true; }
        worker = Task.Run(async () =>
        {
            try
            {
                var ct = cancel.Token;
                if (Config.PrepareSkills && !preview) await Prepare(ct);
                do
                {
                    ct.ThrowIfCancellationRequested();
                    await operation.WaitAsync(ct);
                    try { await Step(preview, ct); } finally { operation.Release(); }
                    if (preview) break;
                    if (Config.DelayMs > 0) await Task.Delay(Config.DelayMs, ct);
                } while (true);
            }
            catch (OperationCanceledException) { phase = "已暂停"; message = "当前操作已结束，可继续已有深渊。"; Note("自动流程已暂停"); }
            catch (FinishedException e) { phase = "已完成"; message = e.Message; Note(e.Message); Report(e.Message, e.Cleared); sessionOpen = false; }
            catch (UnknownOutcomeException e) { phase = "已暂停"; message = e.Message; Note(e.Message, "warning"); Report(e.Message, false); }
            catch (Exception e) { phase = "需要处理"; message = e.Message; Note(e.ToString(), "error"); Report(e.Message, false); }
            finally { Volatile.Write(ref running, 0); }
        });
    }
    async Task Step(bool preview, CancellationToken ct)
    {
        var s = Bridge.Snapshot;
        if (!Bridge.Fresh(s) || !s.At("loggedIn").B()) throw new Exception("游戏连接中断，自动流程已停止。");
        if (s.At("fighting").B())
        {
            phase = "战斗播放中"; message = "等待游戏播放结束。";
            var battleId = s.At("lastBattle", "time").S();
            if (Config.SkipBattleReplay && battleId != "" && !skippedReplays.Contains(battleId) && !preview)
            {
                var receipt = await Bridge.Send("local.skipReplay", new(), ct);
                if (receipt.At("skipped").B()) { skippedReplays.Add(battleId); Note("已跳过战斗播放，胜负仍以游戏结算为准。"); }
            }
            return;
        }
        phase = "读取实战模型";
        learning.EnsureLoaded(sim, ct);
        Learn(s);
        var run = s.At("sima", "run");
        maxDepth = Math.Max(maxDepth, (int)run.At("profundidad").N());
        coreAcquired |= run.At("reliquias").A().Concat(run.At("blasones").A()).Any(x => Cards.IsCore(x, Config));
        if (run != null && RevivalRules.Choice(run) == null && (!preview || Config.AvoidEliteIds.Length > 0))
        {
            var node = run.At("nodo").S();
            var needed = run.At("destinos").A().Select(x => x.S()).Append(node).Where(x => x != "").Distinct();
            foreach (var id in needed)
            {
                if (!NeedsPeek(s, run, id)) continue;
                ct.ThrowIfCancellationRequested();
                await Bridge.Send("sima.peek", new() { ["nodoId"] = id }, ct);
                s = Bridge.Snapshot!;
            }
        }
        var candidates = Build(s!);
        if (candidates.Length == 0) throw new Exception("当前房间没有已识别的合法操作，请在游戏中查看状态后继续。");
        phase = "本地推演中"; message = $"比较 {candidates.Length} 个候选，每候选 {Config.Simulations:N0} 次。";
        if (!OpeningRules.UsesSearch(Config, run) && run != null && !candidates.All(c => c.Method == "sima.tear")) Evaluate(candidates, s!, Config, ct);
        // Money has no reserve target. Compare a purchase with no purchase in
        // the same future encounter before allowing a shop exit to compete.
        var purchases = candidates.Where(c => c.Method == "sima.trade").ToArray();
        Forecast? noBuy = null;
        if (!OpeningRules.UsesSearch(Config, run) && purchases.Any(c => c.Card != null) && run != null)
        {
            var baseline = new Choice("不购买的战斗基线", "local.shopBaseline", new(), card: new JsonObject());
            Evaluate([baseline], s!, Config, ct);
            noBuy = baseline.Forecast;
        }
        if (OpeningRules.UsesSearch(Config, run))
        {
            phase = candidates.Length == 1 ? "核验唯一操作" : "树搜索中";
            message = candidates.Length == 1 ? "只有一个合法操作，跳过无分支搜索；继续核验预测与回执。"
                : $"本地搜索 {Config.Search.Budget(Config.StrategyId):N0} 次，最多 {Config.Search.Horizon} 步；可随时暂停。";
            if (candidates.Length > 1) learning.PrepareSearch();
            choices = new SearchPlanner(learning).Rank(candidates, s!, Config, sim, ct);
            // Forecast is a separate combat audit; it never reranks tree results.
            Evaluate([choices[0]], s!, Config, ct);
        }
        else choices = Rank(OpeningRules.UsesLegacy(Config, run) ? Legacy131.Selection.BeforeShopExit(candidates, noBuy) : BeforeShopExit(candidates, noBuy));
        var pick = choices[0];
        decisions++;
        if (preview) { phase = "推演完成"; message = "建议：" + pick.Label + "。未执行游戏操作。"; Note(message); return; }
        var fighting = pick.Method is "sima.fight" or "sima.open";
        var movingIntoFight = pick.Method == "sima.move" && run.At("mapa", "nodos", pick.Args.At("nodoId").S(), "tipo").S() is "combate" or "elite" or "jefe";
        if ((fighting || movingIntoFight) && pick.Forecast == null && Config.MinWinRate > 0)
            throw new Exception("抽样已关闭，无法验证最低胜率暂停线；请调整模拟次数或关闭此暂停线后继续。");
        if ((fighting || movingIntoFight) && pick.Forecast is { } f && f.Low < Config.MinWinRate)
        {
            throw new Exception($"预测胜率下界 {f.Low:P0} 低于自定义阈值，已暂停并保留深渊进度。撤离已禁止。");
        }
        ct.ThrowIfCancellationRequested();
        phase = "执行中"; message = pick.Label;
        Note(pick.Label + (pick.Reason == "" ? "" : "，" + pick.Reason) + (pick.Forecast is { } forecast ? $"，模型胜率 {forecast.Win:P1}，{forecast.Trials:N0} 次" : ""));
        var beforeLoot = run.At("botin");
        var beforeRun = s.At("sima", "run", "id").S();
        var beforeBattle = s.At("lastBattle", "time").S();
        string learningId = learning.Begin(s!, candidates, pick, Config);
        JsonNode outcome;
        try { outcome = await Bridge.Send(pick.Method, pick.Args, ct); }
        catch (Exception e) { learning.Abort(learningId, e is UnknownOutcomeException or OperationCanceledException ? "unknown" : "failed"); throw; }
        var after = Bridge.Snapshot;
        finalCores = after.At("sima","nucleos").N(initialCores); finalShards = after.At("sima","esquirlas").N(initialShards);
        string? verificationError = pick.Method == "sima.tear" && run != null
            ? RevivalRules.Error(run, after.At("sima", "run"), pick.Args.At("heroId").S())
            : pick.Method == "sima.guarda" && run != null && pick.Card != null
                ? Cards.InheritanceError(run, after.At("sima", "run"), pick.Card, Config) : null;
        if (verificationError != null) { learning.Abort(learningId, "unverified"); throw new Exception(verificationError); }
        learning.Confirm(learningId, after, outcome);
        Learn(after);
        maxDepth = Math.Max(maxDepth, (int)after.At("sima", "run", "profundidad").N());
        coreAcquired |= pick.Core;
        var bt = after.At("lastBattle", "time").S();
        if ((pick.Method is "sima.fight" or "sima.open") && bt != "" && bt != beforeBattle && sessionBattles.Add(bt))
        {
            var battle = after.At("lastBattle");
            var battleDirectory = Path.Combine(Paths.Data, "battles", started.ToString("yyyyMMdd-HHmmssfff"));
            battleRecords.Enqueue(BattleJournal.Capture(battle, run, after, runs + 1, Config.StrategyId, battleDirectory, battleRecords.Count + 1));
            var wonBattle = after.At("lastBattle", "result", "win").B();
            if (wonBattle) wins++; else losses++;
            if (pick.Forecast is { } p) predictionErrors.Add(Math.Pow(p.Win - (wonBattle ? 1 : 0), 2));
        }
        if (pick.Method == "sima.fight" && run.At("mapa", "nodos", run.At("nodo").S(), "tipo").S() == "jefe" && after.At("lastBattle", "result", "win").B()) clearedFloors++;
        if (pick.Method == "sima.tear" && run != null)
        {
            var error = RevivalRules.Error(run, after.At("sima", "run"), pick.Args.At("heroId").S());
            if (error != null) throw new Exception(error);
        }
        receipts.Enqueue(new { round = runs + 1, depth = (int)run.At("profundidad").N(), recovered = outcome.At("recovered").B(), time = DateTimeOffset.Now, pick.Method, pick.Args, pick.Label, prediction = pick.Forecast, priority = pick.Priority, reason = pick.Reason, cardId = pick.Card.At("id").S(), cardName = pick.Card == null ? "" : GameText.Name("relic", pick.Card.At("id").S(), J.Name(pick.Card)), cardDescription = GameText.Description("relic", pick.Card.At("id").S(), pick.Card.At("descZh").S(pick.Card.At("desc").S())), options = candidates.Where(c => c.Card != null).Select(c => new { id = c.Card.At("id").S(), c.Priority, c.Reason }).ToArray(), inheritance = pick.Method == "sima.guarda" ? new { fromDepth = run.At("profundidad").N(), toDepth = after.At("sima", "run", "profundidad").N(), before = Cards.Inherited(run!).Select(c => c.At("id").S()).ToArray(), after = after.At("sima", "run", "reliquias").A().Select(c => c.At("id").S()).ToArray() } : null, confirmed = true, equipmentLoot = DropJournal.ForCommand(after, outcome), obolsSpent = pick.Method == "sima.trade" ? Math.Max(0, beforeLoot.At("obolos").N() - after.At("sima", "run", "botin", "obolos").N()) : 0, loot = new { cores = Math.Max(0, after.At("sima", "run", "botin", "nucleos").N() - beforeLoot.At("nucleos").N()), shards = Math.Max(0, after.At("sima", "run", "botin", "esquirlas").N() - beforeLoot.At("esquirlas").N()) }, run = beforeRun, node = run.At("nodo").S(), nextRun = after.At("sima", "run", "id").S(), nextNode = after.At("sima", "run", "nodo").S() });
        PersistSession();
        if (pick.Method == "sima.tear" && run != null)
        {
            Note("复活已核验：" + pick.Label + "；剩余眼泪 " + after.At("sima", "run", "lagrimas").N() + "，继续检查其他倒地队员。");
        }
        if (pick.Method == "sima.guarda" && run != null && pick.Card != null)
        {
            var error = Cards.InheritanceError(run, after.At("sima", "run"), pick.Card, Config);
            if (error != null) throw new Exception(error);
            if (Config.StrategyId == 4 && Cards.Inherited(run).Count(c => c.At("id").S() == pick.Card.At("id").S()) >= 3)
                Note($"继承已核验：P{run.At("profundidad").N()} → P{after.At("sima", "run", "profundidad").N()}，遗物保留；{CardCatalog.Name(pick.Card)}已达三张上限，本次正常过渡，没有增加张数。");
            else
            Note($"继承已核验：P{run.At("profundidad").N()} → P{after.At("sima", "run", "profundidad").N()}，已继承遗物保留，新增 {pick.Card.At("id").S()}。" );
            phase = "章节重新推演";
            var review = ReviewChapter(after!, ct);
            choices = [review];
            chapterReviews.Add(new { time = DateTimeOffset.Now, depth = after.At("sima", "run", "profundidad").N(),
                relics = after.At("sima", "run", "reliquias").A().Select(c => c.At("id").S()).ToArray(),
                freezeInherited = CardRules.HasInheritedCore(after.At("sima", "run"), Config), prediction = review.Forecast, review.Reason });
            Note(review.Reason + (review.Forecast is { } rf ? $"；下一章普通房间先验推演 {rf.Trials:N0} 次，估计胜率 {rf.Win:P1}（非真实战绩）" : "；抽样关闭，胜率未知"));
            PersistSession();
        }
        if (pick.Method == "sima.trade") completed.Add(beforeRun + ":" + run.At("nodo").S() + ":" + pick.Args.At("oferta").S());
        if (beforeRun != "" && Bridge.Snapshot.At("sima", "run") == null)
        {
            // The final battle can end the run before its replay finishes.
            if (Config.SkipBattleReplay && !ct.IsCancellationRequested)
                for (int i = 0; i < 10 && Bridge.Snapshot.At("fighting").B(); i++)
                {
                    var skipped = await Bridge.Send("local.skipReplay", new(), CancellationToken.None);
                    if (skipped.At("skipped").B()) { skippedReplays.Add(bt); Note("已跳过最终战斗播放，保留实际结算结果。"); break; }
                    await Task.Delay(250);
                }
            learning.FinishRun(beforeRun, run.At("profundidad").N(), Config);
            runs++;
            var won = after.At("lastBattle", "result", "win").B();
            roundResults.Add(new { number = runs, won, depth = (int)run.At("profundidad").N(), ended = DateTimeOffset.Now,
                coreDelta = finalCores - roundInitialCores, shardDelta = finalShards - roundInitialShards });
            roundInitialCores = finalCores; roundInitialShards = finalShards;
            PersistSession();
            Report($"第 {runs} 次下潜{(won ? "通关" : "战败")}，已完成结算。", won);
            var stop = LoopPolicy.Stop(Config, runs, after.At("sima", "cuerdas").N());
            if (stop != null) throw new FinishedException(stop, won);
            choices = [];
            phase = "循环待入场"; message = $"已完成 {runs} 次下潜，准备下一次；剩余绳索 {after.At("sima", "cuerdas").N()}。";
            Note(message);
        }
    }
    internal Choice[] Build(JsonNode s)
    {
        var candidates = BuildActions(s);
        var run = s.At("sima", "run");
        var skipped = PactEligible(run, Config) ? PactRules.SkipReason(run.At("pacto"), Config) : null;
        if (skipped != null)
            foreach (var choice in candidates.Where(c => c.Method is "sima.fight" or "sima.open"))
                choice.Reason = string.Join("；", new[] { choice.Reason, "不缔约：" + skipped }.Where(x => x != ""));
        return candidates;
    }
    Choice[] BuildActions(JsonNode s)
    {
        var run = s.At("sima", "run");
        if (run == null)
        {
            if (!Config.AutoEnter) throw new FinishedException("当前没有深渊，自动入场已关闭。");
            var stop = LoopPolicy.Stop(Config, runs, s.At("sima", "cuerdas").N());
            if (stop != null) throw new FinishedException(stop);
            if (!s.At("sima", "puedeEntrar").B()) throw new Exception(s.At("sima", "motivo").S("游戏当前不允许进入深渊。"));
            var heroes = Team(s);
            return [new("进入深度 " + Config.StartDepth, "sima.enter", new() { ["profundidad"] = Config.StartDepth, ["tramo"] = "t100", ["heroes"] = new JsonArray(heroes.Select(x => JsonValue.Create(x.At("id").S())).ToArray<JsonNode?>()), ["ordenado"] = true })];
        }
        var offers = run.At("blasonPendiente", "opciones").A().ToArray();
        if (offers.Length > 0) return CardChoices(offers, "sima.blason", run);
        offers = run.At("reliquiaOferta").A().ToArray();
        if (offers.Length > 0) return CardChoices(offers, "sima.reliquia", run);
        offers = run.At("guardaPendiente", "cartas").A().ToArray();
        if (offers.Length > 0) return CardChoices(offers, "sima.guarda", run);
        if (Config.ClearFloors > 0 && clearedFloors >= Config.ClearFloors) throw new FinishedException($"已通关 {clearedFloors} 层并完成遗物继承，已暂停保留深渊进度。");
        var id = run.At("nodo").S();
        var nd = run.At("mapa", "nodos", id);
        var type = nd.At("tipo").S();
        var done = run.At("hechos").A().Any(x => x.S() == id);
        if (Config.StopDepth > 0 && run.At("profundidad").N() >= Config.StopDepth) throw new FinishedException("已到达自定义深度，已暂停保留深渊进度。");
        var heroesInRun = run.At("heroes").A().ToArray();
        var low = heroesInRun.Where(h => !h.At("caido").B()).Any(h => h.At("hp").N() / Math.Max(1, h.At("hpMax").N()) < Config.RetreatHp);
        var revival = RevivalRules.Choice(run);
        if (revival != null) return [revival];
        if (low && type != "santuario")
        {
            throw new Exception("生命低于自定义暂停线，已暂停并保留深渊进度。撤离已禁止。");
        }
        if (OpeningRules.UsesSearch(Config, run)) return SearchWorld.LegalActions(s, Config);
        if (!done && ShouldPact(run, Config) && (type is "combate" or "elite" or "jefe" || type == "osario" && nd.At("emboscada").B()))
            return [new("有冻结，缔结契约：" + J.Name(run.At("pacto")), "sima.pacto", new(), .5, .4)];
        if (type == "presagio" && !done)
        {
            return EventRules.Choices(s);
        }
        if (type == "santuario" && !done)
        {
            return SanctuaryChoices(s, run);
        }
        if (type == "chatarrero")
        {
            var obols = run.At("botin", "obolos").N();
            var bought = run.At("compras").A().Select(x => x.S()).ToHashSet();
            var shop = new List<Choice>();
            foreach (var service in run.At("servicios").A())
            {
                var offer = service.At("id").S();
                if (service.At("obolos").N() > obols || bought.Contains(id + ":" + offer) || completed.Contains(run.At("id").S() + ":" + id + ":" + offer)) continue;
                var b = SupplyBenefit(run, offer);
                if (b > .0001) shop.Add(new("商店：" + J.Name(service), "sima.trade", new() { ["oferta"] = offer }, b));
            }
            if (run.At("tienda", "precio").N(double.MaxValue) <= obols)
            {
                if (OpeningRules.UsesLegacy(Config, run)) shop.AddRange(Legacy131.Selection.ShopCards(run, Config, completed));
                else
                {
                var shopOffers = run.At("tienda", "buffs").A().Where(CardRules.Available).Where(card =>
                {
                    var bid = "buff:" + card.At("id").S();
                    return !bought.Contains(id + ":" + bid) && !completed.Contains(run.At("id").S() + ":" + id + ":" + bid);
                }).ToArray();
                if (shopOffers.Length > 0)
                foreach (var choice in CardChoices(shopOffers, "sima.trade", run))
                    shop.Add(new("商店：" + choice.Label, "sima.trade", new() { ["oferta"] = "buff:" + choice.Card.At("id").S() },
                        choice.Benefit, choice.Risk, choice.Core, choice.Priority, choice.Card) { Reason = choice.Reason });
                }
            }
            if (shop.Count > 0) { foreach (var dest in run.At("destinos").A()) shop.Add(RouteChoice(run, dest.S(), Config, s)); return shop.ToArray(); }
        }
        if (!done && type is "combate" or "elite" or "jefe") return FightChoices(s, run, id);
        if (!done && type == "osario")
        {
            return [new("打开骨库", "sima.open", new(), .6, nd.At("emboscada").B() ? .8 : 0)];
        }
        var destinations = run.At("destinos").A().Select(x => x.S()).Where(x => x != "").ToArray();
        if (destinations.Length == 0) throw new Exception("当前无可达房间，已停止，避免重复操作。");
        var moves = new List<Choice>();
        foreach (var dest in destinations)
        {
            var node = run.At("mapa", "nodos", dest); var kind = node.At("tipo").S();
            if (kind == "elite" && !Config.AllowElite || node.At("emboscada").B() && !Config.AllowAmbush) continue;
            moves.Add(RouteChoice(run, dest, Config, s));
        }
        // If every path has a normally avoided room, continue along the least risky
        // legal path. A route preference must never turn into an extract command.
        if (moves.Count == 0) foreach (var dest in destinations) moves.Add(RouteChoice(run, dest, Config, s));
        return moves.ToArray();
    }
    internal static Choice[] SanctuaryChoices(JsonNode snapshot, JsonNode run)
    {
        var result = new List<Choice>();
        var members = run.At("heroes").A().ToArray();
        var alive = members.Where(h => !h.At("caido").B()).ToArray();
        var sant = run.At("santuario");
        bool down = members.Any(h => h.At("caido").B());
        if (down && sant.At("levanta").B())
            result.Add(new("圣所：复活", "sima.rest", new() { ["opcion"] = "levantar" }, 2, priority: 40) { Reason = "先恢复倒地队员" });
        if (alive.Length == 0) return result.ToArray();
        var ratios = alive.Select(h => Math.Clamp(h.At("hp").N() / Math.Max(1, h.At("hpMax").N()), 0, 1)).ToArray();
        var anima = alive.Select(h => Math.Clamp(h.At("anima").N(100), 0, 100)).ToArray();
        bool healthy = !down && ratios.Min() >= .8 && ratios.Average() >= .9;
        bool ready = anima.Min() >= 50 && anima.Average() >= 70;
        bool urgentHeal = ratios.Min() < .6 || ratios.Average() < .75;
        if (sant.At("reposarPct").N() > 0)
            result.Add(new("圣所：恢复生命", "sima.rest", new() { ["opcion"] = "reposar" }, 1 - ratios.Average(), priority: urgentHeal ? 30 : 0)
            { Reason = urgentHeal ? "存在明显生命缺口，优先补给" : "生命缺口与下一战收益比较" });
        result.Add(new("圣所：恢复魂力", "sima.rest", new() { ["opcion"] = "meditar" }, (100 - anima.Average()) / 180, priority: healthy && !ready ? 25 : 0)
        { Reason = healthy && !ready ? "魂力偏低，先补足技能启动资源" : "魂力的小缺口不压过可保留的磨利强化" });
        foreach (var hero in alive)
        {
            var live = snapshot.At("heroes").A().FirstOrDefault(h => h.At("id").S() == hero.At("id").S());
            double potential = Simulation.SharpenPotential(live);
            result.Add(new("圣所：磨利强化 " + J.Name(hero), "sima.rest", new() { ["opcion"] = "afilar", ["heroId"] = hero.At("id").S() },
                Math.Min(.8, potential * .08), priority: healthy && ready && potential > 0 ? 20 : 0)
            { Reason = "磨利每层增加此英雄 8% 技能伤害并随下潜保留；优先有伤害技能的队员，普攻不受益" });
        }
        return result.ToArray();
    }
    internal Choice[] CardChoices(JsonNode[] cards, string method, JsonNode run)
    {
        if (!OpeningRules.Complete(run, Config)) return OpeningRules.Choices(cards, method, run, Config);
        if (Config.StrategyId >= 5) return SearchWorld.CardsFor(cards, method, run, Config);
        if (Config.StrategyId == 4) return CustomStrategy.Choices(cards, method, run, Config);
        if (Config.StrategyId == 2) return Legacy131.Selection.CardChoices(cards, method, run, Config);
        if (!SynergySelection.OpeningComplete(run, Config, method)) return SynergySelection.Opening(cards, method, run, Config);
        var offered = cards.Where(CardRules.Available).ToArray();
        if (offered.Length == 0)
        {
            if (method == "sima.trade") return [];
            throw new Exception("游戏没有实际可选的卡牌，等待状态更新；不会派发不可用选项。");
        }
        if (method == "sima.guarda")
        {
            var uncapped = offered.Where(c => c.At("llevas").N() < 3 && Cards.Inherited(run).Count(x => x.At("id").S() == c.At("id").S()) < 3).ToArray();
            if (uncapped.Length > 0) offered = uncapped;
        }
        cards = offered.Where(c => SynergySelection.SkipReason(c, method, run, Config) == null).ToArray();
        bool fallback = cards.Length == 0;
        if (fallback)
        {
            if (method == "sima.trade") return [];
            cards = SynergySelection.PreferSoftFallback(offered, method, run, Config);
        }
        var action = method == "sima.guarda" ? "带走遗物：" : method == "sima.blason" ? "选择本章徽章（达门槛可保留）："
            : Config.StrategyId == 3 && method == "sima.trade" ? "购买赐福（不计遗物）：" : "选择可继承遗物：";
        var choices = cards.Select(card =>
        {
            int penalty = fallback ? SynergySelection.SoftPenalty(card, method, run, Config) : 0;
            var skipReason = SynergySelection.SkipReason(card, method, run, Config);
            var reason = fallback ? "常规策略无候选，按当前构筑择优兜底，不因偏好暂停；原避让：" + skipReason
                + $"；兜底扣分{penalty}；" : "比较单卡新增收益；";
            return new Choice((fallback ? "策略兜底：" : "") + (Cards.IsCore(card, Config) ? "普攻冻结核心：" : action) + J.Name(card),
                method, new() { ["id"] = card.At("id").S() }, 0, 0, Cards.IsCore(card, Config), 0, card) { Reason = reason };
        }).ToArray();
        return SynergySelection.Apply(choices, method, run, Config);
    }
    internal Choice ReviewChapter(JsonNode snapshot, CancellationToken ct)
    {
        var run = snapshot.At("sima", "run") ?? throw new Exception("章节复核缺少游戏状态。");
        var review = new Choice("章节起点构筑复核", "local.chapterReview", new(), card: new JsonObject())
        { Reason = $"P{run.At("profundidad").N()}按实际保留构筑重新推演：遗物{run.At("reliquias").A().Count()}张、徽章{run.At("blasones").A().Count()}张；"
            + IceStrategyRules.Description(Config.StrategyId) };
        Evaluate([review], snapshot, Config, ct);
        return review;
    }
    internal static Choice[] BeforeShopExit(Choice[] candidates, Forecast? baseline)
    {
        var buys = candidates.Where(c => c.Method == "sima.trade" && (c.Card == null || c.Priority > 0 ||
            baseline != null && c.Forecast != null && c.Forecast.Score >= baseline.Score - .0001)).ToArray();
        var exits = candidates.Where(c => c.Method != "sima.trade").ToArray();
        return buys.Length > 0 ? buys : exits.Length > 0 ? exits : candidates;
    }
    internal static double SupplyBenefit(JsonNode run, string offer)
    {
        if (offer == "esquirla") return 1;
        var alive = run.At("heroes").A().Where(h => !h.At("caido").B()).ToArray();
        if (alive.Length == 0) return 0;
        return offer == "vida" ? 1 - alive.Average(h => h.At("hp").N() / Math.Max(1, h.At("hpMax").N()))
            : offer == "anima" ? (100 - alive.Average(h => h.At("anima").N())) / 100 : 0;
    }
    internal static bool RewardPhase(JsonNode run, Settings cfg) => cfg.FarmWithFreeze && cfg.RewardFloors > 0
        && run.At("profundidad").N() < cfg.RewardFloors && Cards.Current(run).Any(c => Cards.IsCore(c, cfg));
    static double RemainingFights(JsonNode run,string id,HashSet<string> visited)
    {
        if(!visited.Add(id))return 0;
        var nd=run.At("mapa","nodos",id);var next=nd.At("salidas").A().Select(x=>x.S()).ToArray();
        double own=nd.At("tipo").S() is "combate" or "elite" or "jefe" || nd.At("emboscada").B()?1:0;
        return own+(next.Length==0?0:next.Min(x=>RemainingFights(run,x,new HashSet<string>(visited))));
    }
    internal static Choice RouteChoice(JsonNode run, string dest, Settings cfg, JsonNode? snapshot = null)
    {
        var node = run.At("mapa", "nodos", dest); var kind = node.At("tipo").S();
        bool battle = kind is "combate" or "elite" or "jefe" || kind == "osario" && node.At("emboscada").B();
        bool farm = RewardPhase(run, cfg);
        // These weights express a route preference. Drop probabilities are not
        // exposed by the server and are never presented as guaranteed resources.
        double benefit = kind switch { "santuario" => .8, "osario" => .4, "chatarrero" => .3, _ => .05 };
        if (farm && battle) benefit += cfg.ShardRewardWeight + (kind is "elite" or "jefe" ? cfg.CoreRewardWeight : 0);
        bool avoid = cfg.AvoidCombatAfterRewardFloors && !farm;
        double risk = battle ? avoid ? 4 : .15 : 0;
        if (avoid) risk += RemainingFights(run,dest,new HashSet<string>()) * 1.5;
        string mode = farm ? "核心/碎屑奖励" : avoid ? "避战路线" : "常规路线";
        var avoided = EliteRules.Avoided(snapshot.At("peeks", dest), cfg);
        // A finite penalty competes with survival and rewards. Never delete a
        // legal route or pause just because it contains a selected elite.
        if (avoided.Length > 0) risk += 40;
        return new($"{mode}：{dest} · {kind}", "sima.move", new() { ["nodoId"] = dest }, benefit, risk)
        {
            Reason = avoided.Length == 0 ? "" : "优先避让：" + string.Join("、", avoided.Select(e => e.Name)) + "；必经或明显更安全时仍可前进"
        };
    }
    internal static Choice[] Rank(Choice[] candidates) => candidates.OrderByDescending(c => c.Priority).ThenByDescending(c => c.Forecast?.Score ?? c.Benefit * 6 - c.Risk * 3).ToArray();
    internal static bool PactEligible(JsonNode? run, Settings cfg) => run != null && cfg.AutoPactWithFreeze && cfg.PactFloors > 0
        && run.At("profundidad").N() < cfg.PactFloors && run.At("pacto") is JsonObject
        && !run.At("pacto", "sellado").B()
        && run.At("reliquias").A().Concat(run.At("blasones").A()).Any(c => Cards.IsCore(c, cfg));
    internal static bool ShouldPact(JsonNode run, Settings cfg) => PactEligible(run, cfg)
        && PactRules.SkipReason(run.At("pacto"), cfg) == null;
    internal static bool NeedsPeek(JsonNode? s, JsonNode run, string id)
    {
        var peek = s.At("peeks", id);
        if (peek == null) return true;
        // A mist event can replace its current room with a combat encounter.
        // The previously cached empty preview is then no longer authoritative.
        return id == run.At("nodo").S()
            && run.At("mapa", "nodos", id, "tipo").S() is "combate" or "elite" or "jefe"
            && !peek.At("enemigos").A().Any();
    }
    internal Choice[] FightChoices(JsonNode s, JsonNode run, string id) => OpeningRules.UsesLegacy(Config, run) ? Legacy131.Selection.FightChoices(s, run, id) : TargetRules.Choices(s, run, id, Config.SeparateTargets);
    JsonNode[] Team(JsonNode s)
    {
        return FormationRules.ResolveHeroes(Config.Formation, s);
    }
    public async Task Prepare(CancellationToken ct)
    {
        var s = Bridge.Snapshot!; var members = Team(s);
        var path = Path.Combine(Paths.Data, "skills-backup.json");
        if (!File.Exists(path)) J.Write(path, members.Select(h => new { heroId = h.At("id").S(), skills = h.At("skills").A().Where(x => x.At("equipped").B()).Select(x => x.At("id").S()).ToArray() }));
        foreach (var h in members)
        {
            var safe = h.At("skills").A().Where(k => k.At("known").B() && (k.At("element").S() is "ice" or "physical" || !k.At("damages").B() && (k.At("heals").B() || k.At("target").S().Contains("self") || k.At("target").S().Contains("ally")))).OrderBy(k => k.At("element").S() == "ice" ? 0 : k.At("equipped").B() ? 1 : 2).Take((int)h.At("skillSlots").N(4)).Select(k => JsonValue.Create(k.At("id").S())).ToArray<JsonNode?>();
            await Bridge.Send("hero.setSkills", new() { ["heroId"] = h.At("id").S(), ["skills"] = new JsonArray(safe) }, ct);
        }
        Note("可选的技能预设已应用，原槽位已备份。");
    }
    public async Task Restore()
    {
        if (Active) throw new Exception("请先暂停并等待当前操作结束。");
        await operation.WaitAsync();
        try
        {
            var path = Path.Combine(Paths.Data, "skills-backup.json"); var backup = J.Read(path) ?? throw new Exception("没有技能备份。");
            var ids = Bridge.Snapshot.At("heroes").A().Select(h => h.At("id").S()).ToHashSet();
            if (backup.A().Any(x => !ids.Contains(x.At("heroId").S()))) throw new Exception("备份与当前角色不匹配，未写入。");
            foreach (var h in backup.A()) await Bridge.Send("hero.setSkills", (JsonObject)h.DeepClone(), CancellationToken.None);
            File.Move(path, path + ".restored-" + DateTime.Now.ToString("yyyyMMddHHmmss")); Note("技能槽已恢复。");
        }
        finally { operation.Release(); }
    }
    void PersistSession() => J.Write(Path.Combine(Paths.Data, "last-session.json"), new { started, config = Config, runs,
        roundResults, chapterReviews, maxDepth, clearedFloors, wins, losses, coreAcquired, battleRecords,
        resources = new { initialCores, initialShards, finalCores, finalShards }, receipts });
    void Report(string outcome, bool cleared)
    {
        reportPath = Path.Combine(Paths.Data, "reports", DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var text = new System.Text.StringBuilder();
        text.AppendLine("# IdleLegions自动下井实测报告\n");
        text.AppendLine($"结果：{outcome}\n\n游戏确认整轮结束且获胜：{(cleared ? "是" : "否")}\n\n开始：{started:yyyy-MM-dd HH:mm:ss}\n\n结束：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}\n");
        text.AppendLine($"本次确认操作：{receipts.Count}；实战模型已采集：{ModelBattles} 场；每候选本地推演：{Config.Simulations:N0} 次。\n");
        text.AppendLine($"结算资源净变化：核心 {finalCores-initialCores:+0;-0;0}，碎屑 {finalShards-initialShards:+0;-0;0}。暂存战利品与实际结算资源分开统计。\n");
        text.AppendLine($"最高到达深度：{maxDepth}；击败层首领：{clearedFloors}；本段战斗：{wins} 胜 / {losses} 负；普攻冻结核心：{(coreAcquired ? "已取得" : "本段未取得")}。\n");
        text.AppendLine(ResourceJournal.Report(receipts, roundResults.ToArray()));
        text.AppendLine(BattleJournal.Report(battleRecords));
        text.AppendLine(BattleJournal.AcquisitionReport(receipts));
        text.AppendLine(DropJournal.Report(receipts));
        text.AppendLine($"## 本段策略\n\n策略 {Config.StrategyId}：{IceStrategyRules.Name(Config.StrategyId)}。策略二源码来自 v1.3.1，SHA-256 `{LegacyStrategySource.SourceHash}`。\n");
        if (predictionErrors.Count > 0) text.AppendLine($"本段预测 Brier 分数：{predictionErrors.Average():F4}（越低越好，样本 {predictionErrors.Count} 场）。这只衡量这批已选动作，不能验证未选动作的反事实胜率。\n");
        text.AppendLine("全程禁止撤离。" + IceStrategyRules.Description(Config.StrategyId) + "\n");
        text.AppendLine($"独立普攻开关：点燃 {Config.AllowBasicBurn}，带电 {Config.AllowBasicLightning}。纯电清除冻结为模型假设：{Config.LightningConsumesFreeze}，服务端规则未确认。跳过战斗播放：{Config.SkipBattleReplay}；本段跳过 {skippedReplays.Count} 次。所有操作等待回执，超时先核验，确认未发出才重发，最多重试2次。\n");
        text.AppendLine($"前期护盾避让：{Config.SkipShieldCardsEarly}，前 {Config.ShieldSkipFloors} 层；无合适候选可兜底选盾，已持有护盾不移除。\n");
        text.AppendLine($"契约避让：前8次受击免疫 {Config.SkipEightHitPact}，15%闪避 {Config.SkipDodgePact}；跳过契约后仍正常战斗，已签约的效果不会撤销。\n");
        text.AppendLine($"奖励路线：有冻结核心且处于前 {Config.RewardFloors} 层时优先战斗，核心资源权重 {Config.CoreRewardWeight}、碎屑权重 {Config.ShardRewardWeight}；之后优先避战：{Config.AvoidCombatAfterRewardFloors}。必经战斗继续执行。\n");
        text.AppendLine("本地预测基于客户端事件与未知属性先验，不是服务端精确战斗模拟。抽样置信区间不包含模型误差，不能宣称全局最高真实胜率。\n");
        text.AppendLine($"循环模式：{(Config.LoopUntilRopesEmpty ? "直到绳索用完（保留0）" : $"循环{Config.MaxRuns}次")}；已完成 {runs} 次。\n");
        foreach (var result in roundResults) { var r = JsonSerializer.SerializeToNode(result, J.Options); text.AppendLine($"第 {r.At("number").N()} 次：{(r.At("won").B() ? "通关" : "战败")}，P{r.At("depth").N()}，核心 {r.At("coreDelta").N():+0;-0;0}、碎屑 {r.At("shardDelta").N():+0;-0;0}。\n"); }
        text.AppendLine("## 章节继承后重新推演\n\n仅使用游戏确认保留的构筑；预测不是实战结果。\n");
        foreach (var review in chapterReviews)
        {
            var r = JsonSerializer.SerializeToNode(review, J.Options);
            text.AppendLine($"- P{r.At("depth").N()}：{r.At("reason").S()}；胜率估计 {(r.At("prediction") == null ? "未知" : r.At("prediction", "win").N().ToString("P1"))}。");
        }
        text.AppendLine("## 确认过的操作\n\n| 时间 | 操作 | 结果 |\n|---|---|---|");
        foreach (var r in receipts) { var n = JsonSerializer.SerializeToNode(r, J.Options); text.AppendLine($"| {DateTimeOffset.Parse(n.At("time").S()):HH:mm:ss} | {GameText.Text(n.At("label").S()).Replace("|", "/")} | {(n.At("recovered").B() ? "超时后游戏状态核验完成" : "服务端成功回执")} |"); }
        text.AppendLine("\n详细数据位于同目录上级的 last-session.json、battles/ 与 pilot-日期.jsonl。\n");
        File.WriteAllText(reportPath, text.ToString(), new System.Text.UTF8Encoding(false));
    }
    sealed class FinishedException(string text, bool cleared = false) : Exception(text) { public bool Cleared { get; } = cleared; }
}
