using System.Text.Json.Nodes;
namespace FrostAbyss;

static class ModelAudit
{
    public static int Run()
    {
        var source = Directory.GetFiles(Path.Combine(Paths.Data,"battles"),"*.json").Select(J.Read).Where(x=>x!=null).OrderBy(x=>x.At("time").N()).ToArray();
        var global = J.Read(Path.Combine(Paths.Bridge,"snapshot.json"));
        var model = new Simulation(false); var rows = new List<object>();
        var cfg = new Settings { Simulations=2000 };
        int train = Math.Max(10,source.Length/2);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<source.Length;i++)
        {
            var b=source[i]!;
            if(i>=train)
            {
                var before=b.At("before")!; string node=before.At("nodo").S();
                var actualTargets=new JsonObject();
                foreach(var h in before.At("heroes").A())
                {
                    var first=b.At("result","events").A().FirstOrDefault(e=>e.At("side").S()=="ally"&&e.At("src").S()==h.At("nombre").S()&&e.At("dmg").N()>0&&!e.At("tick").B());
                    if(first!=null) actualTargets[h.At("nombre").S()]=first.At("dst").S();
                }
                var s=new JsonObject { ["heroes"]=global.At("heroes")?.DeepClone(),["sima"]=new JsonObject{["run"]=before.DeepClone()},["peeks"]=new JsonObject{[node]=b.At("vista")?.DeepClone()} };
                var c=new Choice("historical action","sima.fight",new(){["objetivos"]=actualTargets});
                model.Evaluate([c],s,cfg,CancellationToken.None);
                var f=c.Forecast!; bool win=b.At("result","win").B();
                rows.Add(new { time=b.At("time").N(),depth=before.At("profundidad").N(),node,predicted=f.Win,actual=win,brier=Math.Pow(f.Win-(win?1:0),2),duration=f.Duration,actualDuration=b.At("result","durationSec").N(),f.Frozen,f.KnownEnemies,trials=f.Trials });
            }
            // Learn only after scoring this observation. Current account skills
            // are the best available proxy for historical skills, a known limit.
            model.Learn(new JsonObject{["lastBattle"]=b.DeepClone()},false);
        }
        J.Write(Path.Combine(Paths.Data,"model-audit.json"),new { ok=true,trainingBattles=train,validationBattles=rows.Count,elapsedSec=clock.Elapsed.TotalSeconds,method="chronological prequential; train only on past battles; present hero skills approximate historical loadout; selected targets inferred from first observed hit",rows });
        return 0;
    }
}
