using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace FrostAbyss;

sealed class SearchSettings
{
    public string Mode { get; set; } = "manual";
    public int Iterations { get; set; } = 1024;
    public int AdaptiveIterations { get; set; } = 4096;
    public int NeuralIterations { get; set; } = 2048;
    public int Horizon { get; set; } = 20;
    public int Workers { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
    public double Exploration { get; set; } = 1.4;
    public bool AutoTrain { get; set; } = true;
    public int TrainingEpochs { get; set; } = 12;
    public string TrainingDevice { get; set; } = "cpu";
    public int Budget(int strategy) => strategy == 6 ? AdaptiveIterations : strategy == 7 ? NeuralIterations : Iterations;
    internal void Validate()
    {
        if (Mode is not ("manual" or "auto")) throw new Exception("参数配置方式无效，请选择 Auto 或手动。");
        if (Mode == "auto") SearchHardware.Current.Recommendation.Parameters.Apply(this);
        if (TrainingDevice is not ("cpu" or "cuda")) throw new Exception("训练设备无效，请选择 CPU 或 CUDA。");
        if (new[] { Iterations, AdaptiveIterations, NeuralIterations }.Any(n => n is < 1 or > 1_000_000)
            || Horizon is < 1 or > 128 || Workers is < 1 or > 64 || TrainingEpochs is < 1 or > 500
            || !double.IsFinite(Exploration) || Exploration <= 0)
            throw new Exception("搜索参数无效：次数1～1000000、步长1～128、并发1～64、训练轮数1～500，探索系数须为正数。");
    }
}

sealed record SearchEstimate(int Visits, double Value, double Prior, int MaxDepth, int Nodes,
    int TotalIterations, double ElapsedSeconds, string Algorithm, int ModelVersion);

// Open-loop stochastic UCT: each simulation samples transitions again. A node
// is an action history, not a frozen lucky combat outcome. Menus may differ
// between samples; edges are reconciled by method + exact arguments.
sealed class SearchPlanner
{
    sealed class Edge(Choice action, double prior)
    {
        internal readonly Choice Action = action;
        internal readonly double Prior = prior;
        internal int Visits;
        internal double Sum;
        internal Node Next = new();
    }
    sealed class Node
    {
        internal readonly Dictionary<string, Edge> Edges = [];
        internal int Visits;
    }
    internal static string Key(Choice c) => c.Method + ":" + c.Args.ToJsonString();
    internal static Choice Copy(Choice c) => new(c.Label, c.Method, (JsonObject)c.Args.DeepClone(),
        card: c.Card?.DeepClone()) { Reason = c.Reason };
    readonly SearchMemory memory;
    internal SearchPlanner(SearchMemory memory) { this.memory = memory; }

