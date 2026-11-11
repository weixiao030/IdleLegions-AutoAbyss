import test from 'node:test';
import assert from 'node:assert/strict';
import {searchDefaults,searchError} from '../src/searchSettings.js';
import {defaults,exportStrategy,importStrategy,customError} from '../src/customStrategy.js';

test('search budgets reject malformed values without silent rounding',()=>{
 assert.equal(searchError(undefined),'');assert.equal(searchError(searchDefaults),'');
 for(const bad of [null,[],3,{workers:0},{workers:1.5},{horizon:129},{iterations:Infinity},{iterations:''},{exploration:NaN},{exploration:0},{autoTrain:'yes'}])assert.notEqual(searchError(bad),'');
 for(const n of [1,64])assert.equal(searchError({...searchDefaults,workers:n}),'');
});
test('strategy four default compares value and preserves explicit family mode on transfer',()=>{
 assert.equal(defaults().selectionMode,'adaptive');
 for(const selectionMode of ['adaptive','family'])assert.equal(importStrategy(exportStrategy({...defaults(),selectionMode})).selectionMode,selectionMode);
 const legacy=JSON.parse(exportStrategy(defaults()));delete legacy.settings.selectionMode;
 assert.equal(importStrategy(JSON.stringify(legacy)).selectionMode,'family');
 assert.notEqual(customError({...defaults(),selectionMode:'unknown'}),'');
});

test('training device defaults to CPU, round trips CUDA and rejects unsupported backends',()=>{
 assert.equal(searchDefaults.trainingDevice,'cpu');
 assert.equal(searchError({trainingEpochs:12}),'');
 for(const trainingDevice of ['cpu','cuda']){
  const saved=JSON.parse(JSON.stringify({...searchDefaults,trainingDevice}));
  assert.equal(searchError(saved),'');assert.equal(saved.trainingDevice,trainingDevice);
 }
 for(const trainingDevice of ['CUDA','gpu','',null,0])assert.notEqual(searchError({trainingDevice}),'');
});

test('Auto is opt in, persists independently from the training device and rejects unknown modes',()=>{
 assert.equal(searchDefaults.mode,'manual');
 assert.equal(searchError({}),'');
 for(const mode of ['auto','manual'])for(const trainingDevice of ['cpu','cuda']){
  const saved=JSON.parse(JSON.stringify({...searchDefaults,mode,trainingDevice,autoTrain:false}));
  assert.equal(searchError(saved),'');assert.equal(saved.mode,mode);assert.equal(saved.trainingDevice,trainingDevice);assert.equal(saved.autoTrain,false);
 }
 for(const mode of ['AUTO','',null,0])assert.notEqual(searchError({mode}),'');
});
