import React, {useState} from 'react';
import {Button, Select, TextField} from '@radix-ui/themes';
import {Play, Pause, Compass, CircleNotch} from '@phosphor-icons/react';

// One launch action for all modes. Resume keeps the existing session and reports.
export default function RunControls({state,draft,busy,invalid,onAction,onChange,saveState,strategy}) {
  const [mode,setMode]=useState(null);
  const active=state.active, sessionOpen=state.loop?.sessionOpen;
  const selected=(mode==='resume'&&!sessionOpen?'single':mode)??(sessionOpen?'resume':'single');
  const locked=active||!!busy;
  const countValid=Number.isInteger(draft.maxRuns)&&draft.maxRuns>0;
  const cannotStart=!state.connected||!!busy||invalid||(selected==='count'&&!countValid);
  const help=selected==='resume'?'沿用暂停前的运行计划，继续累计本轮报告。':selected==='ropes'?'每轮结束后再次入场，直到绳索用完；保留 0 根。':selected==='count'?`每轮结束后再次入场；保留 ${draft.reserveRopes} 根绳索。`:'只运行一轮，到最深处或战败后结束。';
  const start=()=>selected==='resume'?onAction('start'):onAction('loop',selected==='count'?{mode:'count',count:draft.maxRuns}:{mode:selected});
  return <section className="command-panel" aria-label="运行计划">
    <div className="command-options">{strategy}<div className="run-mode"><label htmlFor="run-mode">运行方式</label><div className="mode-fields"><Select.Root value={selected} onValueChange={setMode} disabled={locked}><Select.Trigger id="run-mode" aria-label="运行方式" size="3"/><Select.Content position="popper">{sessionOpen&&<Select.Item value="resume">继续当前计划</Select.Item>}<Select.Item value="single">单次下潜</Select.Item><Select.Item value="count">循环指定次数</Select.Item><Select.Item value="ropes">循环到绳索用完</Select.Item></Select.Content></Select.Root>{selected==='count'&&<label className="run-count"><TextField.Root aria-label="循环次数" type="text" inputMode="numeric" value={draft.maxRuns} disabled={locked} onChange={e=>onChange('maxRuns',/^[+-]?\d+$/.test(e.target.value)?Number(e.target.value):e.target.value)} size="3"><TextField.Slot side="right">次</TextField.Slot></TextField.Root></label>}</div><p>{help}</p></div></div>
    <div className="command-launch"><div className="launch-status"><span className={'status-dot '+(active?'online':'')}/><span>{active?'正在自动下潜':sessionOpen?'本轮已暂停':'准备开始'}</span></div>{active?<Button className="primary-action" size="3" variant="solid" color="gray" disabled={busy==='pause'} onClick={()=>{setMode(null);onAction('pause');}}>{busy==='pause'?<CircleNotch className="spin" size={18}/>:<Pause size={18}/>}暂停下潜</Button>:<Button className="primary-action" size="3" disabled={cannotStart} onClick={start}>{busy==='start'||busy==='loop'?<CircleNotch className="spin" size={18}/>:<Play weight="fill" size={18}/>} {selected==='resume'?'继续下潜':'开始下潜'}</Button>}<Button variant="ghost" color="gray" size="2" disabled={!state.connected||locked||invalid} onClick={()=>onAction('analyze')}><Compass size={17}/>{busy==='analyze'?'正在推演…':'仅推演，不执行'}</Button></div>
    <div className="command-footer"><span>{state.loop?.sessionOpen?`本轮已完成 ${state.loop.completed||0}${state.loop.mode==='ropes'?' 次 · 绳索用完结束':` / ${state.loop.target??state.config.maxRuns} 次`}`:'选择后点击开始，参数会自动保存。'}</span>{saveState}</div>
  </section>;
}
