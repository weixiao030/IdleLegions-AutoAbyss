using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FrostAbyss;

static class Program
{
    static volatile bool quit;
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Directory.CreateDirectory(Paths.Data);
        if (args.Contains("--self-test"))
        {
            var testRoot = args.SkipWhile(a => a != "--self-test-dir").Skip(1).FirstOrDefault();
            if (testRoot != null) { Paths.Data = Path.GetFullPath(testRoot); Paths.GameData = Path.Combine(Paths.Data, "game-fixture"); Directory.CreateDirectory(Paths.Data); }
            Environment.Exit(SelfTest.Run(args)); return;
        }
        if (args.Contains("--validate-model")) { Environment.Exit(ModelAudit.Run(args)); return; }
        using var single = new Mutex(true, @"Local\FrostAbyss-Console", out var first);
        if (!first)
        {
            try { Open(File.ReadAllText(Path.Combine(Paths.Data, "console.url"))); } catch { MessageBox.Show("IdleLegions自动下井已在运行，可从系统托盘打开。", "IdleLegions自动下井"); }
            return;
        }
        var pilot = new Pilot();
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var sock = new TcpListener(IPAddress.Loopback, 0); sock.Start(); var port = ((IPEndPoint)sock.LocalEndpoint).Port; sock.Stop();
        var origin = $"http://127.0.0.1:{port}";
        var url = origin + "/#" + key;
        File.WriteAllText(Path.Combine(Paths.Data, "console.url"), url);
        var listener = new HttpListener(); listener.Prefixes.Add(origin + "/");
        try { listener.Start(); } catch (Exception e) { MessageBox.Show("本地控制台启动失败：" + e.Message, "IdleLegions自动下井"); return; }
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开控制台", null, (_, _) => Open(url));
        menu.Items.Add("暂停自动下井", null, (_, _) => pilot.Pause());
        menu.Items.Add("退出", null, (_, _) => { pilot.Pause(); quit = true; });
        using var trayIcon = Branding.LoadIcon(SystemInformation.SmallIconSize);
        using var tray = new NotifyIcon { Visible = true, Icon = trayIcon, Text = "IdleLegions自动下井", ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Open(url);
        var context = new ApplicationContext();
        using var timer = new System.Windows.Forms.Timer { Interval = 300 };
        timer.Tick += (_, _) => { if (quit && !pilot.Active) context.ExitThread(); }; timer.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try { var req = await listener.GetContextAsync(); _ = Handle(req, pilot, key, origin); }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        });
        pilot.Note("控制台已启动。全部七策略优先冰川（单卡冻结＋全体弹跳），没有冰川就凑冰系4张，不单独追普通冻结。确认继承冰川或4张冰系遗物后启用各自方案；本层临时效果不能代替继承回执。随后一、三、四补七系各4张，二使用经典规则，五至七使用多步搜索。所有策略记录已确认实战，CPU/CUDA仅决定网络训练设备。技能调整默认关闭。");
        if (!args.Contains("--no-browser")) Open(url);
        Application.Run(context); listener.Close(); tray.Visible = false;
        try { File.Delete(Path.Combine(Paths.Data, "console.url")); } catch { }
    }
    static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    static async Task Handle(HttpListenerContext c, Pilot pilot, string key, string origin)
    {
        try
        {
            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            c.Response.Headers["Referrer-Policy"] = "no-referrer";
            c.Response.Headers["Cache-Control"] = "no-store";
            c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
            if (c.Request.UserHostName != new Uri(origin).Authority) { c.Response.StatusCode = 403; return; }
            var path = c.Request.Url!.AbsolutePath;
            if (path.StartsWith("/api/"))
            {
                if (c.Request.Headers["X-Frost-Key"] != key || c.Request.Headers["Origin"] is { } o && o != origin) { c.Response.StatusCode = 403; await Json(c, new { ok = false, error = "本地访问凭证无效，请从托盘重新打开控制台。" }); return; }
                if (path == "/api/status" && c.Request.HttpMethod == "GET") { await Json(c, pilot.Status()); return; }
                if (path == "/api/report" && c.Request.HttpMethod == "GET") { await Json(c, pilot.LatestReport()); return; }
                if (c.Request.HttpMethod != "POST") { c.Response.StatusCode = 405; return; }
                if (c.Request.ContentLength64 > 65536) throw new Exception("请求过大。");
                using var reader = new StreamReader(c.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                switch (path)
                {
                    case "/api/formation":
                        pilot.SaveFormation(JsonSerializer.Deserialize<FormationRequest>(body, J.Options)?.Formation);
                        await Json(c, new { ok = true, config = pilot.Config }); return;
                    case "/api/config":
                        pilot.Save(JsonSerializer.Deserialize<Settings>(body, J.Options) ?? throw new Exception("配置为空。"));
                        await Json(c, new { ok = true, config = pilot.Config }); return;
                    case "/api/start": pilot.Start(); break;
                    case "/api/loop": pilot.Start(loop: JsonSerializer.Deserialize<LoopRequest>(body, J.Options) ?? throw new Exception("循环参数为空。")); break;
                    case "/api/analyze": pilot.Start(true); break;
                    case "/api/pause": pilot.Pause(); break;
                    case "/api/install": if (pilot.Active) throw new Exception("请先暂停。"); await pilot.Bridge.Install(pilot.Config); pilot.Note("桥接已安装，配置已备份。请启动游戏。"); break;
                    case "/api/uninstall": if (pilot.Active) throw new Exception("请先暂停。"); await pilot.Bridge.Install(pilot.Config, true); pilot.Note("桥接 autoload 已移除，备份仍保留。"); break;
                    case "/api/launch": pilot.Bridge.Launch(pilot.Config); break;
                    case "/api/restore": await pilot.Restore(); break;
                    case "/api/quit": pilot.Pause(); quit = true; break;
                    default: c.Response.StatusCode = 404; throw new Exception("接口不存在。");
                }
                await Json(c, new { ok = true }); return;
            }
            if (c.Request.HttpMethod != "GET") { c.Response.StatusCode = 405; return; }
            var resourcePath = path == "/" ? "index.html" : Uri.UnescapeDataString(path.TrimStart('/'));
            var asm = Assembly.GetExecutingAssembly();
            var resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == "web/" + resourcePath);
            if (resource == null) { c.Response.StatusCode = 404; return; }
            c.Response.ContentType = Path.GetExtension(resourcePath) switch { ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8", ".svg" => "image/svg+xml", ".ico" => "image/x-icon", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".png" => "image/png", ".woff2" => "font/woff2", _ => "application/octet-stream" };
            using var stream = asm.GetManifestResourceStream(resource)!;
            if (Path.GetExtension(resourcePath) is ".html" or ".js" or ".css" or ".svg")
            {
                c.Response.Headers["Vary"] = "Accept-Encoding";
                if (AcceptsGzip(c.Request.Headers["Accept-Encoding"]))
                {
                    c.Response.Headers["Content-Encoding"] = "gzip";
                    using var compressed = new GZipStream(c.Response.OutputStream, CompressionLevel.Optimal, leaveOpen: true);
                    await stream.CopyToAsync(compressed);
                    return;
                }
            }
            await stream.CopyToAsync(c.Response.OutputStream);
        }
        catch (Exception e) { try { c.Response.StatusCode = 400; await Json(c, new { ok = false, error = e.Message }); } catch { } }
        finally { c.Response.Close(); }
    }
    static bool AcceptsGzip(string? encodings)
    {
        var wildcard = false;
        foreach (var item in (encodings ?? "").Split(','))
        {
            if (!StringWithQualityHeaderValue.TryParse(item, out var encoding)) continue;
            if (encoding.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase)) return encoding.Quality.GetValueOrDefault(1) > 0;
            if (encoding.Value == "*") wildcard = encoding.Quality.GetValueOrDefault(1) > 0;
        }
        return wildcard;
    }
    static async Task Json(HttpListenerContext c, object value)
    {
        c.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(c.Response.OutputStream, value, J.Options);
    }
}
