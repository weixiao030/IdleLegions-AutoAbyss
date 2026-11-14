using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FrostAbyss;

static class PerformanceBenchmark
{
    static void Main(string[] args)
    {
        if (args[0] == "--verify-storage")
        {
            StorageTests.Run((ok, name) => { if (!ok) throw new Exception(name); Console.WriteLine(name); });
            return;
        }
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        T Measure<T>(string name, Func<T> action)
        {
            var clock = Stopwatch.StartNew(); var value = action();
            rows.Add(new { name, seconds = clock.Elapsed.TotalSeconds });
            Console.WriteLine($"{name}: {clock.Elapsed.TotalSeconds:F3}s");
            return value;
        }
        // Read real history, but never connect, start Pilot, dispatch, or train.
        var pilot = Measure("controllerConstruction", () => new Pilot());
        var simulation = new Simulation(false);
        var memory = new SearchMemory();
        Measure("historyLoad", () => { memory.EnsureLoaded(simulation, CancellationToken.None); return memory.Status(); });
        memory.PrepareSearch();
        var fixture = SimulationSafetyTests.Fixture();
        var predictions = new List<object>();
        foreach (int strategy in Enumerable.Range(1, 7))
        {
            var cfg = new Settings { StrategyId = strategy, Simulations = 2000,
                Search = new() { Iterations = 128, AdaptiveIterations = 128, NeuralIterations = 128, Horizon = 6, Workers = 4 } };
            var state = fixture.DeepClone();
            var choices = TargetRules.Choices(state, state.At("sima", "run")!, "room", true);
            Measure("strategy" + strategy, () =>
            {
                if (strategy == 2) new Legacy131.Simulation(false).Evaluate(choices, state, cfg, CancellationToken.None);
                else if (strategy >= 5)
                {
                    choices = new SearchPlanner(memory).Rank(choices, state, cfg, simulation, CancellationToken.None);
                    simulation.Evaluate([choices[0]], state, cfg, CancellationToken.None);
                }
                else simulation.Evaluate(choices, state, cfg, CancellationToken.None);
                return 0;
            });
            predictions.Add(new { strategy, forecasts = choices.Select(c => new { key = SearchPlanner.Key(c), c.Forecast,
                search = c.Search == null ? null : new { c.Search.Visits, c.Search.Value, c.Search.TotalIterations } }) });
        }
        var json = JsonSerializer.Serialize(predictions, J.Options);
        J.Write(Path.Combine(output, "performance.json"), new { processors = Environment.ProcessorCount,
            simulation.Battles, rows, predictions, signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))) });
    }
}
