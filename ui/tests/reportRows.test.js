import test from 'node:test';
import assert from 'node:assert/strict';
import {aggregateHeroes, roundResourceRows, equipmentRows, reportPage, REPORT_PAGE_SIZE} from '../src/reportRows.js';

test('display rows paginate as 10 / 10 / 8 without modifying source', () => {
  const rows = Array.from({length: 28}, (_, i) => i);
  assert.equal(REPORT_PAGE_SIZE, 10);
  assert.deepEqual([1,2,3].map(p => reportPage(rows, p).rows.length), [10,10,8]);
  assert.deepEqual([1,2,3].flatMap(p => reportPage(rows, p).rows), rows);
  assert.equal(rows.length, 28);
});
test('empty, partial and exact page boundaries', () => {
  assert.deepEqual(reportPage([], 4), {pageCount:1,currentPage:1,start:0,end:0,rows:[]});
  for (const count of [1,9,10,11,20,21]) {
    const rows = Array.from({length:count}, (_,i)=>i);
    assert.equal(reportPage(rows, 99).currentPage, Math.ceil(count/10));
    assert.equal(reportPage(rows, 99).end, count);
  }
});
test('shrinking data clamps the page, including the lower bound', () => {
  assert.equal(reportPage(Array(12), 3).currentPage, 2);
  assert.equal(reportPage(Array(3), 3).currentPage, 1);
  assert.equal(reportPage(Array(12), 0).currentPage, 1);
});
const drop = evidence => ({round:1,depth:4,method:'sima.trade',evidence});
test('no new or unknown equipment evidence produces no display rows', () => {
  assert.deepEqual(equipmentRows([{},drop({}),drop({source:'unavailable'}),drop({source:'inventory-delta',items:[]}),drop({items:[],mailedMythics:0,pendingMythics:0}),drop({mailedMythics:null,pendingMythics:0}),drop({mailedMythics:-1})]), []);
});
test('confirmed mythic equipment remains even without a name', () => {
  const records = [drop({source:'inventory-delta',items:[{id:'a',name:'新增神话装备',rarity:'mythic'},{id:'unnamed',rarity:'mythic'}]})];
  const before = JSON.stringify(records);
  assert.equal(equipmentRows(records).length, 2);
  assert.equal(equipmentRows(records)[1].item.id, 'unnamed');
  assert.equal(JSON.stringify(records), before);
});
test('only positive mailbox counts create an extra row', () => {
  const records = [drop({mailedMythics:2}),drop({pendingMythics:1}),drop({mailedMythics:0,pendingMythics:0}),drop({items:[{id:'a',rarity:'mythic'}],mailedMythics:1,pendingMythics:0})];
  const rows = equipmentRows(records);
  assert.equal(rows.length, 4);
  assert.equal(rows.filter(row=>row.pending).length, 3);
  assert.equal(new Set(rows.map(row=>row.key)).size, 4);
});
test('equipment pagination counts items plus positive mailbox summaries', () => {
  const records = [drop({items:Array.from({length:11},(_,i)=>({id:`item-${i}`,rarity:'mythic'})),pendingMythics:1})];
  const rows = equipmentRows(records);
  assert.equal(reportPage(rows, 1).rows.length, 10);
  assert.equal(reportPage(rows, 2).rows.length, 2);
  assert.equal(rows.length, 12);
});

test('mixed rarities show mythic only without rewriting raw items', () => {
 const items=['common','rare','epic','legendary','mythic','unknown','',null].map((rarity,i)=>({id:String(i),rarity}));
 items.push({id:'missing-rarity'});
 const records=[drop({source:'inventory-delta',items})],before=JSON.stringify(items);
 assert.deepEqual(equipmentRows(records).map(row=>row.item.id),['4']);
 assert.equal(JSON.stringify(items),before);
});
test('non-mythic rows stay hidden but preserve mythic mailbox counts', () => {
 const records=[drop({items:[{id:'rare',rarity:'rare'},{id:'unknown'}],pendingMythics:2})];
 assert.equal(equipmentRows(records).length,1);
 assert.equal(equipmentRows(records)[0].pending,true);
 assert.deepEqual(equipmentRows([drop({items:[{id:'epic',rarity:'epic'}],mailedMythics:0})]),[]);
});


test('resource totals show one completed-round row and filter only by round', () => {
 const rows=[{round:1,cores:5,shards:3},{round:3,cores:4,shards:15},{round:2,cores:7,shards:13}];
 const before=JSON.stringify(rows);
 assert.deepEqual(roundResourceRows(rows).map(x=>x.round),[3,2,1]);
 assert.deepEqual(roundResourceRows(rows,'2'),[{round:2,cores:7,shards:13}]);
 assert.equal(JSON.stringify(rows),before);
 assert.deepEqual(roundResourceRows([]),[]);
});
test('resource presentation retains unknown and confirmed zero totals without adding battle balances', () => {
 const rows=[{round:1,cores:0,shards:0,complete:true},{round:2,cores:null,shards:4,complete:false}];
 assert.deepEqual(roundResourceRows(rows),[rows[1],rows[0]]);
 assert.equal(roundResourceRows(rows,'2')[0].cores,null);
});

