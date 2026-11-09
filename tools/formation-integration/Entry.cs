using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json.Nodes;

namespace FrostAbyss;

// Serves the real production HTTP handler and embedded UI against an isolated
// profile. It never connects to the user's game or writes their settings.
static class FormationIntegration
{
    static async Task Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]);
        Paths.Data = Path.Combine(root, "profile");
        Paths.GameData = Path.Combine(root, "game-fixture");
        Directory.CreateDirectory(root);
        var pilot = new Pilot();
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var origin = $"http://127.0.0.1:{port}";
        const string key = "formation-integration";
        File.WriteAllText(Path.Combine(root, "integration-url.txt"), origin + "/#" + key);
        using var server = new HttpListener();
        server.Prefixes.Add(origin + "/"); server.Start();
        var handle = typeof(Program).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Static)!;
        Console.WriteLine("Formation integration host ready");
        while (true)
        {
            var context = await server.GetContextAsync();
            // Deterministic disconnected/active/ongoing-run states for UI checks.
            if (context.Request.Url!.AbsolutePath == "/test/state")
            {
                var active = context.Request.QueryString["active"] == "1";
                typeof(Pilot).GetField("running", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pilot, active ? 1 : 0);
                var owned = FormationRules.Professions.Select(p => (JsonNode)new JsonObject { ["id"] = "fixture-" + p.Id, ["defId"] = p.Id }).ToArray();
                var run = context.Request.QueryString["run"] == "1" ? new JsonObject { ["heroes"] = new JsonArray(), ["profundidad"] = 3, ["nodo"] = "fixture" } : null;
                J.Write(Path.Combine(Paths.Bridge, "snapshot.json"), new JsonObject { ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0, ["loggedIn"] = true, ["heroes"] = new JsonArray(owned), ["sima"] = new JsonObject { ["run"] = run } });
                context.Response.StatusCode = 204; context.Response.Close(); continue;
            }
            await (Task)handle.Invoke(null, [context, pilot, key, origin])!;
        }
    }
}
