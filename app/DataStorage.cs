using System.IO.Compression;
using System.Security.Cryptography;

namespace FrostAbyss;

sealed record StorageRequest(string Action, string[]? Groups = null, bool Confirm = false);
sealed record StorageGroup(string Id, string Name, long Bytes, int Files, bool Deletable);
sealed record StorageReport(long TotalBytes, double TotalGiB, StorageGroup[] Groups, DateTimeOffset ScannedAt, string Path);

sealed class DataStorage
{
    readonly string root = System.IO.Path.GetFullPath(Paths.Data);
    readonly object scanGate = new();
    StorageReport? report;
    volatile object progress = new { active = false, message = "" };
    internal object Status() => new { usage = Usage(), progress };
    internal StorageReport Usage(bool refresh = false)
    {
        lock (scanGate)
        {
            if (!refresh && report != null && DateTimeOffset.UtcNow - report.ScannedAt < TimeSpan.FromSeconds(5)) return report;
            var totals = new Dictionary<string, (long Bytes, int Files)>();
            foreach (var file in Files())
            {
                try
                {
                    var key = Group(file.FullName); var old = totals.GetValueOrDefault(key);
                    totals[key] = (old.Bytes + file.Length, old.Files + 1);
                }
                catch (IOException) { /* Maintenance may just have replaced this file. */ }
            }
            var names = new (string Id, string Name, bool Deletable)[]
            {
                ("battles", "历史战报", true), ("decisions", "实战学习记录", true),
                ("reports", "导出报告", true), ("logs", "运行日志", true),
                ("cache", "模型与加载缓存", true), ("session", "上次运行展示记录", true),
                ("model", "已训练神经网络", false), ("settings", "参数与技能备份", false), ("other", "其他文件", false)
            };
            var groups = names.Select(n => new StorageGroup(n.Id, n.Name, totals.GetValueOrDefault(n.Id).Bytes, totals.GetValueOrDefault(n.Id).Files, n.Deletable)).ToArray();
            long total = groups.Sum(g => g.Bytes);
            return report = new(total, total / 1073741824d, groups, DateTimeOffset.UtcNow, root);
        }
    }
    IEnumerable<FileInfo> Files() => Directory.Exists(root) ? new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions
        { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }) : [];
    string Group(string path)
    {
        var relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
        if (relative.StartsWith("battles/", StringComparison.OrdinalIgnoreCase)) return "battles";
        if (relative.StartsWith("learning-v1/decisions/", StringComparison.OrdinalIgnoreCase)) return "decisions";
        if (relative.StartsWith("reports/", StringComparison.OrdinalIgnoreCase)) return "reports";
        if (relative.StartsWith("cache/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("learning-v1/cache/", StringComparison.OrdinalIgnoreCase)) return "cache";
        if (relative.StartsWith("learning-v1/network", StringComparison.OrdinalIgnoreCase)) return "model";
        if (!relative.Contains('/') && relative.StartsWith("pilot-", StringComparison.OrdinalIgnoreCase) && relative.EndsWith(".jsonl")) return "logs";
        if (relative == "last-session.json") return "session";
        if (relative is "settings.json" or "skills-backup.json") return "settings";
        return "other";
    }
    internal string Run(StorageRequest request, CancellationToken ct)
    {
        long freed = 0; int completed = 0;
        bool compress = request.Action == "compact";
        if (!compress && request.Action != "delete") throw new Exception("数据维护操作无效。");
        var groups = request.Groups?.ToHashSet(StringComparer.Ordinal) ?? [];
        var allowed = Usage(true).Groups.Where(g => g.Deletable).Select(g => g.Id).ToHashSet();
        if (!compress && (!request.Confirm || groups.Count == 0 || groups.Any(g => !allowed.Contains(g))))
            throw new Exception("请选择可删除的数据并确认；参数和已训练网络不能通过此操作删除。");
        var files = Files().Where(f => compress ? Group(f.FullName) is "battles" or "decisions" && f.Extension == ".json" : groups.Contains(Group(f.FullName))).ToArray();
        progress = new { active = true, completed, total = files.Length, freedBytes = freed, message = compress ? "正在无损压缩历史数据" : "正在删除所选数据" };
        try
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (compress) freed += Compact(file, ct);
                else { long length = file.Length; File.Delete(file.FullName); freed += length; }
                completed++;
                progress = new { active = true, completed, total = files.Length, freedBytes = freed, message = compress ? "正在无损压缩历史数据" : "正在删除所选数据" };
            }
            if (!compress && groups.Any(g => g is "battles" or "decisions"))
            {
                // Removing evidence must not resurrect its derived model at restart.
                foreach (var file in Files().Where(f => Group(f.FullName) == "cache")) File.Delete(file.FullName);
            }
            return $"{(compress ? "压缩" : "删除")}完成：处理 {completed:N0} 个文件，释放 {freed / 1073741824d:F2} GB（{freed / 1048576d:F1} MB）。";
        }
        finally
        {
            progress = new { active = false, completed, total = files.Length, freedBytes = freed, message = $"已处理 {completed:N0} / {files.Length:N0} 个文件" };
            Usage(true);
        }
    }
    internal void Validate(StorageRequest request)
    {
        if (request.Action == "compact") return;
        var allowed = Usage().Groups.Where(g => g.Deletable).Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        if (request.Action != "delete" || !request.Confirm || request.Groups is not { Length: > 0 } groups || groups.Any(g => !allowed.Contains(g)))
            throw new Exception("请选择可删除的数据并确认；参数和已训练网络不能通过此操作删除。");
    }
    static long Compact(FileInfo source, CancellationToken ct)
    {
        string target = source.FullName + ".gz", temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        if (File.Exists(target)) return 0;
        long length = source.Length, modified = source.LastWriteTimeUtc.Ticks;
        try
        {
            byte[] original;
            using (var input = source.OpenRead()) original = SHA256.HashData(input);
            using (var input = source.OpenRead())
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest)) input.CopyTo(gzip);
            ct.ThrowIfCancellationRequested();
            using (var packed = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var unpacked = new GZipStream(packed, CompressionMode.Decompress))
                if (!original.SequenceEqual(SHA256.HashData(unpacked))) throw new IOException("压缩校验失败，已保留原文件。");
            source.Refresh();
            if (source.Length != length || source.LastWriteTimeUtc.Ticks != modified) throw new IOException("数据在压缩期间发生变化，已保留原文件。");
            if (new FileInfo(temp).Length >= length) return 0;
            File.Move(temp, target); File.SetLastWriteTimeUtc(target, new DateTime(modified, DateTimeKind.Utc));
            File.Delete(source.FullName);
            return length - new FileInfo(target).Length;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
