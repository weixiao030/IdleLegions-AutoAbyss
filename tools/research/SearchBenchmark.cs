using System.Diagnostics;
using System.Text.Json.Nodes;
namespace FrostAbyss;
static class SearchBenchmark
{
 static void Main(string[] args)
 {
  string output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
  var clock=Stopwatch.StartNew(); var sim=new Simulation(false);
  var memory=new SearchMemory(Path.Combine(output,"learning"));memory.EnsureLoaded(sim,CancellationToken.None);memory.PrepareSearch();
  double loadSeconds=clock.Elapsed.TotalSeconds;
  var rows=new List<object>();
  var source=Directory.GetFiles(Path.Combine(Paths.Data,"battles"),"*.json",SearchOption.AllDirectories)
    .Select(J.Read).Select(x=>x.At("authoritativeBattle")??x)
    .Where(b=>b.At("before","profundidad").N() is >=2 and <=4 && b.At("context","heroes").A().Any()).Last();
  var snapshot=ModelAudit.HistoricalSnapshot(source!)!;
  foreach(int strategy in new[]{5,6,7})
  {
   var cfg=new Settings{StrategyId=strategy,Simulations=32}; var actions=SearchWorld.LegalActions(snapshot,cfg);
   using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(45));clock.Restart();
   try
   {
    var ranked=new SearchPlanner(memory).Rank(actions,snapshot,cfg,sim,limit.Token);
    rows.Add(new{strategy,seconds=clock.Elapsed.TotalSeconds,budget=cfg.Search.Budget(strategy),actions=actions.Length,completed=true,estimate=ranked[0].Search});
   }
   catch(OperationCanceledException){rows.Add(new{strategy,seconds=clock.Elapsed.TotalSeconds,budget=cfg.Search.Budget(strategy),actions=actions.Length,completed=false,cancelled=true});}
   Console.WriteLine($"strategy {strategy}: {clock.Elapsed.TotalSeconds:F2}s");
  }
  J.Write(Path.Combine(output,"benchmark.json"),new{processors=Environment.ProcessorCount,loadSeconds,learnedBattles=sim.Battles,depth=snapshot.At("sima","run","profundidad").N(),rows});
 }
}