test('skill aggregation combines by kind and ID, retains names and conserves total damage', () => {
 const battles=[{durationSec:10,heroes:[{name:'弓箭手',damage:100,damageDetails:[{kind:'skill',id:'a',name:'连射',damage:70,events:2},{kind:'reaction',id:'a',name:'碎冰',damage:30,events:1}]}]},
 {durationSec:20,heroes:[{name:'弓箭手',damage:50,damageDetails:[{kind:'skill',id:'a',name:'a',damage:50,events:3}]}]}];
 const before=JSON.stringify(battles),hero=aggregateHeroes(battles)[0];
 assert.equal(hero.damage,150);assert.equal(hero.duration,30);
 assert.equal(hero.details.length,2);assert.equal(hero.details[0].name,'连射');assert.equal(hero.details[0].events,5);
 assert.equal(hero.details.reduce((sum,x)=>sum+x.damage,0),hero.damage);
 assert.equal(JSON.stringify(battles),before);
});
test('legacy summaries remain explicitly unsplit alongside new skill data', () => {
 const battles=[{heroes:[{name:'法师',damage:50}]}, {heroes:[{name:'法师',damage:30,damageDetails:[{kind:'skill',id:'ice',name:'冰球',damage:30,events:2}]}]}, {heroes:[{name:'法师',damage:20}]}];
 const hero=aggregateHeroes(battles)[0];
 assert.equal(hero.details[0].kind,'unrecorded');assert.equal(hero.details[0].damage,70);assert.equal(hero.details[0].events,null);
 assert.equal(hero.details.reduce((sum,x)=>sum+x.damage,0),100);
});


test('rich damage aggregation preserves totals, target splits and globally highest hits', () => {
 const make=(damage,crit,max,target)=>({name:'弓箭手',damage,detailVersion:2,damageDetails:[{kind:'skill',id:'marca_cazadora',name:'marca_cazadora',damage,events:2,criticalEvents:1,criticalDamage:crit,minHit:1,maxHit:max,firstHit:0,lastHit:2,periodicEvents:0,periodicDamage:0,kills:1,targets:[{id:target,name:target,damage,events:2,criticalEvents:1,kills:1}],elements:[{id:'ice',name:'冰',damage,events:2}],tags:[]}],skillActivity:[{id:'marca_cazadora',castMarkers:1,damage,events:2,healing:0,reactionDamage:0,relicDamage:0}],largestHits:[{damage:max,time:2,kind:'skill',id:'marca_cazadora',name:'marca_cazadora'}]});
 const battles=[{round:1,depth:7,durationSec:10,heroes:[make(100,60,70,'A')]},{round:1,depth:8,durationSec:20,heroes:[make(200,100,150,'B')]}];
 const before=JSON.stringify(battles),h=aggregateHeroes(battles)[0],d=h.details[0];
 assert.equal(d.name,'女猎手印记');assert.equal(d.criticalDamage,160);assert.equal(d.criticalEvents,2);assert.equal(d.maxHit,150);
 assert.equal(d.targets.reduce((n,t)=>n+t.damage,0),300);assert.equal(d.elements[0].damage,300);
 assert.equal(h.skillActivity[0].castMarkers,2);assert.equal(h.largestHits[0].depth,8);assert.equal(JSON.stringify(battles),before);
});
test('missing old detail metrics remain unknown when combined with new records', () => {
 const h=aggregateHeroes([{heroes:[{name:'法师',damage:10,damageDetails:[{kind:'skill',id:'a',damage:10,events:1}]}]},{heroes:[{name:'法师',damage:20,damageDetails:[{kind:'skill',id:'a',damage:20,events:1,criticalDamage:20,maxHit:20,targets:[{id:'a',damage:20}]}]}]}])[0];
 assert.equal(h.details[0].damage,30);assert.equal(h.details[0].criticalDamage,null);assert.equal(h.details[0].maxHit,null);assert.equal(h.details[0].targets,null);
});

test('different observed loadouts and descriptions survive aggregation without backfill', () => {
 const make=(text,atk)=>({name:'狂战士',defId:'berserker',damage:10,loadout:{stats:{atk},skills:[{id:'strike',description:text}]},damageDetails:[{kind:'skill',id:'strike',damage:10,events:1,description:text,descriptionBasis:'battle-context'}]});
 const battles=[{round:1,depth:1,heroes:[make('310%',100)]},{round:1,depth:2,heroes:[make('320%',110)]},{round:1,depth:3,heroes:[make('310%',100)]},{heroes:[{name:'狂战士',damage:5}]}];
 const before=JSON.stringify(battles),h=aggregateHeroes(battles)[0];
 assert.equal(h.loadouts.length,2);assert.equal(h.loadouts[0].battles.length,2);assert.equal(h.loadoutMissing,1);
 assert.deepEqual(h.details.find(x=>x.id==='strike').descriptions.map(x=>[x.text,x.count]),[['310%',2],['320%',1]]);
 assert.equal(JSON.stringify(battles),before);
});
