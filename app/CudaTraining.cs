using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

namespace FrostAbyss;

// Bounded minibatches. Every kernel reads immutable weights; a separate kernel
// applies the fully reduced gradients, so no sample races with another update.
// Validation and the published model format are shared with the CPU learner.
sealed class CudaTraining : IDisposable
{
    const int Inputs = PlannerFeatures.Count, Hidden = NeuralLearner.Hidden;
    const int Bias = Inputs * Hidden, Policy = Bias + Hidden, Value = Policy + Hidden;
    const int ValueBias = Value + Hidden, Parameters = ValueBias + 1;
    const int MaxEntries = 8192, BatchRows = 16;
    readonly Context context;
    readonly CudaAccelerator accelerator;
    readonly MemoryBuffer1D<double, Stride1D.Dense> weights, gradients, features, hidden, scores, seeds, targets;
    readonly MemoryBuffer1D<int, Stride1D.Dense> offsets;
    readonly Action<Index1D, Buffers> forward, score, seed, gradient;
    readonly Action<Index1D, Buffers, double> update;
    internal string DeviceName => accelerator.Name;
    internal long Batches { get; private set; }

    public CudaTraining(NeuralLearner.Network network)
    {
        // No CUDA context is created unless CUDA was explicitly selected.
        context = Context.Create(b => b.Cuda().EnableAlgorithms());
        try
        {
            if (context.GetCudaDevices().Count == 0) throw new InvalidOperationException("未检测到可用的 NVIDIA CUDA 设备，请检查显卡驱动");
            accelerator = context.CreateCudaAccelerator(0);
            try
            {
                forward = accelerator.LoadAutoGroupedStreamKernel<Index1D, Buffers>(Forward);
                score = accelerator.LoadAutoGroupedStreamKernel<Index1D, Buffers>(Score);
                seed = accelerator.LoadAutoGroupedStreamKernel<Index1D, Buffers>(Seed);
                gradient = accelerator.LoadAutoGroupedStreamKernel<Index1D, Buffers>(Gradient);
                update = accelerator.LoadAutoGroupedStreamKernel<Index1D, Buffers, double>(Update);
                weights = accelerator.Allocate1D(Pack(network));
                gradients = accelerator.Allocate1D<double>(Parameters);
                features = accelerator.Allocate1D<double>(MaxEntries * Inputs);
                hidden = accelerator.Allocate1D<double>(MaxEntries * Hidden);
                scores = accelerator.Allocate1D<double>(MaxEntries);
                seeds = accelerator.Allocate1D<double>(MaxEntries);
                targets = accelerator.Allocate1D<double>(MaxEntries);
                offsets = accelerator.Allocate1D<int>(BatchRows + 1);
            }
            catch { accelerator.Dispose(); throw; }
        }
        catch { context.Dispose(); throw; }
    }

    internal static double[] Pack(NeuralLearner.Network n) =>
        [..n.Weights, ..n.Bias, ..n.Policy, ..n.Value, n.ValueBias];
    internal void Download(NeuralLearner.Network n)
    {
        var w = weights.GetAsArray1D();
        Array.Copy(w, 0, n.Weights, 0, Bias); Array.Copy(w, Bias, n.Bias, 0, Hidden);
        Array.Copy(w, Policy, n.Policy, 0, Hidden); Array.Copy(w, Value, n.Value, 0, Hidden);
        n.ValueBias = w[ValueBias];
        if (!w.All(double.IsFinite)) throw new ArithmeticException("CUDA 训练产生无效数值");
    }

    internal void TrainEpoch(LearningDecision[] rows, double rate)
    {
        for (int start = 0; start < rows.Length;)
        {
            int end = start, entries = 0;
            while (end < rows.Length && end - start < BatchRows)
            {
                int count = 1 + (NeuralLearner.PolicyValid(rows[end]) ? rows[end].Features.Length : 0);
                if (count > MaxEntries) throw new InvalidOperationException($"单条轨迹超过 CUDA 批次上限（{MaxEntries - 1} 个候选）");
                if (entries + count > MaxEntries) break;
                entries += count; end++;
            }
            var x = new double[entries * Inputs]; var y = new double[entries]; var groups = new int[end - start + 1];
            int entry = 0, policyCount = 0;
            for (int row = start; row < end; row++)
            {
                groups[row - start] = entry;
                Array.Copy(rows[row].StateFeatures, 0, x, entry * Inputs, Inputs);
                y[entry++] = rows[row].Return!.Value;
                if (!NeuralLearner.PolicyValid(rows[row])) continue;
                for (int action = 0; action < rows[row].Features.Length; action++)
                {
                    Array.Copy(rows[row].Features[action], 0, x, entry * Inputs, Inputs);
                    y[entry++] = rows[row].Policy![action]; policyCount++;
                }
            }
            groups[^1] = entries;
            features.View.BaseView.SubView(0, x.Length).CopyFromCPU(x);
            targets.View.BaseView.SubView(0, y.Length).CopyFromCPU(y);
            offsets.View.BaseView.SubView(0, groups.Length).CopyFromCPU(groups);
            var b = new Buffers { Weights = weights.View, Gradients = gradients.View, Features = features.View,
                HiddenValues = hidden.View, Scores = scores.View, Seeds = seeds.View, Targets = targets.View,
                Offsets = offsets.View, Entries = entries, Rows = end - start, PolicyCount = policyCount };
            forward(entries * Hidden, b); score(entries, b); seed(b.Rows, b);
            gradient(Parameters, b); update(Parameters, b, rate);
            // Reusing host arrays or submitting the next batch must not race DMA.
            accelerator.Synchronize(); Batches++; start = end;
        }
    }

