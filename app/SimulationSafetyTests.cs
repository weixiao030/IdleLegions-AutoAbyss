using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace FrostAbyss;
static class SimulationSafetyTests
{
 public static async Task Run(Action<bool,string> check)
 {
  var data=Paths.Data;var game=Paths.GameData;
  Paths.Data=Path.Combine(Path.GetTempPath(),"frost-safety-"+Guid.NewGuid().ToString("N"));Paths.GameData=Path.Combine(Paths.Data,"game");Directory.CreateDirectory(Paths.Data);
  try
  {
   var cfg=new Settings();cfg.Validate();
   check(cfg.Simulations==2000 && cfg.SeparateTargets && cfg.AllowElite && cfg.AllowAmbush && cfg.LightningConsumesFreeze,"新配置默认2000次、分别锁定、精英路线、骨库伏击、纯电清冻结假设开启");
   check(cfg.AvoidEliteIds.SequenceEqual([EliteRules.Catalog.Single(e=>e.Name=="陪葬旗官").Id]) && !cfg.AllowBasicLightning,"默认避让真实陪葬旗官ID，电伤模型假设不联动普攻带电");
   var legacy=JsonSerializer.Deserialize<Settings>("{}",J.Options)!;
   check(legacy.Simulations==2000 && legacy.SeparateTargets && legacy.AllowAmbush && legacy.LightningConsumesFreeze && legacy.AvoidEliteIds.Contains("sg_alferez"),"旧配置缺字段采用新默认");
   var custom=JsonSerializer.Deserialize<Settings>("{\"simulations\":3000,\"allowElite\":false,\"allowAmbush\":false,\"lightningConsumesFreeze\":false,\"avoidEliteIds\":[],\"separateTargets\":false}",J.Options)!;
   var pilot=new Pilot();pilot.Save(custom);var loaded=new Pilot().Config;
   check(loaded.Simulations==3000 && !loaded.SeparateTargets && !loaded.AllowAmbush && !loaded.AllowElite && !loaded.LightningConsumesFreeze && loaded.AvoidEliteIds.Length==0,"显式自定义次数、关闭值与空名单保存重载不被新默认覆盖");
   var snapshot=Fixture();var run=snapshot.At("sima","run")!;
   var choices=TargetRules.Choices(snapshot,run,"room",true);
   check(choices.Length==24 && choices.All(c=>c.Args.At("objetivos")!.AsObject().Select(p=>p.Value.S()).Distinct().Count()==4),"四英雄四敌默认24种一对一分配，没有全员集火候选");
   check(TargetRules.Choices(snapshot,run,"room",false).Length==256,"关闭分别锁定恢复256种自由分配，包含集火");
   var enemies=snapshot.At("peeks","room","enemigos")!.AsArray();enemies.RemoveAt(3);enemies.RemoveAt(2);
   choices=TargetRules.Choices(snapshot,run,"room",true);
   check(choices.Length==6 && choices.All(c=>c.Args.At("objetivos")!.AsObject().GroupBy(p=>p.Value.S()).All(g=>g.Count()==2)),"四英雄两敌六种均衡二对二分配，合法复用不暂停");
   run["heroes"]![1]!["caido"]=true;
   check(TargetRules.Choices(snapshot,run,"room",true).All(c=>c.Args.At("objetivos")!.AsObject().Count==3 && c.Args.At("objetivos","Hero1")==null),"倒地英雄不分配目标，存活队员仍均衡锁定");
   var large=Fixture();var largeFoes=large.At("peeks","room","enemigos")!.AsArray();
   for(int i=4;i<8;i++)largeFoes.Add(new JsonObject{["char"]="foe"+i,["name"]="Enemy"+i});
   var largeOptions=TargetRules.Choices(large,large.At("sima","run")!,"room",true);
   check(largeOptions.Length==1680,"四人八敌分别锁定完整比较1680种分配");
   new Simulation(false).Evaluate(largeOptions,large,new Settings{Simulations=2000},CancellationToken.None);
   check(largeOptions.All(c=>c.Forecast?.Trials==2000),"1680个目标候选各实际模拟2000次，不因候选多而缩水");
   var freeOptions=TargetRules.Choices(large,large.At("sima","run")!,"room",false);
   check(freeOptions.Length==4096 && freeOptions.Any(c=>c.Args.At("objetivos")!.AsObject().Select(p=>p.Value.S()).Distinct().Count()==1),"关闭开关保留4096种自由分配及集火");
   new Simulation(false).Evaluate(freeOptions,large,new Settings{Simulations=3000},CancellationToken.None);
   check(freeOptions.All(c=>c.Forecast?.Trials==3000),"4096个自由目标候选各模拟3000次无异常");
   var model=new Simulation(false);
   foreach(var trials in new[]{100,1000,2000,3000})
   {
    string? previous=null;
    for(int repeat=0;repeat<4;repeat++)
    {
     var state=Fixture();choices=TargetRules.Choices(state,state.At("sima","run")!,"room",true);
     model.Evaluate(choices,state,new Settings{Simulations=trials},CancellationToken.None);
     check(choices.All(c=>c.Forecast is { } f && f.Trials==trials && double.IsFinite(f.Score) && f.Low<=f.Win && f.High>=f.Win),$"{trials}次24候选第{repeat+1}轮实际抽样完整、输出有限");
     var signature=JsonSerializer.Serialize(choices.Select(c=>c.Forecast),J.Options);
     check(previous==null || previous==signature,$"{trials}次24候选第{repeat+1}轮同种子可复现，不受并行调度影响");previous=signature;
    }
   }
   using(var cancel=new CancellationTokenSource())
   {
    var state=Fixture();choices=TargetRules.Choices(state,state.At("sima","run")!,"room",true);cancel.CancelAfter(30);bool canceled=false;var watch=Stopwatch.StartNew();
    try{model.Evaluate(choices,state,new Settings{Simulations=10000000},cancel.Token);}catch(OperationCanceledException){canceled=true;}
    check(canceled && watch.Elapsed.TotalSeconds<5,"高次数模拟支持运行中取消，不等待千万次算完");
   }
   // Status/Bridge readers receive independent trees, including the cached fallback.
   J.Write(Path.Combine(Paths.Bridge,"snapshot.json"),Fixture());var bridge=new Bridge();var first=bridge.Snapshot!;first["sima"]!["run"]!["nodo"]="changed";
   File.Delete(Path.Combine(Paths.Bridge,"snapshot.json"));var cached=bridge.Snapshot!;
   check(cached.At("sima","run","nodo").S()=="room" && !ReferenceEquals(first,cached),"Bridge缓存快照每次独立复制，不共享给状态页与模拟器");
   // Concurrent learning and evaluations may share the model, never its mutable lists.
   var learning=Task.Run(()=>{for(int i=0;i<150;i++)model.Learn(Battle(i),false);});
   var evaluations=Enumerable.Range(0,4).Select(_=>Task.Run(()=>{for(int i=0;i<4;i++){var state=Fixture();var options=TargetRules.Choices(state,state.At("sima","run")!,"room",true);model.Evaluate(options,state,new Settings{Simulations=2000},CancellationToken.None);if(options.Any(c=>c.Forecast?.Trials!=2000))throw new Exception("lost trials");}})).ToArray();
   await Task.WhenAll(evaluations.Append(learning));check(model.Battles==150,"同一模型并发学习150场与16轮2000次模拟无集合修改和空引用");
   // Production Step dispatches its chosen name->name mapping through the real Bridge.
   var state2=Fixture();state2["loggedIn"]=true;state2["time"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0;
   J.Write(Path.Combine(Paths.Bridge,"session.key"),"fixture");J.Write(Path.Combine(Paths.Bridge,"snapshot.json"),state2);
   pilot=new Pilot();pilot.Save(new Settings{Simulations=2000,AutoPactWithFreeze=false});
   using var stop=new CancellationTokenSource();JsonNode? dispatched=null;
   var service=Task.Run(async()=>{while(!stop.IsCancellationRequested){var cmd=J.Read(Path.Combine(Paths.Bridge,"command.json"));if(cmd!=null){dispatched=cmd;var receipt=new JsonObject{["id"]=cmd.At("id").S(),["ok"]=true,["confirmed"]=true};state2["lastCommand"]=receipt.DeepClone();state2["time"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000.0;state2["sima"]!["run"]!["hechos"]=new JsonArray("room");J.Write(Path.Combine(Paths.Bridge,"snapshot.json"),state2);J.Write(Path.Combine(Paths.Bridge,"response.json"),receipt);break;}await Task.Delay(10);}});
   try{await (Task)typeof(Pilot).GetMethod("Step",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pilot,[false,CancellationToken.None])!;}finally{stop.Cancel();await service;}
   check(dispatched.At("method").S()=="sima.fight" && dispatched.At("args","objetivos")!.AsObject().Count==4 && dispatched.At("args","objetivos")!.AsObject().Select(p=>p.Value.S()).Distinct().Count()==4,"生产Pilot.Step经真实Bridge派发四人不同目标，不只修改显示或模拟");
  }
  finally{Paths.Data=data;Paths.GameData=game;}
 }
 internal static JsonNode Battle(int id)
 {
  var s=Fixture();var events=Enumerable.Range(0,16).Select(i=>new{t=i*.2,side="ally",src="Hero"+(i%4),dst="Enemy"+(i%4),dmg=20,hpPct=80-i});
  s["lastBattle"]=JsonSerializer.SerializeToNode(new{time="learn"+id,before=s.At("sima","run"),vista=s.At("peeks","room"),result=new{events,durationSec=5}},J.Options);return s;
 }
 internal static JsonNode Fixture()
 {
  var team=Enumerable.Range(0,4).Select(i=>new{id="h"+i,nombre="Hero"+i,hp=1000,hpMax=1000,anima=100,afilado=2}).ToArray();
  return JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new{sima=new{run=new{nodo="room",profundidad=0,mapa=new{nodos=new{room=new{fila=1,tipo="combate"}}},heroes=team,reliquias=new[]{new{id="es_carambano",desc="Tus autoataques CONGELAN al objetivo"}},santuario=new{reposarPct=30}}},heroes=team.Select(h=>new{h.id,h.nombre,stats=new{hp=1000,atk=120,spd=2,crit=40,critdmg=200},skills=new[]{new{equipped=true,damages=true,desc="200% de dano",cooldown=4,element="physical",rule=new{cond="always"}}}}),peeks=new{room=new{enemigos=Enumerable.Range(0,4).Select(i=>new{@char="foe"+i,name="Enemy"+i,foco="front"})}}}))!;
 }
}
