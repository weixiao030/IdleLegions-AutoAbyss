using System.Text.Json.Nodes;
namespace FrostAbyss;

static class ModelAudit
{
    internal static JsonNode? HistoricalSnapshot(JsonNode b)
    {
        var before = b.At("before"); if (before == null) return null;
        var ids = before.At("heroes").A().Select(h => h.At("id").S()).ToHashSet();
        var historical = b.At("context", "heroes").A().Where(h => ids.Contains(h.At("id").S())).ToArray();
        if (historical.Length != ids.Count || historical.Any(h => h.At("stats") is not JsonObject || h.At("skills") is not JsonArray)) return null;
        return new JsonObject { ["heroes"] = new JsonArray(historical.Select(h => h.DeepClone()).ToArray()),
            ["sima"] = new JsonObject { ["run"] = before.DeepClone() },
            ["peeks"] = new JsonObject { [before.At("nodo").S()] = b.At("vista")?.DeepClone() } };
    }
    public static int Run(string[]? args = null)
    {
        args ??= [];
        string Arg(string key, string fallback) => args.SkipWhile(a => a != key).Skip(1).FirstOrDefault() ?? fallback;
        int trials = int.Parse(Arg("--audit-trials", "2000")), limit = int.Parse(Arg("--audit-max", int.MaxValue.ToString()));
        if (trials <= 0 || limit < 2) throw new Exception("历史验证需要正数抽样次数和至少两场战斗。");
        string input = Arg("--audit-input", Path.Combine(Paths.Data, "battles"));
        var source = J.HistoryFiles(input, SearchOption.AllDirectories).Select(J.Read)
            .Select(x => x.At("authoritativeBattle") ?? x).Where(x => x.At("before") != null && x.At("result") != null)
            .GroupBy(x => x.At("commandId").S(x.At("time").S()) + ":" + x.At("before", "id").S()).Select(g => g.First()!)
            .OrderBy(x => x.At("time").N()).TakeLast(limit).ToArray();
        var model = new Simulation(false); var rows = new List<object>();
        var cfg = new Settings { Simulations=trials };
        int train = Math.Max(1,source.Length/2), skipped = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<source.Length;i++)
        {
            var b=source[i]!;
            if(i>=train)
            {
                var s = HistoricalSnapshot(b);
                if (s == null) { skipped++; model.Learn(new JsonObject { ["lastBattle"] = b.DeepClone() }, false); continue; }
                var before=b.At("before")!; string node=before.At("nodo").S();
                var actualTargets=new JsonObject();
                foreach(var h in before.At("heroes").A())
                {
                    var first=b.At("result","events").A().FirstOrDefault(e=>e.At("side").S()=="ally"&&e.At("src").S()==h.At("nombre").S()&&e.At("dmg").N()>0&&!e.At("tick").B());
                    if(first!=null) actualTargets[h.At("nombre").S()]=first.At("dst").S();
                }
                var c=new Choice("historical action","sima.fight",new(){["objetivos"]=actualTargets});
                model.Evaluate([c],s,cfg,CancellationToken.None);
                var f=c.Forecast!; bool win=b.At("result","win").B();
                rows.Add(new { time=b.At("time").N(),depth=before.At("profundidad").N(),node,predicted=f.Win,actual=win,brier=Math.Pow(f.Win-(win?1:0),2),duration=f.Duration,actualDuration=b.At("result","durationSec").N(),f.Frozen,f.KnownEnemies,trials=f.Trials });
            }
            // Score before learning this battle. Historical account skills and
            // stats come only from the immutable pre-command context.
            model.Learn(new JsonObject{["lastBattle"]=b.DeepClone()},false);
        }
        string output = Arg("--audit-output", Path.Combine(Paths.Data, "model-audit.json"));
        if (File.Exists(output)) File.Copy(output, output + ".previous", true);
        J.Write(output,new { ok=rows.Count>0,trainingBattles=train,validationBattles=rows.Count,skippedMissingContext=skipped,elapsedSec=clock.Elapsed.TotalSeconds,method="chronological prequential; immutable historical hero context only; missing context excluded; selected targets inferred from first observed hit",rows });
        return rows.Count > 0 ? 0 : 1;
    }
}
