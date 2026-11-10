import React, {useEffect} from 'react';
import {Badge, Button, Select} from '@radix-ui/themes';
import {ChartLineUp, DownloadSimple} from '@phosphor-icons/react';

const fmt=n=>n==null?'未确认':Math.round(n).toLocaleString('zh-CN');
const percent=(n,total)=>total>0?`${(100*n/total).toFixed(1)}%`:'未确认';
const sourceName=s=>s==='settlement-receipt'?'结算收据':s==='inventory-delta'?'背包差分':'未确认';
const rarityName=s=>({mythic:'神话',legendary:'传说',epic:'史诗',rare:'稀有',common:'普通'}[s]||s||'未提供');

function aggregate(battles){
 const rows=new Map();
 for(const b of battles)for(const h of b.heroes||[]){
  const r=rows.get(h.name)||{name:h.name,damage:0,taken:0,crit:0,skills:0,reactions:0,hp:0,hpKnown:0,count:0,duration:0,sources:{reaction:0,relic:0,skill:0,other:0},sourcesKnown:0};
  r.damage+=h.damage||0;r.taken+=h.damageTaken||0;r.crit+=h.criticalEvents||0;r.skills+=h.skillEvents||0;r.reactions+=h.reactionEvents||0;r.duration+=b.durationSec||0;r.count++;
  if(h.hpLoss!=null){r.hp+=h.hpLoss;r.hpKnown++}
  if(h.damageSources){for(const k of Object.keys(r.sources))r.sources[k]+=h.damageSources[k]||0;r.sourcesKnown++}
  rows.set(h.name,r);
 }
 return [...rows.values()].sort((a,b)=>b.damage-a.damage);
}

function HeroTable({battles}){
 const heroes=aggregate(battles),total=heroes.reduce((n,h)=>n+h.damage,0);
 return <div className="table-scroll"><table><thead><tr><th>角色</th><th>输出</th><th>输出占比</th><th>DPS</th><th>承受事件伤害</th><th>结算生命损失</th><th>暴击 / 技能 / 反应命中</th></tr></thead><tbody>{heroes.map(h=><tr key={h.name}><th scope="row">{h.name}</th><td>{fmt(h.damage)}</td><td>{percent(h.damage,total)}</td><td>{h.duration>0?fmt(h.damage/h.duration):'未确认'}</td><td>{fmt(h.taken)}</td><td>{h.hpKnown?fmt(h.hp):'未确认'}<small className="report-code">{h.hpKnown}/{h.count} 场有结算值</small></td><td>{fmt(h.crit)} / {fmt(h.skills)} / {fmt(h.reactions)}</td></tr>)}</tbody></table></div>;
}

