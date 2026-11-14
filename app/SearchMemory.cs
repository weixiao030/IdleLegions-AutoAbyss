using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

sealed class LearningDecision
{
    public string Id { get; set; } = "";
    public string RunId { get; set; } = "";
    public string EpisodeId { get; set; } = "";
    public string NextRunId { get; set; } = "";
    public JsonNode? Before { get; set; }
    public JsonNode? After { get; set; }
    public JsonNode? Receipt { get; set; }
    public DateTimeOffset Time { get; set; }
    public int Strategy { get; set; }
    public double Depth { get; set; }
    public string Node { get; set; } = "";
    public string Method { get; set; } = "";
    public JsonObject Args { get; set; } = new();
    public double[][] Features { get; set; } = [];
    public double[]? Policy { get; set; }
    public int Chosen { get; set; }
    public bool Confirmed { get; set; }
    public string Outcome { get; set; } = "pending";
    public double? Return { get; set; }
    public double? FinalDepth { get; set; }
    public double? PredictedWin { get; set; }
    public bool? Won { get; set; }
    public string CalibrationKey { get; set; } = "";
    public double[] StateFeatures { get; set; } = [];
}

sealed class SearchMemory
{
    readonly string root = Path.Combine(Paths.Data, "learning-v1");
    readonly Dictionary<string, JsonNode> cards = [];
    readonly Dictionary<string, JsonNode> maps = [];
    readonly Dictionary<string, JsonNode> shops = [];
    readonly Dictionary<string, LearningDecision> pending = [];
    readonly Dictionary<string, string> episodes = [];
    readonly HashSet<string> finishedEpisodes = [];
    readonly Dictionary<string, (int Count, double Predicted, double Actual)> calibration = [];
    internal sealed record BattleCheckpoint(HistoryCache.Stamp[] Files, Simulation.State Model,
        Dictionary<string, JsonNode> Cards, Dictionary<string, JsonNode> Maps, Dictionary<string, JsonNode> Shops,
        string[] Seen, int Archived, double LastTime);
    internal sealed class DecisionInfo
    {
        public string Id { get; set; } = "";
        public string RunId { get; set; } = "";
        public string EpisodeId { get; set; } = "";
        public string NextRunId { get; set; } = "";
        public bool Confirmed { get; set; }
        public double? Return { get; set; }
        public double? PredictedWin { get; set; }
        public bool? Won { get; set; }
        public string CalibrationKey { get; set; } = "";
    }
    internal sealed record IndexedDecision(HistoryCache.Stamp File, DecisionInfo? Info);
    internal sealed record DecisionCheckpoint(IndexedDecision[] Rows);
    readonly Dictionary<string, IndexedDecision> decisionIndex = new(StringComparer.OrdinalIgnoreCase);
    bool loaded;
    int archived, decisions, confirmed;
    volatile object summary = new { loaded = false };
    internal void RefreshStatus() => summary = new { loaded, archivedBattles = archived, decisions, confirmed, pending = pending.Count,
        cards = cards.Count, mapTemplates = maps.Count, calibrationContexts = calibration.Count, completedEpisodes = finishedEpisodes.Count };
    internal NeuralLearner Neural { get; }
    internal int Version => Neural.Version;
    internal SearchMemory(string? directory = null) { if (directory != null) root = directory; Neural = new(root); }
    static string Digest(JsonNode n) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(n.ToJsonString())));
    internal static JsonNode Slim(JsonNode s)
    {
        var ids = s.At("sima", "run", "heroes").A().Select(h => h.At("id").S()).ToHashSet();
        return new JsonObject
        {
            ["sima"] = new JsonObject { ["run"] = s.At("sima", "run")?.DeepClone() },
            ["heroes"] = new JsonArray(s.At("heroes").A().Where(h => ids.Contains(h.At("id").S())).Select(h => h.DeepClone()).ToArray()),
            ["peeks"] = s.At("peeks")?.DeepClone() ?? new JsonObject(), ["omen"] = s.At("omen")?.DeepClone(),
            ["lastBattle"] = new JsonObject { ["time"] = s.At("lastBattle", "time")?.DeepClone() }
        };
    }
    internal void Observe(JsonNode? run)
    {
        if (run == null) return;
        foreach (var c in Cards.Current(run).Concat(run.At("reliquiaOferta").A()).Concat(run.At("guardaPendiente", "cartas").A()).Concat(run.At("tienda", "buffs").A()))
            if (c.At("id").S() != "" && c.At("desc").S() != "") cards[c.At("id").S()] = c.DeepClone();
        foreach (var c in run.At("blasonPendiente", "opciones").A())
        {
            var card = c.DeepClone(); card["familia"] = run.At("blasonPendiente", "familia")?.DeepClone(); card["umbral"] = run.At("blasonPendiente", "umbral")?.DeepClone();
            if (card.At("id").S() != "" && card.At("desc").S() != "") cards[card.At("id").S()] = card;
        }
        if (run.At("mapa", "nodos") is JsonObject nodes && nodes.Count > 0)
        {
            // Save topology only. Avoid per-battle HP, purchases and completed rooms.
            var shape = new JsonObject();
            foreach (var (id, node) in nodes)
                shape[id] = new JsonObject { ["tipo"] = node.At("tipo")?.DeepClone(), ["fila"] = node.At("fila")?.DeepClone(),
                    ["salidas"] = node.At("salidas")?.DeepClone(), ["emboscada"] = node.At("emboscada")?.DeepClone() };
            var map = new JsonObject { ["nodos"] = shape, ["sourceDepth"] = run.At("profundidad").N(), ["santuario"] = run.At("santuario")?.DeepClone(), ["servicios"] = run.At("servicios")?.DeepClone(), ["pacto"] = run.At("pacto")?.DeepClone() }; maps.TryAdd(Digest(map), map);
        }
        if (run.At("tienda", "buffs").A().Any()) { var shop = run.At("tienda")!.DeepClone(); shops.TryAdd(Digest(shop), shop); }
    }
    internal void EnsureLoaded(Simulation? allDataModel, CancellationToken ct)
    {
        if (loaded) return;
        ct.ThrowIfCancellationRequested();
        string directory = Path.Combine(Paths.Data, "battles"); var seen = new HashSet<string>();
        var files = HistoryCache.Scan(directory, SearchOption.AllDirectories);
        string cacheFile = Path.Combine(root, "cache", "battle-model-v1.json");
        bool cacheable = allDataModel?.Battles == 0;
        var saved = cacheable ? HistoryCache.Read<BattleCheckpoint>(cacheFile) : null;
        if (saved?.Files == null || saved.Model == null || saved.Cards == null || saved.Maps == null || saved.Shops == null || saved.Seen == null) saved = null;
        var additions = saved == null ? null : HistoryCache.Additions(saved.Files, files);
        bool restored = saved != null && additions != null;
        double lastTime = restored ? saved!.LastTime : double.MinValue;
        cards.Clear(); maps.Clear(); shops.Clear();
        void Restore()
        {
            allDataModel!.RestoreState(saved!.Model);
            foreach (var pair in saved.Cards) cards[pair.Key] = pair.Value;
            foreach (var pair in saved.Maps) maps[pair.Key] = pair.Value;
            foreach (var pair in saved.Shops) shops[pair.Key] = pair.Value;
            seen.UnionWith(saved.Seen); archived = saved.Archived;
        }
        if (restored) Restore(); else archived = 0;
        var battleFiles = (restored ? additions! : files).Select(f => f.Path);
        var ordered = new List<(string File, double Time)>();
        foreach (var path in battleFiles)
        {
            ct.ThrowIfCancellationRequested(); var stored = J.Read(path); var b = stored.At("authoritativeBattle") ?? stored;
            string key = b.At("commandId").S(b.At("time").S()) + ":" + b.At("before", "id").S();
            if (b.At("before") == null || b.At("result") == null || !seen.Add(key)) continue;
            double time = b.At("time").N();
            // A backfilled older battle changes chronological smoothing. Rebuild
            // rather than silently applying it after a newer checkpoint.
            if (restored && time < lastTime)
            {
                allDataModel!.RestoreState(new([], new(), [], 0, .18));
                HistoryCache.Write(cacheFile, new { invalidated = true });
                EnsureLoaded(allDataModel, ct); return;
            }
            Observe(b.At("before")); ordered.Add((path, time));
        }
        archived += ordered.Count;
        if (allDataModel != null)
            foreach (var (file, _) in ordered.OrderBy(x => x.Time))
            {
                ct.ThrowIfCancellationRequested(); var stored = J.Read(file); var b = stored.At("authoritativeBattle") ?? stored;
                allDataModel.Learn(new JsonObject { ["lastBattle"] = b?.DeepClone() }, false);
            }
        if (cacheable && (!restored || ordered.Count > 0 || additions!.Length > 0))
        {
            ct.ThrowIfCancellationRequested();
            HistoryCache.Write(cacheFile, new BattleCheckpoint(files, allDataModel!.CaptureState(), cards, maps, shops,
                seen.ToArray(), archived, Math.Max(lastTime, ordered.Select(x => x.Time).DefaultIfEmpty(double.MinValue).Max())));
        }
        var corpus = Path.Combine(root, "decisions");
        decisions = confirmed = 0; calibration.Clear(); episodes.Clear(); finishedEpisodes.Clear();
        string indexFile = Path.Combine(root, "cache", "decision-index-v1.json");
        var index = HistoryCache.Read<DecisionCheckpoint>(indexFile);
        var indexed = index?.Rows.ToDictionary(r => r.File.Path, StringComparer.OrdinalIgnoreCase) ?? new();
        decisionIndex.Clear();
        foreach (var file in HistoryCache.Scan(corpus))
            {
                ct.ThrowIfCancellationRequested();
                DecisionInfo? row = null;
                if (indexed.TryGetValue(file.Path, out var old) && old.File == file) row = old.Info;
                else { try { row = J.ReadAs<DecisionInfo>(file.Path); } catch (Exception e) when (e is IOException or JsonException) { } }
                decisionIndex[file.Path] = new(file, row);
                if (row == null) continue; decisions++;
                if (row.EpisodeId != "")
                {
                    if (row.RunId != "") episodes[row.RunId] = row.EpisodeId;
                    if (row.NextRunId != "") episodes[row.NextRunId] = row.EpisodeId;
                    if (row.Return != null) finishedEpisodes.Add(row.EpisodeId);
                }
                if (row.Confirmed) { confirmed++; Calibrate(row.CalibrationKey, row.PredictedWin, row.Won); }
                // Interrupted/unconfirmed commands remain excluded. They are never replayed.
            }
        ct.ThrowIfCancellationRequested();
        HistoryCache.Write(indexFile, new DecisionCheckpoint(decisionIndex.Values.ToArray()));
        loaded = true; RefreshStatus();
    }
    internal void PrepareSearch()
    {
        // JsonNode materializes properties lazily. Materialize the read-only
        // empirical pool once on the owning thread before parallel sampling.
        static void Touch(JsonNode? n)
        {
            if (n is JsonObject o) foreach (var p in o) Touch(p.Value);
            else if (n is JsonArray a) foreach (var v in a) Touch(v);
        }
        foreach (var n in cards.Values.Concat(maps.Values).Concat(shops.Values)) Touch(n);
    }
    internal int[] BadgeThresholds(string family) => cards.Values.Where(c => Cards.Family(c) == family && c.At("umbral").N() > 0)
        .Select(c => (int)c.At("umbral").N()).Distinct().Order().ToArray();
    internal JsonNode[] SampleCards(Random rng, string kind, string family = "", int threshold = 0)
    {
        var pool = cards.Values.Where(c => (kind == "badge" ? c.At("umbral").N() > 0 : c.At("umbral").N() <= 0)
            && (family == "" || Cards.Family(c) == family) && (threshold == 0 || c.At("umbral").N() == threshold)).ToArray();
        // Uniform over seen definitions is explicitly an uncertain prior, not
        // an inferred server drop rate or an optimal family affinity formula.
        return pool.OrderBy(_ => rng.Next()).Take(3).Select(c => c.DeepClone()).ToArray();
    }
    internal JsonNode? SampleMap(Random rng, int depth)
    {
        if (maps.Count == 0) return null;
        double nearest = maps.Values.Min(m => Math.Abs(m.At("sourceDepth").N() - depth));
        var pool = maps.Values.Where(m => Math.Abs(m.At("sourceDepth").N() - depth) <= nearest).ToArray();
        return pool[rng.Next(pool.Length)].DeepClone();
    }
    internal JsonNode SampleShop(Random rng) => shops.Count == 0 ? new JsonObject() : shops.Values.ElementAt(rng.Next(shops.Count)).DeepClone();
    internal static string CalibrationKey(JsonNode s)
    {
        var run = s.At("sima", "run"); string node = run.At("nodo").S();
        return run.At("mapa", "nodos", node, "tipo").S() + ":" + (int)(run.At("profundidad").N() / 5)
            + ":" + Cards.Current(run).Any(c => Cards.IsCore(c, new Settings()));
    }
    void Calibrate(LearningDecision row)
    {
        Calibrate(row.CalibrationKey, row.PredictedWin, row.Won);
    }
    void Calibrate(string key, double? prediction, bool? outcome)
    {
        if (prediction is not { } predicted || outcome is not { } won || key == "") return;
        var old = calibration.GetValueOrDefault(key);
        calibration[key] = (old.Count + 1, old.Predicted + predicted, old.Actual + (won ? 1 : 0));
    }
    void Track(string path, LearningDecision row)
    {
        decisionIndex[path] = new(HistoryCache.StampFor(path), new DecisionInfo
        { Id = row.Id, RunId = row.RunId, EpisodeId = row.EpisodeId, NextRunId = row.NextRunId,
            Confirmed = row.Confirmed, Return = row.Return, PredictedWin = row.PredictedWin, Won = row.Won, CalibrationKey = row.CalibrationKey });
    }
    internal double CalibrateWin(JsonNode s, Forecast f)
    {
        var record = calibration.GetValueOrDefault(CalibrationKey(s));
        if (record.Count == 0) return f.Win;
        double expected = (record.Predicted + 2) / (record.Count + 4), actual = (record.Actual + 2) / (record.Count + 4);
        double Logit(double p) { p = Math.Clamp(p, .001, .999); return Math.Log(p / (1 - p)); }
        double offset = Math.Clamp(Logit(actual) - Logit(expected), -2, 2) * record.Count / (record.Count + 32d);
        return 1 / (1 + Math.Exp(-Logit(f.Win) - offset));
    }
    internal string Begin(JsonNode snapshot, Choice[] choices, Choice selected, Settings cfg)
    {
        Observe(snapshot.At("sima", "run"));
        string runId = snapshot.At("sima", "run", "id").S();
        string episodeId = runId != "" && episodes.TryGetValue(runId, out var existing) ? existing : Guid.NewGuid().ToString("N");
        if (runId != "") episodes[runId] = episodeId;
        var row = new LearningDecision
        {
            EpisodeId = episodeId, Before = Slim(snapshot),
            Id = Guid.NewGuid().ToString("N"), RunId = snapshot.At("sima", "run", "id").S(), Time = DateTimeOffset.UtcNow,
            Strategy = cfg.StrategyId, Depth = snapshot.At("sima", "run", "profundidad").N(), Node = snapshot.At("sima", "run", "nodo").S(),
            Method = selected.Method, Args = (JsonObject)selected.Args.DeepClone(), Chosen = Array.IndexOf(choices, selected),
            Features = choices.Select(c => PlannerFeatures.Extract(snapshot, c, cfg)).ToArray(), StateFeatures = PlannerFeatures.Extract(snapshot, null, cfg),
            PredictedWin = cfg.StrategyId != 2 && (selected.Method is "sima.fight" or "sima.open") ? selected.Forecast?.Win : null,
            CalibrationKey = CalibrationKey(snapshot)
        };
        if (row.Chosen < 0) throw new InvalidOperationException("记录操作不在已比较的候选中。");
        int visits = choices.Sum(c => c.Search?.Visits ?? 0);
        if (visits > 0) row.Policy = choices.Select(c => (c.Search?.Visits ?? 0) / (double)visits).ToArray();
        J.Write(FileFor(row.Id), row); Track(FileFor(row.Id), row); pending[row.Id] = row; decisions++; RefreshStatus(); return row.Id;
    }
    string FileFor(string id) => Path.Combine(root, "decisions", id + ".json");
    static LearningDecision? Read(string file)
    {
        try { return J.ReadAs<LearningDecision>(file); } catch (Exception e) when (e is IOException or JsonException) { return null; }
    }
    internal void Confirm(string id, JsonNode? after, JsonNode? receipt = null)
    {
        if (!pending.Remove(id, out var row)) return;
        row.Confirmed = true; row.Outcome = "confirmed"; row.After = after == null ? null : Slim(after); row.Receipt = receipt?.DeepClone();
        row.NextRunId = after.At("sima", "run", "id").S();
        if (row.NextRunId != "") episodes[row.NextRunId] = row.EpisodeId;
        if (row.RunId == "") { row.RunId = after.At("sima", "run", "id").S(); row.Depth = after.At("sima", "run", "profundidad").N(); }
        if (row.Method is "sima.fight" or "sima.open" && after.At("lastBattle", "before", "id").S() == row.RunId
            && after.At("lastBattle", "time").S() != row.Before.At("lastBattle", "time").S()
            && after.At("lastBattle", "result", "win") is JsonValue result && result.TryGetValue<bool>(out var won)) row.Won = won;
        Calibrate(row); J.Write(FileFor(id), row); Track(FileFor(id), row); confirmed++; Observe(after.At("sima", "run")); RefreshStatus();
    }
    internal void Abort(string id, string outcome)
    {
        if (!pending.Remove(id, out var row)) return;
        row.Outcome = outcome; J.Write(FileFor(id), row); Track(FileFor(id), row); RefreshStatus();
    }
    internal void FinishRun(string runId, double depth, Settings cfg)
    {
        if (!episodes.TryGetValue(runId, out var episode)) return;
        var dir = Path.Combine(root, "decisions"); if (!Directory.Exists(dir)) return;
        foreach (var file in decisionIndex.Values.Where(r => r.Info is { Confirmed: true, Return: null } info && info.EpisodeId == episode).Select(r => r.File.Path).ToArray())
        {
            var row = Read(file); if (row == null || !row.Confirmed || row.EpisodeId != episode || row.Return != null) continue;
            row.FinalDepth = depth;
            row.Return = .5 + .45 * Math.Tanh((depth - row.Depth - (row.Won == false ? 1 : 0)) / 8);
            J.Write(file, row); Track(file, row);
        }
        finishedEpisodes.Add(episode); RefreshStatus();
        if (cfg.Search.AutoTrain) Neural.ScheduleTraining(cfg.Search.TrainingEpochs, cfg.Seed, cfg.Search.TrainingDevice);
    }
    internal object Status() => new { corpus = summary, neural = Neural.Status(), path = root,
        sources = "所有策略；只训练已确认操作与已结束整次下井；搜索标签与实战回报分别保存" };
}

