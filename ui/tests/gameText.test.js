import test from 'node:test';
import assert from 'node:assert/strict';
import {gameName, gameDescription, gameText, cardLabel, cardDescription} from '../src/gameText.js';

test('old ID-only skill/reaction records display official Chinese labels', () => {
  assert.equal(gameName('reaction','vaporizacion','vaporizacion'),'蒸发');
  assert.equal(gameName('reaction','eclipse','eclipse'),'月蚀');
  assert.equal(gameName('reaction','choque_termico'),'热冲击');
  assert.equal(gameName('skill','marca_cazadora'),'女猎手印记');
  assert.equal(gameName('skill','flecha_hielo'),'寒冰箭');
});
test('card names and actual effect amounts are translated without mutating receipts', () => {
  const card={id:'es_costra',nombre:'Costra de hielo',desc:'Tus golpes ralentizan un 2% durante 2 s, y cada golpe encajado te suma 1% de defensa, hasta +6%'};
  const before=JSON.stringify(card);
  assert.equal(cardLabel(card),'冰痂');
  assert.match(cardDescription(card),/2%/);assert.match(cardDescription(card),/6%/);assert.match(cardDescription(card),/防御/);
  assert.equal(JSON.stringify(card),before);
});
test('unmapped text/ID and an explicit Chinese label remain intact', () => {
  assert.equal(gameName('skill','unknown-next-update'),'unknown-next-update');
  assert.equal(gameText('new original text'),'new original text');
  assert.equal(gameName('skill','furia','玩家中文名称'),'玩家中文名称');
  assert.doesNotMatch(gameDescription('skill','tajo_ascendente'),/§/);
  assert.match(gameDescription('skill','tajo_ascendente'),/130%/);
});
