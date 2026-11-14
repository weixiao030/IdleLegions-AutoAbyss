using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FrostAbyss;

// Disposable derived data only. Source receipts and decisions remain authoritative.
// Both changed and deleted sources invalidate the corresponding checkpoint.
static class HistoryCache
{
    internal sealed record Stamp(string Path, long Length, long Modified);
    sealed record Envelope(int Schema, string Hash, string Json);
    internal static Stamp[] Scan(string directory, SearchOption option = SearchOption.TopDirectoryOnly) =>
        J.HistoryFiles(directory, option).Select(StampFor).ToArray();
    internal static Stamp StampFor(string logicalPath)
    {
        var file = new FileInfo(File.Exists(logicalPath) ? logicalPath : logicalPath + ".gz");
        long length = file.Length;
        if (file.Extension == ".gz")
        {
            // Gzip's trailer records original size. Compression preserves the
            // source timestamp, so lossless packing need not retrain the model.
            using var stream = file.OpenRead();
            if (stream.Length >= 18) { stream.Seek(-4, SeekOrigin.End); Span<byte> size = stackalloc byte[4]; stream.ReadExactly(size); length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(size); }
        }
        return new(logicalPath, length, file.LastWriteTimeUtc.Ticks);
    }
    internal static bool Unchanged(Stamp[] previous, Stamp[] current) =>
        previous.Length == current.Length && Additions(previous, current) is { Length: 0 };
    internal static Stamp[]? Additions(Stamp[] previous, Stamp[] current)
    {
        var known = current.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        if (previous.Any(f => !known.TryGetValue(f.Path, out var now) || now != f)) return null;
        var old = previous.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return current.Where(f => !old.Contains(f.Path)).ToArray();
    }
    internal static T? Read<T>(string path) where T : class
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), J.Options);
            if (envelope?.Schema != 1 || envelope.Json == null || envelope.Hash != Hash(envelope.Json)) return null;
            return JsonSerializer.Deserialize<T>(envelope.Json, J.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    internal static void Write<T>(string path, T value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, J.Options);
            J.Write(path, new Envelope(1, Hash(json), json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* A cache cannot prevent running. */ }
    }
    static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
