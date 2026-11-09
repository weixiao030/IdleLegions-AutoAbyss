using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace FrostAbyss;

sealed class Bridge
{
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
                return lastGood;
            }
        }
    }
    public bool Fresh(JsonNode? s) => s != null && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - s.At("time").N() < 15;
    public async Task<JsonNode> Send(string method, JsonObject args, CancellationToken ct)
    {
        if (method == "sima.extract") throw new Exception("禁止撤离：此程序不执行撤离操作。");
        await gate.WaitAsync(ct);
        try
        {
            var s = Snapshot;
            if (!Fresh(s)) throw new Exception("游戏桥接未连接或快照已过期，请先安装桥接并启动游戏。");
            if (s.At("fighting").B() && method != "local.skipReplay") throw new Exception("战斗播放中，请等待结束。");
            var id = Guid.NewGuid().ToString("N");
            var key = File.ReadAllText(Path.Combine(Paths.Bridge, "session.key")).Trim();
            J.Write(Path.Combine(Paths.Bridge, "command.json"), new { id, key, method, args, issuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0, runId = s.At("sima", "run", "id").S(), nodeId = s.At("sima", "run", "nodo").S() });
            // Once dispatched, finish observing the receipt even when the user pauses.
            for (int i = 0; i < 240; i++)
            {
                await Task.Delay(250);
                var r = J.Read(Path.Combine(Paths.Bridge, "response.json"));
                if (r.At("id").S() != id) continue;
                if (!r.At("ok").B()) throw new Exception(r.At("error").S("游戏拒绝操作"));
                // Let the bridge publish the matching post-operation snapshot.
                for (int j = 0; j < 150; j++) { if (Snapshot.At("lastCommand", "id").S() == id) return r!; await Task.Delay(100); }
                throw new Exception("回执成功，但状态快照未同步，已暂停。请检查游戏后再继续。");
            }
            throw new Exception("60 秒未收到回执。操作结果未知，已暂停且不会自动重试，请检查游戏。");
        }
        finally { gate.Release(); }
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
