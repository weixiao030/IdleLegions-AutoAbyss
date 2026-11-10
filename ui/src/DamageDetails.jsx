import React, {useState} from 'react';
import ReportTable from './ReportTable.jsx';
import BattleLoadouts from './BattleLoadouts.jsx';
import {ReportNumber, ReportShare} from './ReportNumbers.jsx';
import {gameName, gameDescription, gameText} from './gameText.js';
import {CaretDown} from '@phosphor-icons/react';

const fmt = n => n == null ? '未记录' : Math.round(n).toLocaleString('zh-CN');
const kindName = kind => ({skill:'技能',reaction:'反应',relic:'遗物',other:'无来源标记',unrecorded:'旧记录'}[kind] || '未标记');
const percent = (n,total) => n == null || !total ? '未记录' : `${(n / total * 100).toFixed(1)}%`;
const title = detail => gameName(detail.kind,detail.id,detail.name);

function SourceBreakdown({detail:d,hero}) {
 const descriptions=d.descriptions||[{basis:d.descriptionBasis||'catalog',text:d.description||'',count:1}];
 return <div className="damage-source-panel">
  {descriptions.map((desc,i)=>{
   const observed=desc.basis==='battle-context';
   const text=d.kind==='skill'&&(desc.basis==='not-recorded'||observed&&!desc.text)?'':gameDescription(d.kind,d.id,desc.text,hero.defId);
   return <p className="damage-effect" key={i}>{d.kind==='skill'&&<strong>{observed?'战前实际技能说明':desc.basis==='not-recorded'?'本场未采集技能参数':'基础技能说明（不代表本场等级）'} · {desc.count} 场： </strong>}{text||(d.kind==='skill'?'请在下方战前构筑中查看已采集场次；旧记录不补填当前技能。':'')}</p>;
  })}
  <dl className="damage-stat-list">
   <div><dt>命中事件</dt><dd>{fmt(d.events)}</dd></div>
   <div><dt>平均命中</dt><dd><ReportNumber value={d.events>0?d.damage/d.events:null}/></dd></div>
   <div><dt>最低 / 最高命中</dt><dd><ReportNumber value={d.minHit}/> / <ReportNumber value={d.maxHit}/></dd></div>
   <div><dt>暴击命中 / 命中占比</dt><dd>{fmt(d.criticalEvents)} / {percent(d.criticalEvents,d.events)}</dd></div>
   <div><dt>暴击伤害 / 该项占比</dt><dd><ReportNumber value={d.criticalDamage}/> / {percent(d.criticalDamage,d.damage)}</dd></div>
   <div><dt>非暴击伤害</dt><dd><ReportNumber value={d.criticalDamage==null?null:d.damage-d.criticalDamage}/></dd></div>
   <div><dt>周期标记伤害 / 事件（含引导）</dt><dd><ReportNumber value={d.periodicDamage}/> / {fmt(d.periodicEvents)}</dd></div>
   <div><dt>引导标记伤害 / 事件</dt><dd><ReportNumber value={d.channelDamage}/> / {fmt(d.channelEvents)}</dd></div>
   <div><dt>击杀标记</dt><dd>{fmt(d.kills)}</dd></div>
   <div><dt>每秒输出（参战时长）</dt><dd><ReportNumber value={hero.duration>0?d.damage/hero.duration:null} suffix="/秒"/></dd></div>
  </dl>
  {Array.isArray(d.targets)?<ReportTable label={hero.name+' · '+title(d)+'命中目标'} scopeKey={`${hero.name}:${d.kind}:${d.id}`} headings={['命中目标','伤害','占该项输出','命中 / 暴击 / 击杀']} rows={d.targets} renderRow={t=><tr key={t.id}><th scope="row">{gameText(t.name||t.id)}</th><td><ReportNumber value={t.damage}/></td><td><ReportShare value={t.damage} total={d.damage} label={'对'+gameText(t.name||t.id)+'的伤害占比'}/></td><td>{fmt(t.events)} / {fmt(t.criticalEvents)} / {fmt(t.kills)}</td></tr>}/>:<p className="report-note">这部分旧记录没有目标和详细命中统计。</p>}
  {Array.isArray(d.elements)&&<div className="damage-element-list"><strong>元素构成</strong>{d.elements.map(e=><span key={e.id}>{gameText(e.name)||'未标记元素'} <ReportNumber value={e.damage}/>（{percent(e.damage,d.damage)}）</span>)}</div>}
  {Array.isArray(d.timings)&&<div className="damage-element-list"><strong>命中类型（互斥）</strong>{d.timings.map(t=><span key={t.id}>{t.name} <ReportNumber value={t.damage}/> · {fmt(t.events)} 次</span>)}</div>}
  {!!d.states?.length&&<div className="damage-element-list"><strong>回执状态（可重叠）</strong>{d.states.map(t=><span key={t.id}>{t.name} <ReportNumber value={t.damage}/> · {fmt(t.events)} 次</span>)}<small>仅表示命中事件携带的状态；不等于该状态造成的净增伤。</small></div>}
  {!!d.tags?.length&&<details className="damage-related-tags"><summary>关联技能、反应与遗物标记</summary><ReportTable label={hero.name+'关联标记'} scopeKey={`${d.kind}:${d.id}`} headings={['标记类型','名称','带该标记的伤害','命中事件']} rows={d.tags} renderRow={t=><tr key={`${t.kind}:${t.id}`}><td>{kindName(t.kind)}</td><th scope="row">{gameName(t.kind,t.id,t.name)}</th><td><ReportNumber value={t.damage}/></td><td>{fmt(t.events)}</td></tr>}/><p className="report-note">同一事件可能同时带技能、反应与遗物标记。这张关联表可以重叠，不与主伤害构成相加。</p></details>}
 </div>;
}

