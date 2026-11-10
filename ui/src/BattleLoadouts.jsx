import React from 'react';
import ReportTable from './ReportTable.jsx';
import {gameText} from './gameText.js';
const value=n=>n==null?'未记录':Number(n).toLocaleString('zh-CN',{maximumFractionDigits:2});
const labels={atk:'攻击',crit:'暴击率',critdmg:'暴击伤害',def:'防御',dodge:'闪避',heal:'治疗',healer:'治疗者',hp:'生命',magic:'魔法职业',mspd:'移动速度倍率',pmag:'魔法伤害',spd:'攻击速度',spdTope:'攻击速度上限'};
export default function BattleLoadouts({hero:h}) {
 return <details className="damage-extra-disclosure"><summary>战前构筑与实际技能参数 · {h.loadouts?.length||0} 组</summary>
  <p className="report-note">操作发出前冻结的账号属性与技能参数。同一构筑合并显示；深渊遗物、祝福和敌方减伤仍可能改变实际伤害，面板数值不等于战斗最终属性。</p>
  {h.loadoutMissing>0&&<p className="report-note">{h.loadoutMissing} 场没有战前快照，保留缺失状态；不会使用当前装备或基础技能倍率补填。</p>}
  {(h.loadouts||[]).map((c,i)=><details className="damage-related-tags" key={i}><summary>构筑 {i+1} · {c.battles.length} 场 · 等级 {value(c.level)} · 技能槽 {value(c.skillSlots)}</summary>
   <p className="report-note">{c.battles.map(b=>`第 ${b.round} 轮 P${b.depth}`).filter((v,i,a)=>a.indexOf(v)===i).join('、')}</p>
   <dl className="damage-stat-list">{Object.entries(c.stats||{}).map(([key,n])=><div key={key}><dt>{labels[key]||'属性'}</dt><dd>{typeof n==='boolean'?(n?'是':'否'):value(n)}{['crit','critdmg','dodge'].includes(key)?'%':''}</dd></div>)}</dl>
   <ReportTable label={h.name+'战前技能参数'} scopeKey={String(i)} headings={['技能 / 等级','冷却（秒）','状态','实际效果说明']} rows={c.skills||[]} renderRow={s=><tr key={s.id}><th scope="row">{gameText(s.name)} · {value(s.level)} 级</th><td>{value(s.cooldown)}</td><td>{s.equipped==null?'未记录':s.equipped?'已装备':'未装备'}</td><td className="damage-effect">{gameText(s.description)||'本场未提供说明'}</td></tr>}/>
  </details>)}
 </details>;
}
