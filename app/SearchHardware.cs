using System.Runtime.InteropServices;

namespace FrostAbyss;

sealed record SearchBudget(int Iterations, int AdaptiveIterations, int NeuralIterations,
    int Horizon, int Workers, int TrainingEpochs, double Exploration = 1.4)
{
    internal void Apply(SearchSettings settings)
    {
        settings.Iterations = Iterations; settings.AdaptiveIterations = AdaptiveIterations;
        settings.NeuralIterations = NeuralIterations; settings.Horizon = Horizon;
        settings.Workers = Workers; settings.TrainingEpochs = TrainingEpochs; settings.Exploration = Exploration;
    }
}

sealed record SearchRecommendation(string Level, string Description, SearchBudget Parameters);
sealed record SearchHardwareInfo(int LogicalProcessors, double? MemoryGiB, SearchRecommendation Recommendation);

static class SearchHardware
{
    static readonly Lazy<SearchHardwareInfo> detected = new(Detect);
    internal static SearchHardwareInfo Current => detected.Value;

    // Capacity-based starting budgets, not a benchmark or a promised latency.
    // No GPU is enabled here: training device remains the user's separate choice.
    internal static SearchRecommendation Recommend(int logicalProcessors, double? memoryGiB)
    {
        int threads = Math.Max(1, logicalProcessors);
        if (memoryGiB == null || !double.IsFinite(memoryGiB.Value) || memoryGiB < 7 || threads < 4)
            return new("轻量", "内存信息不足、内存较小或 CPU 线程较少，优先减少搜索等待。", new(64, 128, 96, 6, 1, 6));
        if (threads < 8 || memoryGiB < 15)
            return new("入门", "使用较短路径和较低并发，给游戏保留运行空间。", new(128, 256, 192, 8, Math.Min(2, threads / 2), 8));
        if (threads < 16 || memoryGiB < 31)
            return new("均衡", "适中的搜索预算与路径长度，兼顾后续比较和决策等待。", new(256, 512, 384, 10, Math.Min(4, threads / 2), 8));
        return new("性能", "为线程和内存较充足的电脑增加搜索预算，仍给游戏留出 CPU。", new(512, 1024, 768, 12, Math.Min(8, threads / 2), 12));
    }

    static SearchHardwareInfo Detect()
    {
        double? memory = null;
        try
        {
            var status = new MemoryStatus();
            if (GlobalMemoryStatusEx(status)) memory = status.TotalPhysical / 1073741824d;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        int threads = Environment.ProcessorCount;
        return new(threads, memory, Recommend(threads, memory));
    }

    [StructLayout(LayoutKind.Sequential)]
    sealed class MemoryStatus
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatus>();
        public uint MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
}
