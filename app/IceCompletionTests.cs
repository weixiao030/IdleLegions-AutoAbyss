using System.Text.Json;
using System.Text.Json.Nodes;
namespace FrostAbyss;
static class IceCompletionTests
{
    public static void Run(Action<bool,string> check)
    {
        var old=Paths.Data;
        try
        {
            Paths.Data=Path.Combine(old,"completion-v151");
            JsonNode Card(string id,string f,string d="+1% de daño")=>IceOpeningTests.Card(id,f,d);
            var glacier=Card("es_glaciar","escarcha");
            foreach(int strategy in new[]{1,3,4})foreach(string method in new[]{"sima.reliquia","sima.guarda"})
            {
                var cfg=new Settings{StrategyId=strategy,Simulations=0,SkipShieldCardsEarly=false};var pilot=new Pilot();pilot.Save(cfg);
                foreach(string family in IceStrategyRules.Families)
                {
                    string other=IceStrategyRules.Families.First(f=>f!=family&&f!="escarcha");
                    var held=IceOpeningTests.State([glacier,..Enumerable.Range(0,4).Select(i=>Card($"held_{family}_{i}",family))]);
                    var lure=Card("lure",family,"+999% de daño");var missing=Card("missing",other);
                    var cs=pilot.CardChoices([lure,missing],method,held);
                    check(Pilot.Rank(cs)[0].Card.At("id").S()=="missing",$"策略{strategy} {method} {family}满4让位未满系，即使满系提供高增伤");
                }
                var run=IceOpeningTests.State(glacier);int selected=0;
                while(!SynergySelection.Complete(run,cfg,method)&&selected<40)
                {
                    var offers=IceStrategyRules.Families.Select(f=>Card($"next_{selected}_{f}",f)).ToArray();
                    var before=run.ToJsonString();var pick=Pilot.Rank(pilot.CardChoices(offers,method,run))[0];
                    check(before==run.ToJsonString()&&run.At("reliquias").A().Count(c=>Cards.Family(c)==Cards.Family(pick.Card))<4,$"策略{strategy} {method}连续补系第{selected+1}选不抢满系且不改原快照");
                    run["reliquias"]!.AsArray().Add(pick.Card!.DeepClone());run["llegada"]!["reliquias"]=run.At("reliquias").A().Count();selected++;
                }
                check(selected==27&&SynergySelection.Complete(run,cfg,method),$"策略{strategy} {method}供应充分时27次补齐七系各4张");
                cfg.CustomStrategy.SelectionMode="family";cfg.CustomStrategy.FamilyOrder=IceStrategyRules.Families.Reverse().ToArray();pilot.Save(cfg);
                var low=Card("low",cfg.CustomStrategy.FamilyOrder[0]);var high=Card("high",cfg.CustomStrategy.FamilyOrder[^1],"+90% de daño");
                var after=pilot.CardChoices([low,high],method,run);
                check(after.All(c=>c.Priority==0)&&Pilot.Rank(after)[0].Card.At("id").S()=="high",$"策略{strategy} {method}七系满4忽略手动系列顺序，选择新增收益");
                var temporary=IceOpeningTests.State(glacier);temporary["blasones"]=new JsonArray(Card("badge","brasa"));temporary["buffs"]=new JsonArray(Card("buff","fulgor"));
                var progress=JsonSerializer.SerializeToNode(IceStrategyRules.Progress(temporary,cfg),J.Options)!;
                check(progress.A().Sum(c=>c.At("count").N())==1&&progress.A().All(c=>c.At("target").N()==4),$"策略{strategy}徽章赐福不计遗物，每系目标4");
                temporary["reliquias"]!.AsArray().Add(Card("new","brasa"));
                check(SynergySelection.Baseline(temporary,"sima.reliquia").Length==2&&SynergySelection.Baseline(temporary,"sima.guarda").Length==1,$"策略{strategy}本层获取与继承数量分开");
                temporary.AsObject().Remove("llegada");
                check(JsonSerializer.SerializeToNode(IceStrategyRules.Progress(temporary,cfg),J.Options)!.A().All(c=>c!["inheritedCount"]==null),$"策略{strategy}无继承证据显示未知");
            }
            var badge=Card("es2_carambano","escarcha");var relic=Card("es_carambano","escarcha");
            var retained=IceOpeningTests.State(relic,relic);retained["blasones"]=new JsonArray(badge.DeepClone());
            check(Cards.RetainedBadges(retained,Cards.Inherited(retained)).Length==1,"已观测冰锥徽章按目录真实2张门槛保留，徽章和遗物ID不混淆");
            retained["llegada"]!["reliquias"]=1;
            check(Cards.RetainedBadges(retained,Cards.Inherited(retained)).Length==0,"实际继承不足门槛不虚构保留冻结徽章");
        }
        finally{Paths.Data=old;}
    }
}
