using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace FrostAbyss;

sealed class Bridge
{
    public const string BundledVersion = "0.3.5";
    internal TimeSpan[] RecoveryDelays { get; init; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(15)];
    internal TimeSpan RecoveryTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public static bool NeedsUpdate(JsonNode? snapshot) => snapshot != null && (!Version.TryParse(snapshot.At("version").S(), out var version) || version < Version.Parse(BundledVersion));
    readonly SemaphoreSlim gate = new(1, 1);
    readonly object readLock = new();
    JsonNode? lastGood;
    public JsonNode? Snapshot
    {
        get
        {
            lock (readLock)
            {
                // Godot replaces its snapshot atomically. Windows readers can still
                // see a brief sharing/rename failure; retain the last valid frame.
                var next = J.Read(Path.Combine(Paths.Bridge, "snapshot.json"));
                if (next != null) lastGood = next;
                // Each caller owns its tree. Status rendering and simulation must not
                // materialize or enumerate the same lazily parsed JsonNode.
                return lastGood?.DeepClone();
            }
        }
    }
    public bool Fresh(JsonNode? s) => s != null && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - s.At("time").N() < 15;
    public bool RetryTimeouts { get; set; } = true;
    public Action<string>? Notice { get; set; }
    public async Task<JsonNode> Send(string method, JsonObject args, CancellationToken ct)
    {
        if (method == "sima.extract") throw new Exception("禁止撤离：此程序不执行撤离操作。");
        await gate.WaitAsync(ct);
        try
        {
            var before = Snapshot?.DeepClone();
            var result = await Dispatch(method, args, ct);
            if (result.At("ok").B()) return result;
            if (!RetryTimeouts || !Recoverable(result, method)) throw Failure(result, method);
            // Unknown outcomes are reconciled with read-only state, never replayed.
            // A command is resent only when the bridge proves it was not dispatched.
            for (int attempt = 1; attempt <= 2 && (result.At("retryable").B() || method == "sima.peek"); attempt++)
            {
                Notice?.Invoke($"{method} 回执异常，重试 {attempt}/2：仅重试明确未发出或只读的请求。");
                await Task.Delay(RecoveryDelays[0], ct);
                result = await Dispatch(method, args, ct);
                if (result.At("ok").B()) return result;
                if (!Recoverable(result, method)) throw Failure(result, method);
            }
            if (result.At("retryable").B() || method == "sima.peek") throw Failure(result, method);
            var recovery = Stopwatch.StartNew();
            string lastRefreshError = "";
            for (int attempt = 0; attempt < RecoveryDelays.Length && recovery.Elapsed < RecoveryTimeout; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var after = Snapshot;
                if (Fresh(after) && RecoveryRules.Completed(method, args, before, after, result.At("id").S()))
                {
                    Notice?.Invoke(method + " 已通过最新游戏状态核验完成，继续下潜。");
                    return new JsonObject { ["ok"] = true, ["confirmed"] = true, ["recovered"] = true, ["id"] = result.At("id").S() };
                }
                Notice?.Invoke($"{method} 回执异常，状态核验 {attempt + 1}/{RecoveryDelays.Length}：等待游戏同步，只查询状态，不重复执行操作。");
                var remaining = RecoveryTimeout - recovery.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(RecoveryDelays[attempt] < remaining ? RecoveryDelays[attempt] : remaining, ct);
                if (recovery.Elapsed >= RecoveryTimeout) break;
                // A busy bridge is still finishing the original command. Do not
                // overwrite its mailbox or mistake the last cached state for a refusal.
                after = Snapshot;
                if (Fresh(after) && RecoveryRules.Completed(method, args, before, after, result.At("id").S()))
                {
                    Notice?.Invoke(method + " 已通过最新游戏状态核验完成，继续下潜。");
                    return new JsonObject { ["ok"] = true, ["confirmed"] = true, ["recovered"] = true, ["id"] = result.At("id").S() };
                }
                if (!Fresh(after) || after.At("busy").B()) continue;
                remaining = RecoveryTimeout - recovery.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                var refresh = await Dispatch("local.refresh", new(), ct, remaining);
                lastRefreshError = refresh.At("ok").B() ? "" : refresh.At("error").S();
                after = Snapshot;
                if (refresh.At("ok").B() && Fresh(after) && RecoveryRules.Completed(method, args, before, after, result.At("id").S()))
                {
                    Notice?.Invoke(method + " 已通过最新游戏状态核验完成，继续下潜。");
                    return new JsonObject { ["ok"] = true, ["confirmed"] = true, ["recovered"] = true, ["id"] = result.At("id").S() };
                }
            }
            var versionHint = NeedsUpdate(Snapshot) ? $" 当前游戏桥接 {Snapshot.At("version").S("未知")}，请正常关闭游戏，在连接设置更新至 {BundledVersion} 后重新登录。" : "";
            throw new UnknownOutcomeException(method + " 等待状态同步后仍无法确认结果，已暂停并保留深渊进度。请在游戏中核对后继续。" + versionHint + " 原始原因：" + result.At("error").S() + (lastRefreshError == "" ? "" : "；状态查询：" + lastRefreshError));
        }
        finally { gate.Release(); }
    }
    static bool Recoverable(JsonNode result, string method) => result.At("retryable").B()
        || result.At("code").S() is "receipt_missing" or "transport_timeout"
        || result.At("error").S().Contains("未收到操作回执")
        || method == "sima.peek" && (result.At("error").S().Contains("respuesta") || result.At("error").S().Contains("Conexión"));
    static Exception Failure(JsonNode result, string method) => new(method + "：" + result.At("error").S("游戏拒绝操作"));
    async Task<JsonNode> Dispatch(string method, JsonObject args, CancellationToken ct, TimeSpan? timeout = null)
    {
        ct.ThrowIfCancellationRequested();
        var s = Snapshot;
        if (!Fresh(s)) throw new Exception("游戏桥接未连接或快照已过期，请检查游戏连接后继续。");
        if (s.At("fighting").B() && method is not ("local.skipReplay" or "local.refresh")) throw new Exception("战斗播放中，请等待结束。");
        var id = Guid.NewGuid().ToString("N");
        var key = File.ReadAllText(Path.Combine(Paths.Bridge, "session.key")).Trim();
        J.Write(Path.Combine(Paths.Bridge, "command.json"), new { id, key, method, args, issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0, runId = s.At("sima", "run", "id").S(), nodeId = s.At("sima", "run", "nodo").S() });
        // Once dispatched, observe the receipt even when the user pauses.
        var elapsed = Stopwatch.StartNew();
        var deadline = timeout ?? TimeSpan.FromSeconds(60);
        while (elapsed.Elapsed < deadline)
        {
            await Task.Delay(250);
            var r = J.Read(Path.Combine(Paths.Bridge, "response.json"));
            if (r.At("id").S() != id) continue;
            // Both failure and success need their post-operation snapshot before recovery.
            for (int j = 0; j < 150; j++)
            {
                if (Snapshot.At("lastCommand", "id").S() == id) return r!;
                if (elapsed.Elapsed >= deadline) break;
                await Task.Delay(100);
            }
            return new JsonObject { ["ok"] = false, ["id"] = id, ["code"] = "transport_timeout", ["error"] = "回执已返回，但状态快照未同步。" };
        }
        return new JsonObject { ["ok"] = false, ["id"] = id, ["code"] = "transport_timeout", ["error"] = $"{deadline.TotalSeconds:0.#}秒未收到桥接回执，操作结果未知。" };
    }
    public static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new Exception("缺少内嵌资源 " + name);
        using var reader = new StreamReader(stream, Encoding.UTF8); return reader.ReadToEnd();
    }
    public string Discover(string configured)
    {
        if (File.Exists(configured) && Path.GetFileName(configured).Equals("client.exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(configured);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath")?.ToString();
        if (steam != null) roots.Add(steam);
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady)) roots.Add(Path.Combine(d.RootDirectory.FullName, "SteamLibrary"));
        foreach (var root in roots.ToArray())
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        foreach (var root in roots) { var p = Path.Combine(root, "steamapps", "common", "Idle Legions", "client.exe"); if (File.Exists(p)) return p; }
        throw new Exception("未找到游戏。请在参数中填写 Steam 游戏 client.exe 的完整路径。");
    }
    public bool Running => Process.GetProcessesByName("client").Any(p => { try { return p.MainModule?.FileName?.EndsWith(@"Idle Legions\client.exe", StringComparison.OrdinalIgnoreCase) == true; } catch { return false; } });
    public async Task Install(Settings cfg, bool uninstall = false)
    {
        if (Running) throw new Exception("请先正常关闭 Idle Legions，再安装或卸载桥接。");
        if (!uninstall) _ = Discover(cfg.GamePath);
        Directory.CreateDirectory(Paths.Bridge);
        var path = Path.Combine(Paths.GameData, "ventana_inicio.cfg");
        var text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        if (File.Exists(path)) File.Copy(path, Path.Combine(Paths.Bridge, "ventana_inicio.before-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".cfg"), false);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^FrostAbyss\s*=.*\r?\n?", "");
        if (!uninstall)
        {
            const string line = "FrostAbyss=\"*user://frost_abyss/frost_bridge.gd\"";
            text = text.Contains("[autoload]") ? text.Replace("[autoload]", "[autoload]\n" + line) : text + "\n[autoload]\n" + line + "\n";
            var script = Path.Combine(Paths.Bridge, "frost_bridge.gd");
            if (File.Exists(script)) File.Copy(script, script + ".before-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
            await File.WriteAllTextAsync(script + ".tmp", Resource("bridge.gd"), new UTF8Encoding(false)); File.Move(script + ".tmp", script, true);
            if (!File.Exists(Path.Combine(Paths.Bridge, "session.key")) || File.ReadAllText(Path.Combine(Paths.Bridge, "session.key")).Trim().Length < 32) File.WriteAllText(Path.Combine(Paths.Bridge, "session.key"), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            foreach (var f in new[] { "command.json", "snapshot.json", "response.json" }) if (File.Exists(Path.Combine(Paths.Bridge, f))) File.Delete(Path.Combine(Paths.Bridge, f));
            lock (readLock) lastGood = null;
        }
        await File.WriteAllTextAsync(path + ".frost.tmp", text, new UTF8Encoding(false)); File.Move(path + ".frost.tmp", path, true);
    }
    public void Launch(Settings cfg) { if (Running) return; var p = Discover(cfg.GamePath); Process.Start(new ProcessStartInfo(p) { WorkingDirectory = Path.GetDirectoryName(p)!, UseShellExecute = true }); }
}