export default function DamageDetails({hero:h}) {
 const [expanded,setExpanded]=useState(null);
 return <>
  <ReportTable label={h.name+'技能明细'} scopeKey={h.name} headings={['技能 / 效果','输出','占角色输出','命中事件']} rows={h.details} renderRow={d=>{
   const key=`${d.kind}:${d.id}`,open=expanded===key;
   return <React.Fragment key={key}><tr>
    <th scope="row"><button className="damage-source-toggle" aria-expanded={open} onClick={()=>setExpanded(open?null:key)}><CaretDown size={13}/>{title(d)}</button><small className="report-code">{kindName(d.kind)}</small></th>
    <td><ReportNumber value={d.damage}/></td><td><ReportShare value={d.damage} total={h.damage} label={h.name+' · '+title(d)+'占角色输出'}/></td><td>{fmt(d.events)}</td>
   </tr>{open&&<tr className="damage-breakdown-row"><td colSpan={4}><SourceBreakdown detail={d} hero={h}/></td></tr>}</React.Fragment>;
  }}/>
  <p className="report-note">点击技能或效果查看暴击、平均 / 最高命中、目标和元素构成。主表互斥归类：反应 → 遗物 → 技能 → 无来源标记；无来源标记的命中按引导、周期及其余分别列项；周期标记不等于燃烧等持续伤害。命中数不代表施放次数。</p>
  <BattleLoadouts hero={h}/>
  <details className="damage-extra-disclosure"><summary>技能施法与参与伤害</summary>{h.skillActivity.length?<ReportTable label={h.name+'技能参与统计'} scopeKey={h.name} headings={['技能','施法标记','伤害命中','参与伤害','其中反应 / 遗物','治疗记录']} rows={h.skillActivity} renderRow={s=><tr key={s.id}><th scope="row">{gameName('skill',s.id,s.name)}</th><td>{fmt(s.castMarkers)}</td><td>{fmt(s.events)}</td><td><ReportNumber value={s.damage}/></td><td><ReportNumber value={s.reactionDamage}/> / <ReportNumber value={s.relicDamage}/></td><td><ReportNumber value={s.healing}/></td></tr>}/>:<p className="report-note">旧记录未采集施法标记；不能从命中数反推施放次数。</p>}<p className="report-note">技能参与伤害包含带该技能标记的反应和遗物伤害，已包含在主表总输出中。施法标记仅统计游戏明确发送的纯施法事件；0 不代表没有施放。增益技能没有伤害时仍列出。</p></details>
  <details className="damage-extra-disclosure"><summary>最高单次命中 · 前 20 条</summary>{h.largestHits.length?<ReportTable label={h.name+'最高命中'} scopeKey={h.name} headings={['轮次 / 深度 / 时间','技能或效果','目标','伤害','元素 / 标记']} rows={h.largestHits} renderRow={(hit,i)=><tr key={i}><td>第 {hit.round} 轮 · P{hit.depth}<small className="report-code">战斗内 {hit.time.toFixed(2)} 秒</small></td><th scope="row">{title(hit)}</th><td>{gameText(hit.target)}</td><td><ReportNumber value={hit.damage}/></td><td>{gameText(hit.element)}{hit.critical?' · 暴击':''}{hit.channel?' · 引导':hit.periodic?' · 周期':''}{hit.kill?' · 击杀':''}</td></tr>}/>:<p className="report-note">旧记录未采集单次命中明细。</p>}<p className="report-note">时间是各场战斗内时间；这里只展示最高 20 条，完整事件保留在本地原始回执中。</p></details>
 </>;
}