export default function BattleReport({state,onDownload,reportRound,setReportRound,reportDepth,setReportDepth}){
 const battles=state.battles||[],equipment=state.equipment||[],acquisitions=state.acquisitions||[];
 const all=[...battles,...equipment,...acquisitions];
 const rounds=[...new Set(all.map(x=>x.round))].sort((a,b)=>a-b);
 const allDepths=[...new Set(all.filter(x=>reportRound==='all'||String(x.round)===reportRound).map(x=>x.depth))].sort((a,b)=>a-b);
 useEffect(()=>{if(reportDepth!=='all'&&!allDepths.includes(Number(reportDepth)))setReportDepth('all')},[reportDepth,reportRound,allDepths.join(','),setReportDepth]);
 const within=x=>(reportRound==='all'||String(x.round)===reportRound)&&(reportDepth==='all'||String(x.depth)===reportDepth);
 const filtered=battles.filter(within),acquired=acquisitions.filter(within),drops=equipment.filter(within);
 const heroes=aggregate(filtered),totalDamage=heroes.reduce((n,h)=>n+h.damage,0);
 const cards=new Map();for(const b of filtered)for(const c of b.cardProcs||[]){const r=cards.get(c.id)||{...c,damage:0,events:0};r.damage+=c.damage||0;r.events+=c.events||0;cards.set(c.id,r)}
 const cardDamage=[...cards.values()].reduce((n,c)=>n+c.damage,0),truncated=filtered.filter(x=>x.truncated).length;
 const vulnerable=aggregate(filtered.filter(x=>!x.won)).filter(x=>x.hpKnown&&x.hp>0).sort((a,b)=>b.hp-a.hp)[0];
 const scope=reportRound==='all'?'全段':`第 ${reportRound} 轮`;
 return <>
  <div className="page-title"><div><h1>逐层战斗报告</h1><p>从轮次、深度和角色比较输出、生存与取得物品。</p></div><div className="actions">{state.reportPath&&<Button variant="outline" onClick={onDownload}><DownloadSimple size={17}/>下载完整报告</Button>}<Badge color={battles.length?'cyan':'gray'}>{battles.length} 场已记录</Badge></div></div>
  <div className="report-toolbar"><label>轮次<Select.Root value={reportRound} onValueChange={setReportRound}><Select.Trigger aria-label="筛选轮次"/><Select.Content><Select.Item value="all">全部轮次</Select.Item>{rounds.map(n=><Select.Item key={n} value={String(n)}>第 {n} 轮</Select.Item>)}</Select.Content></Select.Root></label><label>深度<Select.Root value={reportDepth} onValueChange={setReportDepth}><Select.Trigger aria-label="筛选深度"/><Select.Content><Select.Item value="all">全部深度</Select.Item>{allDepths.map(n=><Select.Item key={n} value={String(n)}>P{n}</Select.Item>)}</Select.Content></Select.Root></label><span className="muted">筛选同时作用于战斗、卡牌与结算记录</span></div>
  {!filtered.length?<section className="report-empty"><ChartLineUp size={36}/><h2>所选范围没有战斗记录</h2><p>新战斗收到回执后会自动记录；卡牌和结算可在下方查看。</p></section>:<>
   <section className="report-section"><div className="section-bar"><h2>{scope}角色汇总{reportDepth!=='all'?` · P${reportDepth}`:''}</h2><span className="muted">{filtered.length} 场 · {truncated} 场日志截断</span></div><HeroTable battles={filtered}/><p className="report-note">DPS按角色参战时长计算。结算生命损失与敌方事件伤害分别展示；旧记录缺字段时保持未确认。</p></section>
   {reportRound==='all'&&<section className="report-section"><div className="section-bar"><h2>单轮角色汇总</h2><span className="muted">展开比较各轮表现</span></div>{rounds.filter(n=>filtered.some(b=>b.round===n)).map(n=><details className="report-round" key={n}><summary>第 {n} 轮 · {filtered.filter(b=>b.round===n).length} 场</summary><HeroTable battles={filtered.filter(b=>b.round===n)}/></details>)}</section>}
   <section className="report-section"><div className="section-bar"><h2>逐层逐角色</h2><span className="muted">每场分别记录，保留同层多次战斗</span></div><div className="table-scroll"><table><thead><tr><th>轮次 / 深度</th><th>结果 / 时长</th><th>角色</th><th>输出</th><th>承受事件伤害</th><th>结算生命损失</th></tr></thead><tbody>{filtered.flatMap((b,i)=>(b.heroes||[]).map(h=><tr key={`${i}-${h.id||h.name}`}><td>第 {b.round} 轮 · P{b.depth}</td><td><Badge color={b.won?'cyan':'gray'}>{b.won?'胜利':'战败'}</Badge><small className="report-code">{b.durationSec>0?`${b.durationSec.toFixed(1)} 秒`:'时长未确认'}</small>{b.truncated&&<small className="report-warning">事件明细截断</small>}</td><th scope="row">{h.name}</th><td>{fmt(h.damage)}</td><td>{fmt(h.damageTaken)}</td><td>{fmt(h.hpLoss)}</td></tr>))}</tbody></table></div></section>
   <section className="report-section"><div className="section-bar"><h2>输出来源</h2><span className="muted">互斥归类，避免重复相加</span></div><div className="table-scroll"><table><thead><tr><th>角色</th><th>反应伤害</th><th>遗物伤害</th><th>技能伤害</th><th>普通 / 未标记伤害</th></tr></thead><tbody>{heroes.map(h=><tr key={h.name}><th scope="row">{h.name}</th>{Object.entries(h.sources).map(([k,v])=><td key={k}>{h.sourcesKnown?fmt(v):'未记录'}</td>)}</tr>)}</tbody></table></div><p className="report-note">同一事件依次按反应、遗物、技能、其他归入一项。来源未标记不代表没有卡牌增益。旧记录不反推来源。</p></section>
   <section className="report-section"><div className="section-bar"><h2>卡牌触发伤害</h2><span className="muted">只统计带 rel 标记的事件</span></div>{cards.size?<div className="table-scroll"><table><thead><tr><th>遗物 / 效果</th><th>触发伤害</th><th>占已标记触发</th><th>占全队输出</th><th>触发次数</th></tr></thead><tbody>{[...cards.values()].sort((a,b)=>b.damage-a.damage).map(c=><tr key={c.id}><th scope="row">{c.name}<small className="report-code">{c.description||c.id}</small></th><td>{fmt(c.damage)}</td><td>{percent(c.damage,cardDamage)}</td><td>{percent(c.damage,totalDamage)}</td><td>{fmt(c.events)}</td></tr>)}</tbody></table></div>:<p className="report-note">当前回执没有带 rel 标记的触发事件。</p>}<p className="report-note">含零伤害效果触发。占比只反映已标记直接伤害，常驻攻击、攻速、减速和异常增伤无法从总伤害中精确拆分。</p></section>
   <section className="report-section"><div className="section-bar"><h2>分析提示</h2></div>{truncated>0&&<p className="report-note">有 {truncated} 场事件截断，输出与来源占比不完整；建议先比较未截断的同层战斗。</p>}{vulnerable&&<p className="report-note">战败场中{vulnerable.name}结算生命损失合计 {fmt(vulnerable.hp)}。优先核对该角色的站位、生存装备及护盾覆盖，再用同层记录比较调整前后。</p>}<p className="report-note">比较同层、同阵容和相近时长的战斗更有参考价值。现有记录可定位表现变化，不能单独证明某张卡或某件装备的净提升。</p></section>
   <section className="report-section"><div className="section-bar"><h2>确认资源变化</h2></div><div className="table-scroll"><table><thead><tr><th>轮次</th><th>深度</th><th>核心增量</th><th>碎屑增量</th><th>奥波币增量</th></tr></thead><tbody>{filtered.map((b,i)=><tr key={i}><td>{b.round}</td><td>P{b.depth}</td><td>{fmt(b.resourceDelta?.cores)}</td><td>{fmt(b.resourceDelta?.shards)}</td><td>{fmt(b.resourceDelta?.obols)}</td></tr>)}</tbody></table></div><p className="report-note">操作前后暂存战利品快照差分，负数表示消耗。跨章或缺少快照时为未确认，不等同结算入账。</p></section>
   <p className="report-note">输出与承受伤害按事件相加；护盾、免疫和过量伤害可能使事件伤害高于实际扣血。暴击、技能及反应统计命中事件数。</p>
  </>}
  <section className="report-section"><div className="section-bar"><h2>装备与结算记录</h2><Badge color="gray">{drops.length} 次操作</Badge></div>{drops.length?<div className="table-scroll"><table><thead><tr><th>轮次 / 深度</th><th>装备名称</th><th>稀有度</th><th>证据来源</th></tr></thead><tbody>{drops.flatMap((d,i)=>{const e=d.evidence||{},items=e.items||[];return [...(items.length?items:[{name:e.source==='unavailable'?'未确认':'未列出新增装备'}]).map((it,j)=><tr key={`${i}-${j}`}><td>第 {d.round} 轮 · P{d.depth}<small className="report-code">{d.method}</small></td><th scope="row">{it.name}<small className="report-code">{it.id}</small></th><td>{rarityName(it.rarity)}</td><td>{sourceName(e.source)}</td></tr>),...(e.mailedMythics!=null||e.pendingMythics!=null?[<tr key={`${i}-pending`}><td>第 {d.round} 轮 · P{d.depth}</td><td colSpan={3}>寄往邮箱神话 {fmt(e.mailedMythics)} · 待领取 {fmt(e.pendingMythics)}，名称未提供</td></tr>]:[])]})}</tbody></table></div>:<p className="report-note">本段尚无装备结算证据。桥接0.3.3可采集新操作的结算收据和背包差分；旧桥接与历史记录无法补出名称。</p>}<p className="report-note">结算收据优先；背包差分只证明操作期间新增，不能确定是哪只怪物掉落。未提供名称的邮箱和待领取装备只记数量。</p></section>
  <section className="report-section"><div className="section-bar"><h2>已确认取得的卡牌</h2><Badge color="gray">{acquired.length} 次</Badge></div>{acquired.length?<div className="table-scroll"><table><thead><tr><th>轮次</th><th>深度</th><th>卡牌</th><th>效果</th></tr></thead><tbody>{acquired.map((c,i)=><tr key={i}><td>{c.round}</td><td>P{c.depth}</td><th scope="row">{c.cardName||c.cardId}<small className="report-code">{c.cardId}</small></th><td>{c.cardDescription||'未提供效果文本'}</td></tr>)}</tbody></table></div>:<p className="report-note">所选范围尚无已确认卡牌回执。</p>}</section>
 </>;
}
