import test from 'node:test';
import assert from 'node:assert/strict';
import {equipmentRows, reportPage, REPORT_PAGE_SIZE} from '../src/reportRows.js';

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