    internal Choice[] Rank(Choice[] choices, JsonNode snapshot, Settings cfg, Simulation simulation, CancellationToken ct)
    {
        cfg.Search.Validate();
        ct.ThrowIfCancellationRequested();
        if (choices.Length == 1)
        {
            // There is nothing to rank. Keep the independent combat forecast,
            // receipt checks and value-learning trace in Pilot; do not invent
            // search visits or a policy target for a forced action.
            choices[0].Search = new(0, 0, 1, 0, 1, 0, 0, "唯一合法操作（无需搜索）", memory.Version);
            choices[0].Reason = "唯一合法操作；跳过无分支搜索，仍核验战斗预测与游戏回执";
            return choices;
        }
        var neural = memory.Neural.Capture();
        var clock = Stopwatch.StartNew(); int budget = Math.Max(choices.Length, cfg.Search.Budget(cfg.StrategyId));
        if (choices.Length == 0) return [];
        int workers = Math.Min(cfg.Search.Workers, budget);
        var roots = new Node[workers]; var depths = new int[workers]; var counts = new int[workers];
        // Prepared before parallel work: no shared lazy JsonNode reads in workers.
        var slim = SearchMemory.Slim(snapshot);
        var states = Enumerable.Range(0, workers).Select(_ => slim.DeepClone()).ToArray();
        var settingsJson = JsonSerializer.Serialize(cfg, J.Options);
        var workerSettings = Enumerable.Range(0, workers).Select(_ => JsonSerializer.Deserialize<Settings>(settingsJson, J.Options)!).ToArray();
        var offers = Enumerable.Range(0, workers).Select(_ => choices.Select(Copy).ToArray()).ToArray();
        var priors = choices.Select(c => cfg.StrategyId == 7 && neural.Ready ? neural.Policy(PlannerFeatures.Extract(snapshot, c, cfg)) : 0).ToArray();
        double max = priors.DefaultIfEmpty().Max();
        priors = priors.Select(v => Math.Exp(Math.Clamp(v - max, -30, 0))).ToArray();
        double sum = priors.Sum(); for (int i=0;i<priors.Length;i++) priors[i] /= Math.Max(1e-12, sum);
        Parallel.For(0, workers, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = workers }, worker =>
        {
            var rng = new Random(unchecked(cfg.Seed + worker * 7919));
            var root = roots[worker] = new Node();
            for (int i=0;i<offers[worker].Length;i++) root.Edges[Key(offers[worker][i])] = new(offers[worker][i], priors[i]);
            int iterations = budget / workers + (worker < budget % workers ? 1 : 0);
            for (int iteration=0;iteration<iterations;iteration++)
            {
                ct.ThrowIfCancellationRequested();
                var world = new SearchWorld(states[worker].DeepClone(), cfg, simulation, memory, rng, ct, workerSettings[worker]);
                Node node = root; var path = new List<(Node, Edge)>(); bool rollout = false;
                for (int step=0;step<cfg.Search.Horizon && !world.Terminal;step++)
                {
                    depths[worker] = Math.Max(depths[worker], step + 1);
                    var actions = step == 0 ? offers[worker] : world.Actions();
                    if (actions.Length == 0) break;
                    if (rollout) { world.Apply(actions[rng.Next(actions.Length)]); continue; }
                    var legal = new List<Edge>();
                    foreach (var action in actions)
                    {
                        string key = Key(action);
                        if (!node.Edges.TryGetValue(key, out var edge))
                        {
                            double prior = cfg.StrategyId == 7 && neural.Ready ? Math.Exp(Math.Clamp(neural.Policy(PlannerFeatures.Extract(world.Snapshot, action, cfg)), -12, 12)) : 1;
                            node.Edges[key] = edge = new(action, prior); counts[worker]++;
                        }
                        legal.Add(edge);
                    }
                    var unvisited = legal.Where(e => e.Visits == 0).ToArray();
                    Edge selected;
                    if (unvisited.Length > 0)
                    {
                        // Root coverage is spread across workers, independent of offer order.
                        selected = step == 0 ? unvisited.FirstOrDefault(e => Key(e.Action) == Key(offers[worker][(iteration * workers + worker) % choices.Length])) ?? unvisited[rng.Next(unvisited.Length)] : unvisited[rng.Next(unvisited.Length)];
                        rollout = true;
                    }
                    else
                    {
                        double priorTotal = legal.Sum(e => e.Prior);
                        double Bound(Edge e) => e.Sum / e.Visits + cfg.Search.Exploration * (cfg.StrategyId == 7 && neural.Ready
                            ? e.Prior / Math.Max(1e-12, priorTotal) * Math.Sqrt(node.Visits + 1) / (1 + e.Visits)
                            : Math.Sqrt(Math.Log(node.Visits + 1d) / e.Visits));
                        selected = legal.MaxBy(Bound)!;
                    }
                    path.Add((node, selected)); world.Apply(selected.Action); node = selected.Next;
                }
                double value = world.Value;
                if (cfg.StrategyId == 7 && neural.Ready && !world.Terminal)
                    value = .7 * value + .3 * neural.Value(PlannerFeatures.Extract(world.Snapshot, null, cfg));
                foreach (var (parent, edge) in path) { parent.Visits++; edge.Visits++; edge.Sum += value; }
            }
        });
        int nodes = counts.Sum() + workers * choices.Length;
        for (int i=0;i<choices.Length;i++)
        {
            string key = Key(choices[i]); var stats = roots.Select(r => r.Edges[key]).ToArray(); int visits = stats.Sum(e => e.Visits);
            choices[i].Search = new(visits, visits > 0 ? stats.Sum(e => e.Sum) / visits : 0, priors[i], depths.Max(), nodes,
                budget, clock.Elapsed.TotalSeconds, cfg.StrategyId == 7 ? (neural.Ready ? "策略价值网络 + PUCT" : "冷启动 UCT（积累神经网络样本）") : cfg.StrategyId == 6 ? "UCT + 实战校准" : "UCT", neural.Version);
            choices[i].Reason = $"{choices[i].Search!.Algorithm}：访问{visits}次，路径价值{choices[i].Search!.Value:F3}；仅在本地采样，未知房间使用经验先验，真实结果不保证最优层数";
        }
        return choices.OrderByDescending(c => c.Search!.Visits).ThenByDescending(c => c.Search!.Value).ToArray();
    }
}
