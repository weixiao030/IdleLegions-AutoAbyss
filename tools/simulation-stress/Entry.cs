using System.Diagnostics;
using System.Text.Json.Nodes;
namespace FrostAbyss;
static class SimulationStress
{
 static async Task Main(string[] args)
 {
  Paths.Data=Path.GetFullPath(args[0]);Paths.GameData=Path.Combine(Paths.Data,"game-fixture");Directory.CreateDirectory(Paths.Data);
  ThreadPool.SetMinThreads(32,32);
  if(args.Contains("--self-test")){Environment.ExitCode=SelfTest.Run(args);return;}
  if(args.Contains("--safety")){var checks=new List<string>();try{await SimulationSafetyTests.Run((ok,name)=>{if(!ok)throw new Exception(name);checks.Add(name);});J.Write(Path.Combine(Paths.Data,"safety.json"),new{ok=true,checks});}catch(Exception e){J.Write(Path.Combine(Paths.Data,"safety.json"),new{ok=false,checks,error=e.ToString()});Environment.ExitCode=1;}return;}
  if(args.Contains("--concurrent"))
  {
   var model=new Simulation(false);var gate=new ManualResetEventSlim();
   var learn=Task.Run(()=>{gate.Wait();for(int i=0;i<3000;i++)model.Learn(SimulationSafetyTests.Battle(i),false);});
   var evals=Enumerable.Range(0,8).Select(_=>Task.Run(()=>{gate.Wait();for(int i=0;i<20;i++){var state=Fixture();var choices=Enumerable.Range(0,24).Select(j=>new Choice("candidate"+j,"sima.fight",new())).ToArray();model.Evaluate(choices,state,new Settings{Simulations=100},CancellationToken.None);}})).ToArray();gate.Set();
   try{await Task.WhenAll(evals.Append(learn));J.Write(Path.Combine(Paths.Data,"concurrent.json"),new{ok=true,model.Battles});}catch(Exception e){J.Write(Path.Combine(Paths.Data,"concurrent.json"),new{ok=false,error=e.ToString()});Environment.ExitCode=1;}return;
  }
  if(args.Contains("--defaults")){J.Write(Path.Combine(Paths.Data,"defaults.json"),new Settings());return;}
  var results=new List<object>();
  foreach(var trials in new[]{100,1000,2000,3000,5000})
  {
   var sw=Stopwatch.StartNew();int passed=0;string? error=null;
   for(int repeat=0;repeat<24;repeat++)
   {
    try
    {
     var snapshot=Fixture();var choices=Enumerable.Range(0,24).Select(i=>new Choice("stress"+i,"sima.rest",new(){["opcion"]="afilar",["heroId"]="h"+(i%4)})).ToArray();
     new Simulation(false).Evaluate(choices,snapshot,new Settings{Simulations=trials},CancellationToken.None);
     if(choices.Any(c=>c.Forecast?.Trials!=trials))throw new Exception("trial count mismatch");passed++;
    }catch(Exception e){error=e.ToString();break;}
   }
   results.Add(new{trials,passed,actualTrials=(long)trials*24*passed,seconds=sw.Elapsed.TotalSeconds,error});if(error!=null)Environment.ExitCode=1;
  }
  J.Write(Path.Combine(Paths.Data,"stress.json"),new{processors=Environment.ProcessorCount,results});Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(results,J.Options));
 }
 internal static JsonNode Fixture() => SimulationSafetyTests.Fixture();
}
