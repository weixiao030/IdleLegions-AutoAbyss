import {customError} from './customStrategy.js';
import {useEffect, useRef, useState} from 'react';

const integers = ['simulations','seed','delayMs','startDepth','stopDepth','maxRuns','clearFloors','reserveRopes','rewardFloors','pactFloors','shieldSkipFloors','strategyId'];
const decimals = ['retreatHp','minWinRate','riskAversion','freezeWeight','enemyStrength','modelUncertainty','coreRewardWeight','shardRewardWeight','freezeDuration'];
export function configError(config) {
  if (!config) return '正在载入参数';
  if ([...integers,...decimals].some(k => typeof config[k] !== 'number' || !Number.isFinite(config[k]))) return '请输入完整的有限数字，填写完成后自动保存。';
  if (integers.some(k => !Number.isInteger(config[k]))) return '次数、种子、深度、层数、绳索和毫秒间隔须填写整数。';
  if (config.strategyId < 1 || config.strategyId > 4) return '请选择策略一至策略四。';
  if (config.strategyId === 4) return customError(config.customStrategy);
  return '';
}

// One serialized writer for parameters, formation, paths and imports. Responses
// acknowledge only the snapshot sent, never replace newer edits in the UI.
export default function useAutoSave(api, storageKey, locked, onSaved) {
  const [draft,setDraft] = useState(null), [status,setStatus] = useState({kind:'loading',text:'正在载入参数'});
  const current = useRef(null), saved = useRef(''), timer = useRef(), writer = useRef(null), blocked = useRef(locked), savedCallback = useRef(onSaved);
  blocked.current=locked; savedCallback.current=onSaved;
  const encode = value => JSON.stringify(value);
  function remember(value) { try {localStorage.setItem(storageKey,encode({draft:value}));} catch {} }
  function schedule(delay=650) {
    clearTimeout(timer.current);
    timer.current=setTimeout(()=>{if(!blocked.current)flush().catch(()=>{});},delay);
  }
  function accept(value, force=false) {
    if(current.current && !force)return;
    saved.current=encode(value);
    let next=value;
    if(!force) {
      try {const cache=JSON.parse(localStorage.getItem(storageKey));if(cache?.draft && typeof cache.draft==='object')next={...value,...cache.draft};} catch {}
    }
    current.current=next;setDraft(next);
    if(encode(next)!==saved.current){setStatus({kind:'pending',text:'正在恢复未完成的自动保存…'});schedule();}
    else {try{localStorage.removeItem(storageKey);}catch{} setStatus({kind:'saved',text:'全部更改已自动保存'});}
  }
  function replace(next) {
    current.current=next;setDraft(next);remember(next);
    const invalid=configError(next);
    setStatus(invalid?{kind:'invalid',text:invalid}:{kind:'pending',text:'更改待自动保存…'});
    schedule();
  }
  function change(name,value){replace({...current.current,[name]:value});}
  async function flush() {
    clearTimeout(timer.current);
    if(writer.current) {await writer.current;return flush();}
    if(!current.current)return;
    const invalid=configError(current.current);
    if(invalid){setStatus({kind:'invalid',text:invalid});throw Error(invalid);}
    if(encode(current.current)===saved.current)return;
    if(blocked.current)throw Error('请暂停下潜，等待当前操作结束后自动保存。');
    const sent=current.current, serialized=encode(sent);
    setStatus({kind:'saving',text:'正在自动保存…'});
    const task=api('config',sent);
    writer.current=task;
    try {
      await task;saved.current=serialized;savedCallback.current(sent);
      if(encode(current.current)===serialized){try{localStorage.removeItem(storageKey);}catch{} setStatus({kind:'saved',text:'全部更改已自动保存'});}
      else setStatus({kind:'pending',text:'正在保存最新更改…'});
    } catch(e) {
      setStatus({kind:'error',text:`自动保存失败：${e.message} 将自动重试，草稿已保留。`});schedule(4000);throw e;
    } finally {writer.current=null;}
    if(encode(current.current)!==saved.current)return flush();
  }
  useEffect(()=>{if(!locked&&current.current&&encode(current.current)!==saved.current)schedule();},[locked]);
  useEffect(()=>{
    const unload=e=>{if(current.current&&encode(current.current)!==saved.current){remember(current.current);}};
    window.addEventListener('beforeunload',unload);
    return()=>{clearTimeout(timer.current);window.removeEventListener('beforeunload',unload);};
  },[]);
  return {draft,status,change,replace,accept,flush,invalid:configError(draft)};
}
