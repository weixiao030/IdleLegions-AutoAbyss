import test from 'node:test';
import assert from 'node:assert/strict';
import {formatReportNumber, exactReportNumber, reportShare} from '../src/reportNumbers.js';

test('Chinese large numbers follow the screenshot without changing the input', () => {
  for (const [value, expected] of [[708000000,'7.08亿'],[647000000,'6.47亿'],[11941000,'1194.1万'],[152000,'15.2万'],[10000000,'1000万'],[10000,'1万'],[9999,'9,999'],[0,'0'],[-152000,'-15.2万']]) {
    assert.equal(formatReportNumber(value), expected);
  }
});
test('rounding near unit boundaries promotes to the next unit', () => {
  assert.equal(formatReportNumber(99999999), '1亿');
  assert.equal(formatReportNumber(9999.6), '1万');
  assert.equal(formatReportNumber(-99999999), '-1亿');
  assert.equal(formatReportNumber(123456789), '1.23亿');
});
test('unknown, non-finite and zero remain distinct', () => {
  for (const value of [null,undefined,NaN,Infinity,'100000000']) {
    assert.equal(formatReportNumber(value), '未确认');
    assert.equal(exactReportNumber(value), '未确认');
  }
  assert.equal(exactReportNumber(708000000), '708,000,000');
  assert.equal(exactReportNumber(11941000.25), '11,941,000.25');
});
test('share bars use full totals, preserve zero and reject unknown denominators', () => {
  assert.equal(reportShare(708,2236), 100*708/2236);
  assert.equal(reportShare(0,10), 0);
  for (const total of [0,-1,null,NaN]) assert.equal(reportShare(1,total), null);
  assert.equal(reportShare(null,10), null);
  assert.equal(reportShare(-1,10), null);
  assert.equal(reportShare(20,10), 100);
});
