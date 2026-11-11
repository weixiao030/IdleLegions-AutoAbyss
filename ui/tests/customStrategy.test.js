import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,existsSync} from 'node:fs';
import {catalog,defaults,customError,move,moveTo,categoryIds,orderedCards,cardText,cardName,preferenceFor,exportStrategy,importStrategy} from '../src/customStrategy.js';
import {configError} from '../src/useAutoSave.js';

test('complete local inventory has authentic art and honest Chinese evidence coverage',()=>{
 assert.equal(catalog.cards.length,151);assert.equal(new Set(catalog.cards.map(c=>c.id)).size,151);
 assert.equal(catalog.cards.filter(c=>c.verified).length,151);assert.equal(catalog.coverage.pending,0);
 for(const card of catalog.cards){
  assert.match(card.name,/[\u3400-\u9fff]/);assert.match(card.description,/[\u3400-\u9fff]/);
  const image=new URL(`../public${card.image}`,import.meta.url);assert.ok(existsSync(image));
  const png=readFileSync(image);assert.equal(png.readUInt32BE(0),0x89504e47);assert.equal(png.readUInt32BE(16),96);assert.equal(png.readUInt32BE(20),144);
  if(!card.verified){assert.match(card.description,/待核实/);assert.equal(card.threshold,null);}
 }
});
test('reordering is immutable and validates complete unique permutations',()=>{
 const cfg=defaults(), before=structuredClone(cfg);const next=move(cfg.familyOrder,0,1);
 assert.deepEqual(cfg,before);assert.equal(next[1],cfg.familyOrder[0]);assert.equal(customError({...cfg,familyOrder:next}),'');
 assert.deepEqual(moveTo(cfg.familyOrder,0,6),[...cfg.familyOrder.slice(1),cfg.familyOrder[0]]);
 assert.deepEqual(moveTo(cfg.familyOrder,6,0),[cfg.familyOrder[6],...cfg.familyOrder.slice(0,6)]);
 assert.match(customError({...cfg,familyOrder:next.slice(1)}),/系列/);
 assert.match(customError({...cfg,categoryOrder:{fulgor:categoryIds.slice(1)}}),/细分类/);
 assert.equal(customError({...cfg,cards:{future_card:{priority:1,targetCopies:0,mode:'block'}}}),'');
 assert.match(customError({...cfg,cards:{future_card:{priority:1000,targetCopies:0,mode:'normal'}}}),/1～999/);
});
test('nested custom settings do not mutate another family or defaults',()=>{
 const cfg=defaults(),before=defaults();cfg.categoryOrder.fulgor=categoryIds.slice().reverse();cfg.cards.fu_punteria={priority:1,targetCopies:0,mode:'avoid'};
 assert.deepEqual(defaults(),before);assert.deepEqual(defaults().cards,{});
 assert.ok(orderedCards(cfg,'fulgor').every(c=>c.family==='fulgor'));
});
test('reference defaults rank every family from 1 without a generic 100 priority',()=>{
 const v=defaults();
 for(const f of v.familyOrder){const cards=orderedCards(v,f);assert.deepEqual(cards.map(c=>preferenceFor(v,c.id).priority),cards.map((c,i)=>i+1));}
 assert.equal(preferenceFor(v,'br_yesca').mode,'block');
 assert.equal(catalog.cards.find(c=>c.id==='au2_triaje').category,'survival');
 assert.equal(catalog.cards.find(c=>c.id==='um2_sello').category,'mark');
 assert.equal(catalog.cards.find(c=>c.id==='fu6_conducto').category,'bounce');
});
test('dedicated transfer roundtrips effective settings and cannot import other strategies',()=>{
 const v=defaults();v.familyOrder.reverse();v.cards.es_carambano={priority:4,targetCopies:1,mode:'avoid'};
 const text=exportStrategy(v),copy=importStrategy(text);assert.equal(customError(copy),'');
 assert.deepEqual(copy.familyOrder,v.familyOrder);assert.deepEqual(copy.cards.es_carambano,v.cards.es_carambano);
 for(const c of catalog.cards)assert.deepEqual(preferenceFor(copy,c.id),preferenceFor(v,c.id));
 const outer={strategyId:3,simulations:2000,customStrategy:v},before=structuredClone(outer);
 const next={...outer,customStrategy:copy};assert.equal(next.strategyId,3);assert.equal(next.simulations,2000);assert.deepEqual(outer,before);
 for(const bad of [null,[],{},outer,{type:'IdleLegions.Strategy4',version:2,settings:v},{type:'IdleLegions.Strategy4',version:1,settings:{...v,strategyId:4}},{type:'IdleLegions.Strategy4',version:1,settings:{...v,cards:{a:{priority:0,targetCopies:0,mode:'normal'}}}}])assert.throws(()=>importStrategy(JSON.stringify(bad)));
 assert.throws(()=>importStrategy('{broken'));assert.throws(()=>importStrategy('x'.repeat(512*1024+1)));
});
test('strategies 1–3 ignore arbitrary inactive custom configuration while 4 validates it',()=>{
 const base=JSON.parse(readFileSync(new URL('../../docs/默认参数.json',import.meta.url),'utf8'));
 for(const strategyId of [1,2,3])assert.equal(configError({...base,strategyId,customStrategy:{familyOrder:[],cards:null}}),'');
 assert.match(configError({...base,strategyId:4,customStrategy:{familyOrder:[],cards:null}}),/策略四/);
 assert.equal(configError({...base,strategyId:4,customStrategy:defaults()}),'');
});
test('known runtime relics display verified Chinese names and effect descriptions',()=>{
 const card={id:'es_carambano',nombre:'Carámbano',desc:'Tus autoataques CONGELAN al objetivo'};
 assert.match(cardName(card),/[\u3400-\u9fff]/);assert.match(cardText(card),/冻结/);
 assert.equal(cardName({id:'future',nombreZh:'新卡'}),'新卡');
 assert.equal(cardName({id:'future',nombre:'Future card'}),'新发现卡牌');
 assert.equal(cardText({id:'future',desc:'Future effect'}),'效果资料待核实');
});


test('light preset is opt-in and preserves all existing custom settings',async()=>{
 const {applyLightPreset,cardTitle}=await import('../src/customStrategy.js');
 const v=defaults();v.familyOrder.reverse();v.cards.au_fervor={priority:7,targetCopies:2,mode:'avoid'};
 const before=structuredClone(v),next=applyLightPreset(v);
 assert.equal(defaults().lightFervorOnly,false);assert.equal(next.lightFervorOnly,true);
 assert.deepEqual(v,before);assert.deepEqual(next.cards,v.cards);assert.deepEqual(next.familyOrder,v.familyOrder);
 assert.equal(importStrategy(exportStrategy(next)).lightFervorOnly,true);
 const old=JSON.parse(exportStrategy(v));delete old.settings.lightFervorOnly;
 assert.equal(importStrategy(JSON.stringify(old)).lightFervorOnly,false);
 assert.match(customError({...v,lightFervorOnly:'true'}),/光系/);
 assert.equal(cardTitle(catalog.cards.find(c=>c.id==='es_carambano')),'冰锥 · 遗物');
 assert.equal(cardTitle(catalog.cards.find(c=>c.id==='es2_carambano')),'冰锥 · 2张羁绊');
});
