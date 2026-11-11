using System.Text.Json;
using System.Text.Json.Nodes;
namespace FrostAbyss;
static class FreezeCollectionTests
{
    public static void Run(Action<bool,string> check)
    {
        var old=Paths.Data;
        try
        {
            Paths.Data=Path.Combine(old,"light-preset-v151");
            JsonNode Card(string id,string f,string d="+10% de daño")=>IceOpeningTests.Card(id,f,d);
            var glacier=Card("es_glaciar","escarcha");var light=Card("au_other_fixture","aurora","+90% de daño");var fire=Card("br_other_fixture","brasa");
            var fervor=Card("au_fervor","aurora");var litany=Card("au_letania","aurora");
            var cfg=new Settings{StrategyId=4,Simulations=0,SkipShieldCardsEarly=false};var pilot=new Pilot();pilot.Save(cfg);
            var run=IceOpeningTests.State(glacier);
            string Pick(string method,params JsonNode[] cs)=>Pilot.Rank(pilot.CardChoices(cs,method,run))[0].Card.At("id").S();
            check(!cfg.CustomStrategy.LightFervorOnly&&Pick("sima.guarda",light,fire)=="au_other_fixture","光系配置默认关闭，保留收益选牌行为");
            cfg.CustomStrategy.Cards["br_other_fixture"]=new(){Priority=7,TargetCopies=2};
            cfg.CustomStrategy.LightFervorOnly=true;pilot.Save(cfg);
            check(new Pilot().Config.CustomStrategy.LightFervorOnly&&new Pilot().Config.CustomStrategy.Cards["br_other_fixture"].Priority==7,"光系选项保存重开且不覆盖原单卡设置");
            check(Pick("sima.reliquia",light,fire)=="au_other_fixture","光系配置允许本层拿光牌凑羁绊");
            check(Pick("sima.guarda",light,fire)=="br_other_fixture","光系配置章末普通光牌让位别系");
            check(Pick("sima.guarda",fervor,light,fire)=="au_fervor","光系配置优先首份狂热");
            run=IceOpeningTests.State(glacier,fervor);
            check(Pick("sima.guarda",fervor,litany,fire)=="au_letania","已有狂热优先首份连祷，不重复继承狂热");
            run=IceOpeningTests.State(glacier,fervor,litany);
            check(Pick("sima.guarda",fervor,litany,light,fire)=="br_other_fixture","狂热连祷各一份后转补其他系列");
            check(Pick("sima.guarda",light)=="au_other_fixture","强制仅光牌菜单合法过渡，不暂停");
            var bad=light.DeepClone();bad["available"]=false;
            check(Pick("sima.guarda",bad,fire)=="br_other_fixture","光系配置不恢复游戏不可用牌");
            cfg.CustomStrategy.LightFervorOnly=false;pilot.Save(cfg);
            check(Pick("sima.guarda",light,fire)=="au_other_fixture","关闭光系配置恢复原收益比较");
            foreach(int strategy in new[]{1,2,3})
            {
                cfg.StrategyId=strategy;cfg.CustomStrategy.LightFervorOnly=false;pilot.Save(cfg);
                var before=JsonSerializer.Serialize(pilot.CardChoices([light,fire],"sima.guarda",run).Select(c=>new{c.Label,c.Priority,c.Reason,c.Args}),J.Options);
                cfg.CustomStrategy.LightFervorOnly=true;pilot.Save(cfg);
                check(before==JsonSerializer.Serialize(pilot.CardChoices([light,fire],"sima.guarda",run).Select(c=>new{c.Label,c.Priority,c.Reason,c.Args}),J.Options),$"光系配置仅策略四生效，不影响策略{strategy}");
            }
            check(!SynergySelection.LightKeeper(Card("au_duelo","aurora","Un aliado caído da fervor"))&&!SynergySelection.LightKeeper(Card("au_ceniza","aurora")),"盟友倒下狂热不误认为每普攻狂热");
        }
        finally{Paths.Data=old;}
    }
}