    struct Buffers
    {
        public ArrayView<double> Weights, Gradients, Features, HiddenValues, Scores, Seeds, Targets;
        public ArrayView<int> Offsets;
        public int Entries, Rows, PolicyCount;
    }
    static void Forward(Index1D index, Buffers b)
    {
        int entry = index / Hidden, j = index % Hidden;
        double sum = b.Weights[Bias + j];
        for (int i = 0; i < Inputs; i++) sum += b.Weights[j * Inputs + i] * b.Features[entry * Inputs + i];
        b.HiddenValues[index] = XMath.Tanh(sum);
    }
    static void Score(Index1D entry, Buffers b)
    {
        bool state = false;
        for (int row = 0; row < b.Rows; row++) if (b.Offsets[row] == entry) state = true;
        double sum = state ? b.Weights[ValueBias] : 0;
        for (int j = 0; j < Hidden; j++) sum += b.HiddenValues[entry * Hidden + j] * b.Weights[(state ? Value : Policy) + j];
        b.Scores[entry] = state ? 1 / (1 + XMath.Exp(-XMath.Clamp(sum, -30.0, 30.0))) : sum;
    }
    static void Seed(Index1D row, Buffers b)
    {
        int start = b.Offsets[row], end = b.Offsets[row + 1];
        double value = b.Scores[start];
        b.Seeds[start] = 2 * (value - b.Targets[start]) * value * (1 - value);
        if (end == start + 1) return;
        double max = b.Scores[start + 1], sum = 0;
        for (int e = start + 2; e < end; e++) max = XMath.Max(max, b.Scores[e]);
        for (int e = start + 1; e < end; e++) sum += XMath.Exp(XMath.Clamp(b.Scores[e] - max, -30.0, 0.0));
        for (int e = start + 1; e < end; e++)
            b.Seeds[e] = .3 * (XMath.Exp(XMath.Clamp(b.Scores[e] - max, -30.0, 0.0)) / sum - b.Targets[e]) / (end - start - 1);
    }
    static void Gradient(Index1D parameter, Buffers b)
    {
        double sum = 0;
        int row = 0, p = parameter;
        for (int e = 0; e < b.Entries; e++)
        {
            if (e >= b.Offsets[row + 1]) row++;
            bool state = e == b.Offsets[row];
            double g = XMath.Clamp(b.Seeds[e], -2.0, 2.0);
            if (p == ValueBias) { if (state) sum += g; }
            else if (p >= Policy)
            {
                bool valueHead = p >= Value;
                if (valueHead == state) sum += g * b.HiddenValues[e * Hidden + p - (valueHead ? Value : Policy)];
            }
            else
            {
                int j = p < Bias ? p / Inputs : p - Bias;
                double h = b.HiddenValues[e * Hidden + j];
                double dh = g * b.Weights[(state ? Value : Policy) + j] * (1 - h * h);
                sum += p < Bias ? dh * b.Features[e * Inputs + p % Inputs] : dh;
            }
        }
        // CPU SGD applies L2 on each head/trunk update. Approximate the same
        // accumulated decay for this minibatch, including the policy rate.
        double decay = p < Bias ? b.Rows + .3 * b.PolicyCount :
            p >= Policy && p < Value ? .3 * b.PolicyCount : p >= Value && p < ValueBias ? b.Rows : 0;
        b.Gradients[p] = sum + .0001 * decay * b.Weights[p];
    }
    static void Update(Index1D p, Buffers b, double rate) => b.Weights[p] -= rate * b.Gradients[p];

    public void Dispose()
    {
        offsets.Dispose(); targets.Dispose(); seeds.Dispose(); scores.Dispose(); hidden.Dispose(); features.Dispose();
        gradients.Dispose(); weights.Dispose(); accelerator.Dispose(); context.Dispose();
    }
}
