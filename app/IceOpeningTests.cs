using System.Text.Json.Nodes;
namespace FrostAbyss;
static class IceOpeningTests
{
    internal static JsonNode Card(string id, string family, string desc = "+10% de daño") => new JsonObject { ["id"] = id, ["familia"] = family, ["desc"] = desc };
    internal static JsonObject State(params JsonNode[] cards) => new() { ["profundidad"] = 8, ["reliquias"] = new JsonArray(cards.Select(c=>c.DeepClone()).ToArray()), ["llegada"] = new JsonObject { ["reliquias"] = cards.Length } };
    public static void Run(Action<bool,string> check)
    {
        var old=Paths.Data;
        try
        {
            Paths.Data=Path.Combine(old,"opening-v151");
            var glacier=Card("es_glaciar","escarcha","Tus autos CONGELAN, y cada 5 tu golpe salta a todos");
            var freeze=Card("es_carambano","escarcha","Tus autoataques CONGELAN al objetivo");
            var ice=Card("es_gain_fixture","escarcha");var fire=Card("br_gain_fixture","brasa","+100% de daño");
            foreach(int strategy in new[]{1,3,4})
            {
                var cfg=new Settings{StrategyId=strategy,Simulations=0,SkipShieldCardsEarly=false};var pilot=new Pilot();pilot.Save(cfg);
                foreach(string method in new[]{"sima.reliquia","sima.blason","sima.guarda","sima.trade"})
                {
                    Choice Pick(JsonNode run,params JsonNode[] cs)=>Pilot.Rank(pilot.CardChoices(cs,method,run))[0];
                    foreach(var state in new[]{State(),State(fire),State(freeze)})
                    {
                        check(Pick(state,fire,ice).Card.At("id").S()=="es_gain_fixture",$"策略{strategy} {method}不足4冰时补冰，旧火或普通冻结不提前解除");
                        check(Pick(state,freeze,glacier,fire).Card.At("id").S()=="es_glaciar",$"策略{strategy} {method}冻结加5次弹跳冰川优先");
                    }
                    check(Pick(State(freeze),freeze,ice).Card.At("id").S()=="es_gain_fixture",$"策略{strategy} {method}普通冻结不重复置顶");
                    var onlyBadge=State();onlyBadge["blasones"]=new JsonArray(freeze.DeepClone());
                    check(Pick(onlyBadge,freeze,ice).Card.At("id").S()=="es_gain_fixture",$"策略{strategy} {method}真实徽章冻结作为新增收益基线");
                    var transition=pilot.CardChoices([fire],method,State());
                    check(method=="sima.trade"?transition.Length==0:transition.Length==1,$"策略{strategy} {method}无冰强制过渡、商店正常离开");
                    var newGlacier=State();newGlacier["reliquias"]!.AsArray().Add(glacier.DeepClone());
                    check(Pick(newGlacier,fire,ice).Card.At("id").S()=="es_gain_fixture",$"策略{strategy} {method}本层冰川与实际继承基线分开");
                    var capped=glacier.DeepClone();capped["llevas"]=3;
                    if(method=="sima.guarda")check(Pick(State(),capped,ice).Card.At("id").S()=="es_gain_fixture",$"策略{strategy}开局也遵守继承上限");
                }
                var four=State(ice,ice,ice,ice);
                check(SynergySelection.OpeningComplete(four,cfg)&&!CardRules.HasInheritedCore(four,cfg),$"策略{strategy}冰4达标不冒充实际冻结");
                check(Pilot.Rank(pilot.CardChoices([ice,fire],"sima.reliquia",four))[0].Card.At("id").S()=="br_gain_fixture",$"策略{strategy}冰4后满系让位");
            }
            check(Cards.IsCore(Card("es4_hielo","escarcha"),new())&&!Cards.IsCore(Card("es6_glaciar","escarcha"),new()),"黑冰冻结与四张冰川减速弹跳不混淆");
        }
        finally{Paths.Data=old;}
    }
}
