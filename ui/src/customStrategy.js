import catalog from '../../shared/card-catalog.json' with {type:'json'};
import reference from '../../shared/strategy4-defaults.json' with {type:'json'};

export {catalog};
export const familyIds = catalog.families.map(x=>x.id);
export const categoryIds = catalog.categories.map(x=>x.id);
export const cardById = Object.fromEntries(catalog.cards.map(c=>[c.id,c]));
export const defaultPreference = {priority:999, targetCopies:3, mode:'normal'};
export const defaultReference = reference.reference;
export const categoryOrderFor=(value,family)=>value?.categoryOrder?.[family]||reference.categoryOrder[family]||categoryIds;
export function preferenceFor(value,id) {
  return {...defaultPreference,priority:reference.cardPriority[id]??999,mode:reference.cardMode[id]??'normal',...value?.cards?.[id]};
}
export const fixedOpening = '开局优先寒冰和普攻冰冻。继承有普攻冰冻就优先带走，没有则择优继承过渡遗物，下一层继续寻找；到达回执确认冰冻遗物已继承后，再按下方自定义选牌。临时徽章和本章新牌不算继承确认。';
export function defaults() {
  return {familyOrder:[...reference.familyOrder], categoryOrder:structuredClone(reference.categoryOrder), cards:{}, avoidOwnedEffects:true};
}
export function settings(value) {return {...defaults(),...value};}
export function move(items, index, delta) {
  return moveTo(items,index,index+delta);
}
export function moveTo(items,from,to) {
  const next=items.slice();
  if(from<0||to<0||from>=next.length||to>=next.length)return next;
  next.splice(to,0,next.splice(from,1)[0]);return next;
}
const record=v=>v!==null&&typeof v==='object'&&!Array.isArray(v);
const permutation=(list,ids)=>Array.isArray(list)&&list.length===ids.length&&new Set(list).size===ids.length&&list.every(x=>ids.includes(x));
export function customError(value) {
  if(value!==undefined&&!record(value))return '策略四：配置须为有效对象。';
  const v=settings(value);
  if(!permutation(v.familyOrder,familyIds))return '策略四：系列顺序须包含七个不同系列。';
  if(!record(v.categoryOrder) || Object.entries(v.categoryOrder).some(([id,list])=>!familyIds.includes(id)||!permutation(list,categoryIds)))return '策略四：细分类顺序无效。';
  if(!record(v.cards)||Object.keys(v.cards).length>2000 || Object.entries(v.cards).some(([id,p])=>!id.trim()||id.length>128||!record(p)||!Number.isInteger(p.priority)||p.priority<1||p.priority>999||!Number.isInteger(p.targetCopies)||p.targetCopies<0||p.targetCopies>99||!['normal','avoid','block'].includes(p.mode)))return '策略四：单卡优先级须为1～999，目标张数须为0～99。';
  if(typeof v.avoidOwnedEffects!=='boolean')return '策略四：重复效果避让须为开关值。';
  return '';
}
export function orderedCards(value, family) {
  const v=settings(value), order=categoryOrderFor(v,family);
  return catalog.cards.filter(c=>c.family===family).sort((a,b)=>order.indexOf(a.category)-order.indexOf(b.category)||
    preferenceFor(v,a.id).priority-preferenceFor(v,b.id).priority||a.id.localeCompare(b.id));
}
export function exportStrategy(value) {
  const v=settings(value),error=customError(v);if(error)throw new Error(error);
  // Export effective defaults as well, so a later catalog update cannot change
  // the meaning of an exported strategy. No general application settings enter.
  const cards=Object.fromEntries(catalog.cards.map(c=>[c.id,preferenceFor(v,c.id)]));
  Object.assign(cards,v.cards);
  return JSON.stringify({type:'IdleLegions.Strategy4',version:1,settings:{familyOrder:v.familyOrder,categoryOrder:v.categoryOrder,cards,avoidOwnedEffects:v.avoidOwnedEffects}},null,2);
}
export function importStrategy(text) {
  if(text.length>512*1024)throw new Error('策略四文件不能超过512KB。');
  let file;try{file=JSON.parse(text);}catch{throw new Error('文件不是有效的JSON，请选择策略四导出文件。');}
  if(!record(file)||file.type!=='IdleLegions.Strategy4'||file.version!==1||!record(file.settings))throw new Error('请选择策略四专用导出文件（版本1）。');
  const allowed=['familyOrder','categoryOrder','cards','avoidOwnedEffects'];
  if(Object.keys(file.settings).some(k=>!allowed.includes(k))||allowed.some(k=>!Object.hasOwn(file.settings,k)))throw new Error('文件含有策略四以外的参数，或缺少必需配置。');
  const error=customError(file.settings);if(error)throw new Error(error);
  const v=file.settings;
  // Whitelist every nested field; never apply a whole application config.
  return {familyOrder:[...v.familyOrder],categoryOrder:Object.fromEntries(Object.entries(v.categoryOrder).map(([k,list])=>[k,[...list]])),cards:Object.fromEntries(Object.entries(v.cards).map(([id,p])=>[id,{priority:p.priority,targetCopies:p.targetCopies,mode:p.mode}])),avoidOwnedEffects:v.avoidOwnedEffects};
}
const chinese = value => /[\u3400-\u9fff]/.test(value || '');
export function cardName(card) {
  if (cardById[card?.id]?.verified) return cardById[card.id].name;
  const candidate = card?.nameZh || card?.nombreZh || card?.name;
  return chinese(candidate) ? candidate : '新发现卡牌';
}
export function cardText(card) {
  if (cardById[card?.id]?.verified) return cardById[card.id].description;
  const candidate = card?.descZh || card?.description;
  return chinese(candidate) ? candidate : '效果资料待核实';
}
