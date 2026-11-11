import test from 'node:test';
import assert from 'node:assert/strict';
import {seriesStatus} from '../src/seriesProgress.js';
const series = counts => counts.map((count,i) => ({name:['冰','火','电','毒','暗影','光耀','钢'][i], count}));

test('empty or incomplete collection state must never claim completion', () => {
  for (const value of [null, undefined, [], series([8]), series([8,8,8,8,8,8,NaN])])
    assert.match(seriesStatus(value), /等待完整/);
});
test('no active run uses a waiting state, not a historical completion claim', () => {
  assert.match(seriesStatus(series([8,8,8,8,8,8,8]), false), /尚未进入/);
});
test('each family has its own deficit and source data remains unchanged', () => {
  const data=series([5,2,4,3,7,1,0]);
  const before=JSON.stringify(data);
  assert.equal(seriesStatus(data), '本层还缺：火2张、毒1张、光耀3张、钢4张。');
  assert.equal(JSON.stringify(data),before);
});
test('only seven completed family counters declare current-floor completion', () => {
  assert.equal(seriesStatus(series([4,5,6,4,4,4,4])), '本层七系已齐，继续核对继承进度。');
});
