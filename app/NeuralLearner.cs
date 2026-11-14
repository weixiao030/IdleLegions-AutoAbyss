using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FrostAbyss;

// Small MLP, trained on CPU or CUDA from confirmed, finished real episodes.
// Action scores share a trunk with the value head. Old policies contribute
// value targets only; only actual search visits supervise the policy head.
sealed class NeuralLearner
{
    internal sealed class Network
    {
        public int Schema { get; set; } = 1;
        public int Version { get; set; }
        public double[] Weights { get; set; } = new double[Hidden * PlannerFeatures.Count];
        public double[] Bias { get; set; } = new double[Hidden];
        public double[] Policy { get; set; } = new double[Hidden];
        public double[] Value { get; set; } = new double[Hidden];
        public double ValueBias { get; set; }
        public double ValidationLoss { get; set; }
        public int Episodes { get; set; }
        public int Samples { get; set; }
    }
    internal const int Hidden = 64;
    readonly string root;
    readonly object gate = new();
    Network active = new(); // Never mutate a published model.
    Task? training;
    string message = "等待已结束实战；冷启动使用 UCT 搜索，不使用随机网络选牌";
    int trainSamples, validationSamples, policySamples;
    string requestedDevice = "cpu", device = "未开始";
    string? deviceName, fallbackReason;
    long cudaBatches;
    readonly Func<Network, CudaTraining> cudaFactory;
    internal sealed class Frozen(Network n)
    {
        internal bool Ready => n.Version > 0;
        internal int Version => n.Version;
        internal double Policy(double[] x) => n.Version == 0 ? 0 : Dot(Forward(n, x), n.Policy);
        internal double Value(double[] x) => n.Version == 0 ? .5 : PredictValue(n, x);
    }
    internal Frozen Capture() => new(Volatile.Read(ref active));
    internal bool Ready => Volatile.Read(ref active).Version > 0;
    internal int Version => Volatile.Read(ref active).Version;
    internal bool TrainingActive => training is { IsCompleted: false };
    internal NeuralLearner(string root, Func<Network, CudaTraining>? cudaFactory = null)
    {
        this.root = root;
        this.cudaFactory = cudaFactory ?? (n => new CudaTraining(n));
        try
        {
            var candidate = JsonSerializer.Deserialize<Network>(File.ReadAllText(Path.Combine(root, "network.json")), J.Options);
            if (candidate != null && Valid(candidate)) { active = candidate; message = "已载入通过留出验证的模型"; }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }
    static bool Valid(Network n) => n.Schema == 1 && n.Version > 0
        && n.Weights?.Length == Hidden * PlannerFeatures.Count && n.Bias?.Length == Hidden
        && n.Policy?.Length == Hidden && n.Value?.Length == Hidden
        && n.Weights.Concat(n.Bias).Concat(n.Policy).Concat(n.Value).Append(n.ValueBias).Append(n.ValidationLoss).All(double.IsFinite);
    static Network Copy(Network n) => JsonSerializer.Deserialize<Network>(JsonSerializer.Serialize(n, J.Options), J.Options)!;
    static double[] Forward(Network n, double[] x)
    {
        var h = new double[Hidden];
        for (int j = 0; j < Hidden; j++)
        {
            double z = n.Bias[j];
            for (int i = 0; i < PlannerFeatures.Count; i++) z += n.Weights[j * PlannerFeatures.Count + i] * x[i];
            h[j] = Math.Tanh(z);
        }
        return h;
    }
    static double Dot(double[] a, double[] b) { double sum = 0; for (int i = 0; i < a.Length; i++) sum += a[i] * b[i]; return sum; }
    static double Sigmoid(double x) => 1 / (1 + Math.Exp(-Math.Clamp(x, -30, 30)));
    internal double Policy(double[] x) { var n = Volatile.Read(ref active); return n.Version == 0 ? 0 : Dot(Forward(n, x), n.Policy); }
    internal double Value(double[] x) { var n = Volatile.Read(ref active); return n.Version == 0 ? .5 : Sigmoid(Dot(Forward(n, x), n.Value) + n.ValueBias); }
    internal object Status()
    {
        lock (gate)
        {
            var n = Volatile.Read(ref active);
            return new { ready = n.Version > 0, version = n.Version, training = training is { IsCompleted: false }, message,
                trainSamples, validationSamples, policySamples, acceptedSamples = n.Samples, acceptedEpisodes = n.Episodes,
                validationLoss = n.Version > 0 ? (double?)n.ValidationLoss : null,
                architecture = "112 → 64 tanh → 策略评分 / sigmoid 价值双头；按整次下井划分验证集",
                requestedDevice, device, deviceName, fallbackReason, cudaBatches };
        }
    }
    internal Task ScheduleTraining(int epochs, int seed, string trainingDevice = "cpu")
    {
        if (trainingDevice is not ("cpu" or "cuda")) throw new ArgumentException("训练设备须为 cpu 或 cuda");
        lock (gate)
        {
            if (training is { IsCompleted: false }) return training;
            requestedDevice = trainingDevice; device = "未开始"; deviceName = fallbackReason = null; cudaBatches = 0;
            message = "正在读取已确认且已结束的实战轨迹";
            return training = Task.Run(() =>
            {
                try { Train(epochs, seed, trainingDevice); }
                catch (Exception e) { lock (gate) message = "训练失败，继续保留现有模型：" + e.Message; }
            });
        }
    }
    static bool FeaturesValid(double[]? x) => x?.Length == PlannerFeatures.Count && x.All(double.IsFinite);
    static bool Usable(LearningDecision r) => r.Confirmed && r.Return is >= 0 and <= 1
        && r.FinalDepth != null && r.EpisodeId != "" && FeaturesValid(r.StateFeatures)
        && r.Features.Length > 0 && r.Features.All(FeaturesValid) && r.Chosen >= 0 && r.Chosen < r.Features.Length;
    internal static bool PolicyValid(LearningDecision r) => r.Policy?.Length == r.Features.Length
        && r.Policy.All(p => double.IsFinite(p) && p >= 0) && Math.Abs(r.Policy.Sum() - 1) < .00001;
    internal static bool ValidationEpisode(string id) => SHA256.HashData(Encoding.UTF8.GetBytes(id))[0] % 5 == 0;
    void Train(int epochs, int seed, string trainingDevice)
    {
        var dir = Path.Combine(root, "decisions"); var rows = new List<LearningDecision>();
        foreach (var file in J.HistoryFiles(dir).Order(StringComparer.Ordinal))
        {
            try
            {
                var r = J.ReadAs<LearningDecision>(file);
                if (r != null && Usable(r)) rows.Add(r);
            }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        // The same episode can never appear in both sets, even when the game
        // changes its run id on every floor. Ordering is fixed before shuffling.
        var train = rows.Where(r => !ValidationEpisode(r.EpisodeId)).ToArray();
        var valid = rows.Where(r => ValidationEpisode(r.EpisodeId)).ToArray();
        lock (gate) { trainSamples = train.Length; validationSamples = valid.Length; policySamples = rows.Count(PolicyValid); }
        if (train.Length < 64 || valid.Length < 16 || train.Select(r => r.EpisodeId).Distinct().Count() < 4
            || valid.Select(r => r.EpisodeId).Distinct().Count() < 2)
        {
            lock (gate) message = "样本不足：至少训练64条/4次下井，验证16条/2次下井；继续搜索并自动采集全部策略";
            return;
        }
        var previous = Volatile.Read(ref active); var candidate = Copy(previous); var rng = new Random(seed);
        if (candidate.Version == 0)
        {
            for (int i = 0; i < candidate.Weights.Length; i++) candidate.Weights[i] = (rng.NextDouble() - .5) * .2;
            for (int i = 0; i < Hidden; i++) { candidate.Policy[i] = (rng.NextDouble() - .5) * .1; candidate.Value[i] = (rng.NextDouble() - .5) * .1; }
        }
        var initial = Copy(candidate);
        double initialLoss = Loss(previous, valid);
        double bestLoss = initialLoss; Network? best = null;
        void Fit(bool useCuda)
        {
            candidate = Copy(initial); bestLoss = initialLoss; best = null;
            var ordered = train.ToArray(); var shuffle = new Random(seed);
            using var cuda = useCuda ? cudaFactory(candidate) : null;
            lock (gate) { device = useCuda ? "CUDA" : "CPU"; deviceName = cuda?.DeviceName; }
            for (int epoch = 0; epoch < epochs; epoch++)
            {
                lock (gate) message = $"{device} 训练第 {epoch + 1} / {epochs} 轮；验证后决定是否启用";
                for (int i = ordered.Length - 1; i > 0; i--) { int j = shuffle.Next(i + 1); (ordered[i], ordered[j]) = (ordered[j], ordered[i]); }
                double rate = .006 / Math.Sqrt(epoch + 1);
                if (cuda != null)
                {
                    cuda.TrainEpoch(ordered, rate); cuda.Download(candidate);
                    lock (gate) cudaBatches = cuda.Batches;
                }
                else TrainCpuEpoch(candidate, ordered, rate);
                double loss = Loss(candidate, valid);
                if (double.IsFinite(loss) && loss < bestLoss) { bestLoss = loss; best = Copy(candidate); }
            }
        }
        if (trainingDevice == "cuda")
        {
            try { Fit(true); }
            catch (Exception e)
            {
                lock (gate) { fallbackReason = "CUDA 不可用，已回退 CPU：" + e.Message; message = fallbackReason; }
                // Restart from the untouched initial weights, even after a
                // partial GPU epoch. Never publish a partially failed model.
                Fit(false);
            }
        }
        else Fit(false);
        if (best == null || bestLoss >= initialLoss - .00001 || ValueLoss(best, valid) > ValueLoss(previous, valid) + .0001)
        {
            lock (gate) message = "留出验证未改善，保留旧模型；继续累积实战";
            return;
        }
        best.Version = previous.Version + 1; best.ValidationLoss = bestLoss;
        best.Samples = rows.Count; best.Episodes = rows.Select(r => r.EpisodeId).Distinct().Count();
        if (!Valid(best)) throw new Exception("训练权重无效");
        string filePath = Path.Combine(root, "network.json");
        if (File.Exists(filePath)) File.Copy(filePath, filePath + ".previous", true);
        J.Write(filePath, best); Volatile.Write(ref active, best);
        lock (gate) message = $"模型 v{best.Version} 已通过整次下井留出验证：损失 {initialLoss:F5} → {bestLoss:F5}；训练 {train.Length} 条，验证 {valid.Length} 条";
        J.Write(Path.Combine(root, "last-training.json"), new { time = DateTimeOffset.UtcNow, best.Version, initialLoss, bestLoss,
            trainSamples, validationSamples, policySamples, requestedDevice, device, deviceName, fallbackReason, cudaBatches });
    }
    static void TrainCpuEpoch(Network candidate, LearningDecision[] rows, double rate)
    {
        foreach (var row in rows)
        {
            var h = Forward(candidate, row.StateFeatures);
            double value = Sigmoid(Dot(h, candidate.Value) + candidate.ValueBias);
            double gradient = 2 * (value - row.Return!.Value) * value * (1 - value);
            Backward(candidate, row.StateFeatures, h, candidate.Value, gradient, rate);
            candidate.ValueBias -= rate * gradient;
            if (!PolicyValid(row)) continue;
            var hidden = row.Features.Select(x => Forward(candidate, x)).ToArray();
            var logits = hidden.Select(x => Dot(x, candidate.Policy)).ToArray();
            double max = logits.Max(); var probs = logits.Select(v => Math.Exp(Math.Clamp(v - max, -30, 0))).ToArray(); double total = probs.Sum();
            for (int i = 0; i < probs.Length; i++)
                Backward(candidate, row.Features[i], hidden[i], candidate.Policy, (probs[i] / total - row.Policy![i]) / probs.Length, rate * .3);
        }
    }
    static void Backward(Network n, double[] x, double[] h, double[] head, double gradient, double rate)
    {
        gradient = Math.Clamp(gradient, -2, 2);
        for (int j = 0; j < Hidden; j++)
        {
            double dh = gradient * head[j] * (1 - h[j] * h[j]);
            head[j] -= rate * (gradient * h[j] + .0001 * head[j]);
            n.Bias[j] -= rate * dh;
            for (int i = 0; i < PlannerFeatures.Count; i++)
            {
                int k = j * PlannerFeatures.Count + i;
                n.Weights[k] -= rate * (dh * x[i] + .0001 * n.Weights[k]);
            }
        }
    }
    static double PredictValue(Network n, double[] x) => Sigmoid(Dot(Forward(n, x), n.Value) + n.ValueBias);
    static double ValueLoss(Network n, LearningDecision[] rows) => rows.Average(r => Math.Pow(PredictValue(n, r.StateFeatures) - r.Return!.Value, 2));
    static double Loss(Network n, LearningDecision[] rows)
    {
        double loss = ValueLoss(n, rows); var policies = rows.Where(PolicyValid).ToArray();
        if (policies.Length == 0) return loss;
        return loss + .05 * policies.Average(r =>
        {
            var scores = r.Features.Select(x => Dot(Forward(n, x), n.Policy)).ToArray();
            double max = scores.Max(), logSum = Math.Log(scores.Sum(s => Math.Exp(Math.Clamp(s - max, -30, 0))));
            return scores.Select((s, i) => -r.Policy![i] * (s - max - logSum)).Sum();
        });
    }
}
