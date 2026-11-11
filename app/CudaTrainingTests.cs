namespace FrostAbyss;

static class CudaTrainingTests
{
    // Independent finite differences of the batch objective catch head/trunk
    // offsets, softmax signs, reduction errors and simultaneous update races.
    internal static void Run(Action<bool, string> assert)
    {
        var rng = new Random(12); var n = new NeuralLearner.Network();
        foreach (var a in new[] { n.Weights, n.Bias, n.Policy, n.Value })
            for (int i = 0; i < a.Length; i++) a[i] = (rng.NextDouble() - .5) * .15;
        double[] Feature(double value) { var x = new double[PlannerFeatures.Count]; x[0] = value; x[1] = -.4; x[^1] = .3; return x; }
        LearningDecision[] rows = [
            new() { StateFeatures = Feature(.2), Return = .8, Features = [Feature(-.3), Feature(.7), Feature(.5)], Policy = [.1, .7, .2] },
            new() { StateFeatures = Feature(-.5), Return = .1, Features = [Feature(.1)], Policy = null },
            new() { StateFeatures = Feature(.4), Return = .6, Features = [Feature(.3), Feature(.8)], Policy = [.6, .4] }];
        double Objective()
        {
            double total = 0;
            double[] H(double[] x) => Enumerable.Range(0, 64).Select(j => Math.Tanh(n.Bias[j] +
                x.Select((v, i) => v * n.Weights[j * x.Length + i]).Sum())).ToArray();
            foreach (var row in rows)
            {
                double z = H(row.StateFeatures).Select((v, j) => v * n.Value[j]).Sum() + n.ValueBias;
                double value = 1 / (1 + Math.Exp(-z)); total += Math.Pow(value - row.Return!.Value, 2);
                if (row.Policy == null) continue;
                var scores = row.Features.Select(x => H(x).Select((v, j) => v * n.Policy[j]).Sum()).ToArray();
                double logSum = Math.Log(scores.Sum(Math.Exp));
                total += .3 / scores.Length * scores.Select((s, i) => -row.Policy[i] * (s - logSum)).Sum();
            }
            return total + .00005 * ((3 + .3 * 5) * n.Weights.Sum(v => v * v) +
                .3 * 5 * n.Policy.Sum(v => v * v) + 3 * n.Value.Sum(v => v * v));
        }
        var before = CudaTraining.Pack(n); const double epsilon = 1e-5, rate = .002;
        var expected = new Dictionary<int, double>();
        int offset = 0;
        foreach (var a in new[] { n.Weights, n.Bias, n.Policy, n.Value })
        {
            foreach (int i in new[] { 0, 1, a.Length / 2, a.Length - 1 }.Distinct())
            {
                double original = a[i]; a[i] = original + epsilon; double plus = Objective();
                a[i] = original - epsilon; double minus = Objective(); a[i] = original;
                expected[offset + i] = (plus - minus) / (2 * epsilon);
            }
            offset += a.Length;
        }
        n.ValueBias += epsilon; double high = Objective(); n.ValueBias -= 2 * epsilon; double low = Objective(); n.ValueBias += epsilon;
        expected[offset] = (high - low) / (2 * epsilon);
        using (var gpu = new CudaTraining(n)) { gpu.TrainEpoch(rows, rate); gpu.Download(n); }
        var after = CudaTraining.Pack(n);
        double error = expected.Max(kv => Math.Abs((before[kv.Key] - after[kv.Key]) / rate - kv.Value));
        assert(error < 1e-7, $"CUDA双头及共享主干梯度通过独立有限差分校验（最大误差{error:G3}）");
        var originalWeights = CudaTraining.Pack(n);
        using (var gpu = new CudaTraining(n))
        {
            for (int i = 0; i < 2; i++) gpu.TrainEpoch(rows, rate);
            gpu.Download(n);
        }
        var runA = CudaTraining.Pack(n);
        Array.Copy(originalWeights, 0, n.Weights, 0, n.Weights.Length);
        offset = n.Weights.Length; Array.Copy(originalWeights, offset, n.Bias, 0, 64); offset += 64;
        Array.Copy(originalWeights, offset, n.Policy, 0, 64); offset += 64;
        Array.Copy(originalWeights, offset, n.Value, 0, 64); n.ValueBias = originalWeights[^1];
        using (var gpu = new CudaTraining(n)) { for (int i = 0; i < 2; i++) gpu.TrainEpoch(rows, rate); gpu.Download(n); }
        assert(runA.Zip(CudaTraining.Pack(n)).All(p => Math.Abs(p.First - p.Second) < 1e-12), "CUDA重复训练数值一致，无并发写入竞争");
    }
}