static class PlannerFeatures
{
    internal const int Count = 112;
    internal static double[] Extract(JsonNode s, Choice? choice, Settings cfg)
    {
        var x = new double[Count]; var run = s.At("sima", "run"); var hs = run.At("heroes").A().ToArray();
        var hp = hs.Select(h => Math.Clamp(h.At("hp").N() / Math.Max(1, h.At("hpMax").N()), 0, 1)).ToArray();
        var fx = Effects.Parse(Cards.Current(run), cfg);
        x[0] = Math.Tanh(run.At("profundidad").N() / 30); x[1] = run.At("mapa", "nodos", run.At("nodo").S(), "fila").N() / 12;
        x[2] = hp.DefaultIfEmpty().Average(); x[3] = hp.DefaultIfEmpty().Min(); x[4] = hs.Count(h => !h.At("caido").B()) / 4d;
        x[5] = hs.Select(h => h.At("anima").N() / 100).DefaultIfEmpty().Average();
        x[6] = Math.Tanh(run.At("lagrimas").N() / 3); x[7] = Math.Tanh(run.At("botin", "obolos").N() / 100);
        x[8] = fx.Freeze ? 1 : 0; x[9] = fx.Cleave > 0 ? 4d / fx.Cleave : 0;
        double[] values = [fx.Damage, fx.Speed, fx.Crit, fx.CritDamage, fx.Life, fx.Defense, fx.Shield, fx.VsStatus, fx.VsSlow, fx.ReactionDamage, fx.Leech, fx.Slow];
        for (int i=0;i<values.Length;i++) x[10+i] = Math.Tanh(values[i]);
        string[] methods = ["sima.move", "sima.fight", "sima.reliquia", "sima.blason", "sima.guarda", "sima.trade", "sima.rest", "sima.tear", "sima.omen", "sima.pacto", "sima.open"];
        int method = Array.IndexOf(methods, choice?.Method); if (method >= 0) x[22+method] = 1;
        if (choice?.Card != null)
        {
            var c = Effects.Parse([choice.Card], cfg);
            double[] added = [c.Damage, c.Speed, c.Crit, c.CritDamage, c.Life, c.Defense, c.Shield, c.VsStatus, c.VsSlow, c.ReactionDamage];
            for (int i=0;i<added.Length;i++) x[33+i] = Math.Tanh(added[i]);
            x[43] = c.Freeze && !fx.Freeze ? 1 : 0; x[44] = c.Cleave > 0 ? 4d / c.Cleave : 0;
        }
        x[45] = choice?.Card == null ? 0 : Math.Tanh(CardValue.Marginal(choice.Card, Cards.Current(run), cfg)); x[46] = x[2]; x[47] = fx.Unknown.Count / (double)Math.Max(1, Cards.Current(run).Length);
        string destination = choice?.Method == "sima.move" ? choice.Args.At("nodoId").S() : run.At("nodo").S();
        var node = run.At("mapa", "nodos", destination);
        string[] rooms = ["combate", "elite", "jefe", "santuario", "chatarrero", "presagio", "osario"];
        int room = Array.IndexOf(rooms, node.At("tipo").S()); if (room >= 0) x[48+room] = 1;
        x[55] = node.At("fila").N() / 12; x[56] = node.At("salidas").A().Count() / 3d;
        x[57] = s.At("peeks", destination, "enemigos").A().Count() / 4d;
        var loads = (choice?.Args.At("objetivos") as JsonObject)?.GroupBy(p => p.Value.S()).Select(g => g.Count()).ToArray() ?? [];
        x[58] = loads.DefaultIfEmpty().Max() / 4d; x[59] = loads.Length / 4d;
        string[] rest = ["reposar", "meditar", "afilar", "levantar"];
        int restKind = Array.IndexOf(rest, choice?.Args.At("opcion").S()); if (restKind >= 0) x[60+restKind] = 1;
        if (choice?.Card is { } card)
        {
            int family = Array.IndexOf(CardCatalog.Families, Cards.Family(card)); if (family >= 0) x[64+family] = 1;
            var effect = Effects.Parse([card], cfg);
            x[71] = card.At("umbral").N() / 8; x[72] = Cards.Current(run).Count(c => c.At("id").S() == card.At("id").S()) / 3d;
            x[73] = effect.Freeze ? 1 : 0; x[74] = effect.Burn ? 1 : 0; x[75] = effect.Lightning ? 1 : 0;
            x[76] = effect.Poison ? 1 : 0; x[77] = effect.Detonate ? 1 : 0;
            x[78] = Math.Tanh(effect.Slow); x[79] = Math.Tanh(effect.Leech);
        }
        for (int hero = 0; hero < Math.Min(4, hs.Length); hero++)
        {
            var h = hs[hero]; var account = s.At("heroes").A().FirstOrDefault(a => a.At("id").S() == h.At("id").S());
            double[] stats = [h.At("hp").N() / Math.Max(1,h.At("hpMax").N()), h.At("anima").N()/100,
                Math.Log10(1+Math.Max(0,account.At("stats","atk").N()))/10, Math.Log10(1+Math.Max(0,account.At("stats","pmag").N()))/10,
                account.At("stats","crit").N()/100, account.At("stats","critdmg").N()/500,
                choice?.Args.At("heroId").S() == h.At("id").S() ? 1 : 0,
                Math.Tanh(h.At("afilado").N()/5)];
            for (int stat=0;stat<8;stat++) x[80+hero*8+stat] = stats[stat];
        }
        for (int i=0;i<x.Length;i++) x[i] = double.IsFinite(x[i]) ? Math.Clamp(x[i], -5, 5) : 0;
        return x;
    }
}
