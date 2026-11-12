import React, {useEffect, useRef, useState} from 'react';
import {Button, Switch, TextField, Badge} from '@radix-ui/themes';
import {ArrowUp, ArrowDown, MagnifyingGlass, ArrowCounterClockwise, Snowflake, LockKey, DownloadSimple, UploadSimple} from '@phosphor-icons/react';
import {catalog, defaults, settings, move, orderedCards, preferenceFor, categoryOrderFor, fixedOpening, defaultReference, exportStrategy, importStrategy, applyLightPreset, cardTitle} from './customStrategy.js';
import StrategyOrderList from './StrategyOrderList.jsx';
import './customStrategy.css';

const familyName=id=>catalog.families.find(f=>f.id===id)?.name;
const categoryName=id=>catalog.categories.find(c=>c.id===id)?.name;
export function CardArt({id,name,className=''}) {
 const [failed,setFailed]=useState(false);
 useEffect(()=>setFailed(false),[id]);
 return failed?<span className={'card-art-unavailable '+className}>暂无卡图</span>:<img className={'card-art '+className} src={`/cards/${encodeURIComponent(id)}.png`} alt={`${name}卡图`} width="192" height="288" loading="lazy" onError={()=>setFailed(true)}/>;
}
function CardNumber({label,value,min,max,onChange}) {
 const [text,setText]=useState(String(value));
 useEffect(()=>setText(String(value)),[value]);
 const valid=text!==''&&Number.isInteger(Number(text))&&Number(text)>=min&&Number(text)<=max;
 return <label className="custom-number"><span>{label}</span><input type="number" min={min} max={max} value={text} aria-label={label} aria-invalid={!valid} onChange={e=>{const s=e.target.value;setText(s);const n=Number(s);if(s!==''&&Number.isInteger(n)&&n>=min&&n<=max)onChange(n);}}/>{!valid&&<small>请输入 {min}～{max}</small>}</label>;
}
function OrderButtons({label,index,total,onMove}) {
 return <span className="custom-order-buttons"><button type="button" aria-label={`${label}上移`} title="上移" disabled={index===0} onClick={()=>onMove(-1)}><ArrowUp size={16}/></button><button type="button" aria-label={`${label}下移`} title="下移" disabled={index===total-1} onClick={()=>onMove(1)}><ArrowDown size={16}/></button></span>;
}
export default function CustomStrategyEditor({value,onChange,disabled,strategyId,openingComplete,saveState,onActivate}) {
 const v=settings(value), [family,setFamily]=useState('fulgor'), [query,setQuery]=useState(''), [filter,setFilter]=useState('all');
 const categoryOrder=categoryOrderFor(v,family),fileInput=useRef(null),locked=useRef(disabled),[transfer,setTransfer]=useState(null);
 locked.current=disabled;
 const patch=changes=>onChange({...v,...changes});
 const preference=(id,changes)=>patch({cards:{...v.cards,[id]:{...preferenceFor(v,id),...changes}}});
 const reorderCategories=order=>patch({categoryOrder:{...v.categoryOrder,[family]:order}});
 function download(){try{const blob=new Blob([exportStrategy(v)],{type:'application/json;charset=utf-8'}),url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download='策略四自定义选牌.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);setTransfer({text:'策略四配置已导出。',error:false});}catch(e){setTransfer({text:e.message,error:true});}}
 async function upload(e){const file=e.target.files?.[0];e.target.value='';if(!file||locked.current)return;try{if(file.size>512*1024)throw new Error('策略四文件不能超过512KB。');const next=importStrategy(await file.text());if(locked.current)throw new Error('正在下潜，请暂停后重新导入。');onChange(next);setTransfer({text:'已导入策略四选牌配置，将自动保存。当前使用的策略保持不变。',error:false});}catch(err){setTransfer({text:err.message,error:true});}}
 const cards=orderedCards(v,family).filter(c=>(filter==='all'||filter===c.kind||filter==='edited'&&v.cards[c.id]||filter==='pending'&&!c.verified)&&(!query||`${c.name} ${c.description} ${categoryName(c.category)}`.includes(query.trim())));
 const edits=Object.values(v.cards||{}), count=mode=>edits.filter(p=>p.mode===mode).length;
 return <div className="custom-strategy">
  <div className="page-title"><div><h1>策略四 · 自定义选牌</h1><p>先补冰系与七系4张羁绊，全部达标后按单卡收益比较。</p></div>{saveState}</div>
  <div className="custom-activation" role="status"><Badge color={strategyId===4?'cyan':'gray'}>{strategyId===4?'策略四已选择':'当前未启用'}</Badge><span>{strategyId===4?(openingComplete?'冰川或冰系4卡已继承，继续补七系4张':'优先冰川，没有就凑冰4；继承后启用'):`当前使用策略${['一','二','三'][strategyId-1]||'其他'}，此页配置仅供策略四使用。`}</span>{strategyId!==4&&<Button size="1" variant="outline" disabled={disabled} onClick={onActivate}>启用策略四</Button>}</div>
  <section className="custom-opening"><Snowflake size={23}/><div><h2>固定前置 · 自动过渡</h2><p>{fixedOpening}</p><small>冰系尚未达标、达到目标张数或强制菜单全被拉黑时逐级兜底，自动继续；商店没有合适卡牌时直接继续路线。</small></div></section>
  <section className="learning-panel" aria-label="光系特殊继承配置"><div className="section-bar"><h2>光系只继承普攻狂热</h2><Badge color={v.lightFervorOnly?'cyan':'gray'}>{v.lightFervorOnly?'已启用':'默认关闭'}</Badge></div><p>优先继承“狂热”“连祷”各一份，再补其他系列。其他光牌可在本层凑羁绊，章末优先不带走；菜单没有其他合法选项时正常过渡。仅策略四生效，不改动现有单卡排序。</p><Button disabled={disabled} variant={v.lightFervorOnly?'outline':'solid'} onClick={()=>v.lightFervorOnly?patch({lightFervorOnly:false}):onChange(applyLightPreset(v))}>{v.lightFervorOnly?'关闭此配置':'使用此配置'}</Button></section>
  <section className="learning-panel"><h2>补系与单卡选牌方式</h2><fieldset disabled={disabled}><label className="custom-mode"><span>选牌模式</span><select aria-label="策略四选牌模式" value={v.selectionMode} onChange={e=>patch({selectionMode:e.target.value})}><option value="adaptive">收益比较（默认）</option><option value="family">手动系列、分类、单卡排序</option></select></label></fieldset><p>{v.selectionMode==='adaptive'?'比较新增效果与战斗推演，冰系不会整体置顶；下方系列顺序暂不参与排序。软避让、拉黑和目标张数仍生效。':'按下方系列、细分类、单卡顺序选择。已满4张的系列仍会让位给未满系列；七系达标后自动改为收益比较。'}</p></section>
  <div className="custom-catalog-note"><strong>本地卡牌 {catalog.coverage.localCards} 张 · 中文与卡图已全部核实</strong><span>名称和描述来自游戏中文资源，徽章效果已通过真实菜单回执逐项核对。当前本地版本七系列清单完整；服务端后续新增卡以实际回执为准。</span><span>默认参考：{defaultReference} 单卡从1开始编号，1最高；仅在同系列、同细分类内比较。</span></div>
  {disabled&&<p className="custom-lock" role="status"><LockKey size={16}/>正在下潜，请暂停并等待当前回执后修改。仍可浏览卡牌。</p>}
  <div className="custom-toolbar"><span>已自定义 {edits.length} 张 · 自定义拉黑 {count('block')} 张 · 软避让 {count('avoid')} 张</span><div className="custom-transfer"><Button size="1" variant="outline" onClick={download}><DownloadSimple/>导出选牌</Button><Button size="1" variant="outline" disabled={disabled} onClick={()=>fileInput.current.click()}><UploadSimple/>导入选牌</Button><input ref={fileInput} type="file" accept=".json,application/json" hidden onChange={upload}/><Button size="1" variant="ghost" disabled={disabled} onClick={()=>{onChange(defaults());setTransfer(null);}}><ArrowCounterClockwise/>恢复策略四默认</Button></div></div>
  {transfer&&<p className={'custom-transfer-message '+(transfer.error?'error':'')} role={transfer.error?'alert':'status'}>{transfer.text}</p>}
  <div className="custom-layout">
   <section className="custom-families" aria-label="系列优先级"><h2>系列顺序</h2><p>长按左侧手柄拖动；1最高。先系列，后效果与单卡。</p><StrategyOrderList items={v.familyOrder} labelFor={familyName} label="系列排序列表" classFor={id=>id===family?'selected':''} disabled={disabled} onReorder={order=>patch({familyOrder:order})}>{(id,index)=><><button className="custom-family-select" type="button" aria-pressed={id===family} onClick={()=>{setFamily(id);setQuery('');}}><span>{index+1}</span><strong>{familyName(id)}</strong><small>{catalog.cards.filter(c=>c.family===id).length} 张</small></button><fieldset disabled={disabled}><OrderButtons label={familyName(id)} index={index} total={v.familyOrder.length} onMove={delta=>patch({familyOrder:move(v.familyOrder,index,delta)})}/></fieldset></>}</StrategyOrderList>
    <fieldset disabled={disabled}><label className="custom-repeat"><Switch size="1" checked={v.avoidOwnedEffects} onCheckedChange={b=>patch({avoidOwnedEffects:b})}/><span>软避让已有独立效果<small>已有更快弹跳、必暴或无增益的重复普攻效果时，让位给新效果。</small></span></label></fieldset>
   </section>
   <section className="custom-detail" aria-label={`${familyName(family)}卡牌配置`}>
    <div className="custom-detail-heading"><h2>{familyName(family)} · 效果与卡牌</h2><span>{cards.length} 张</span></div>
    <details className="custom-categories" open><summary>调整本系细分类顺序<span>当前优先：{categoryName(categoryOrder[0])}</span></summary><p>长按手柄拖动，或使用上下移按钮；1最高。只对本系生效；单卡优先级只在同系列、同细分类内比较。每张卡按主要效果归类，完整效果见卡牌说明。</p><fieldset disabled={disabled}><StrategyOrderList key={family} items={categoryOrder} labelFor={categoryName} label="细分类排序列表" disabled={disabled} onReorder={reorderCategories}>{(id,index)=><><span className="custom-order-number">{index+1}</span><strong>{categoryName(id)}</strong><OrderButtons label={categoryName(id)} index={index} total={categoryOrder.length} onMove={delta=>reorderCategories(move(categoryOrder,index,delta))}/></>}</StrategyOrderList></fieldset></details>
    <div className="custom-search"><TextField.Root aria-label="搜索本系卡牌" placeholder="搜索名称或中文效果" value={query} onChange={e=>setQuery(e.target.value)}><TextField.Slot><MagnifyingGlass/></TextField.Slot></TextField.Root><select aria-label="卡牌筛选" value={filter} onChange={e=>setFilter(e.target.value)}><option value="all">全部卡牌</option><option value="relic">可继承遗物</option><option value="badge">本章徽章</option><option value="edited">已配置</option></select></div>
    <p className="custom-help">选择顺序：正常候选 → 软避让 → 拉黑末位兜底。拉黑平时优先排除，强制菜单全被拉黑时择优继续；商店不为兜底消费。目标张数：达到后软避让，0 表示不限；遗物与临时徽章独立计算。</p>
    <div className="custom-card-list">{cards.map(c=>{const p=preferenceFor(v,c.id);return <article className={'custom-card mode-'+p.mode} key={c.id} data-card-id={c.id}>
     <CardArt id={c.id} name={c.name}/><div className="custom-card-body"><div className="custom-card-title"><h3>{cardTitle(c)}</h3>{c.core&&<Badge size="1" color="cyan">冰冻核心</Badge>}</div><div className="custom-card-meta"><span>{c.kind==='badge'?'本章徽章':'可继承遗物'}</span><span>{c.threshold?`${c.threshold}张羁绊解锁`:c.rarity}</span><span>{categoryName(c.category)}</span></div><p className="custom-card-description">{c.description}</p>{!c.verified&&<details className="custom-source"><summary>区分此卡</summary><code>{c.id}</code></details>}<fieldset className="custom-card-controls" disabled={disabled}><CardNumber label={`${cardTitle(c)}优先级`} value={p.priority} min={1} max={999} onChange={n=>preference(c.id,{priority:n})}/><CardNumber label={`${cardTitle(c)}目标张数`} value={p.targetCopies} min={0} max={99} onChange={n=>preference(c.id,{targetCopies:n})}/><label className="custom-mode"><span>选择方式</span><select aria-label={`${cardTitle(c)}选择方式`} value={p.mode} onChange={e=>preference(c.id,{mode:e.target.value})}><option value="normal">正常选择</option><option value="avoid">软避让</option><option value="block">拉黑（最后兜底）</option></select></label></fieldset></div>
    </article>})}</div>{!cards.length&&<div className="empty"><MagnifyingGlass size={24}/><p>没有符合条件的卡牌，请更换搜索或筛选。</p></div>}
   </section>
  </div>
 </div>;
}
