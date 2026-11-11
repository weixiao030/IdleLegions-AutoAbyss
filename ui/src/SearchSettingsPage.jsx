import React from 'react';
import {Switch} from '@radix-ui/themes';
import {TreeStructure, Cpu} from '@phosphor-icons/react';
import {searchDefaults} from './searchSettings.js';
import './learning.css';

const fields=[
 ['iterations','策略五搜索次数',1,1000000,'纯 UCT；每次决策的总搜索预算。'],
 ['adaptiveIterations','策略六搜索次数',1,1000000,'UCT 加实战校准；每条战斗前推会多次采样，耗时较高。'],
 ['neuralIterations','策略七搜索次数',1,1000000,'有效网络辅助 PUCT；模型未就绪时用 UCT。'],
 ['horizon','每条路径最多步数',1,128,'比较后续多少步操作；越长越慢，未知事件可能提前截止。'],
 ['workers','搜索并发',1,64,'同时工作的 CPU 搜索任务数；过高会争抢游戏资源。'],
 ['trainingEpochs','自动训练轮数',1,500,'整次下井结束且样本足够后，训练网络的轮数。'],
 ['exploration','探索系数',0.01,null,'越大越愿意探索较少访问的候选；默认 1.4。']
];

export default function SearchSettingsPanel({config,hardware,onChange,disabled}) {
 const v={...searchDefaults,...config.search},auto=v.mode==='auto',recommendation=hardware?.recommendation;
 const set=(key,value)=>onChange('search',{...v,[key]:value});
 const changeMode=mode=>onChange('search',{...v,...(mode==='auto'?recommendation?.parameters:{}),mode});
 return <>
  <section className="learning-panel search-settings" aria-label="搜索与训练设置">
   <div className="section-bar"><h2><TreeStructure size={20}/>搜索与自动训练</h2></div>
   <p>次数控制搜索预算，步数控制前推范围，并发控制 CPU 任务数。只有一个合法操作时直接核验；存在多个候选时才搜索。</p>
   <fieldset disabled={disabled}>
    <div className="search-mode training-device"><label htmlFor="search-mode">参数配置方式</label>
     <select id="search-mode" value={v.mode} onChange={e=>changeMode(e.target.value)} disabled={disabled||!recommendation} aria-describedby="search-mode-help"><option value="manual">手动（保留当前参数）</option><option value="auto">Auto（根据本机配置）</option></select>
     <small id="search-mode-help">Auto 根据 CPU 逻辑线程数与总内存选择起始预算，每次加载、保存及开始时重新核对。切回手动后，可编辑当前数值。训练设备与自动训练开关独立保留。</small>
    </div>
    <div className="search-hardware" aria-label="本机配置与自动参数" role="status"><Cpu size={22}/><div>
     <strong>{hardware?`${hardware.logicalProcessors} 个 CPU 逻辑线程 · ${hardware.memoryGiB==null?'内存读取失败':`${hardware.memoryGiB.toFixed(1)} GB 总内存`}`:'正在读取本机配置…'}</strong>
     <p>{recommendation?`${auto?'已采用':'Auto 推荐'}：${recommendation.level}。${recommendation.description}`:'配置来自本地程序，读取完成后可选择 Auto。'}</p>
     <p>按硬件容量给出保守起点，未测量 CPU 实际速度，不保证固定决策时间。内存读取失败时使用轻量配置。</p>
    </div></div>
    <fieldset disabled={disabled||auto} className="search-budget-fields"><legend className="search-budget-legend">{auto?'Auto 当前采用参数（切换手动后可编辑）':'手动搜索参数'}</legend><div className="search-fields">{fields.map(([key,label,min,max,hint])=><label key={key}><span>{label}</span><input aria-label={label} type="number" min={min} max={max??undefined} step={key==='exploration'?0.1:1} value={v[key]} onChange={e=>set(key,e.target.value===''?'':Number(e.target.value))}/><small>{hint} {max?`${min}～${max.toLocaleString()}`:'须为正数'}</small></label>)}</div></fieldset>
    <div className="training-device"><label htmlFor="training-device">神经网络训练设备</label><select id="training-device" aria-describedby="training-device-help" value={v.trainingDevice} onChange={e=>set('trainingDevice',e.target.value)}><option value="cpu">CPU（默认）</option><option value="cuda">CUDA（NVIDIA 显卡）</option></select><small id="training-device-help">仅用于自动训练；搜索、战斗模拟、推理和验证仍使用 CPU。Auto 不会自动开启 CUDA。训练开始时检测显卡，失败会提示并回退 CPU；无需 CUDA Toolkit，需要兼容的 NVIDIA 驱动。</small></div>
    <label className="learning-toggle"><Switch aria-label="每次下井结束后自动训练" checked={v.autoTrain} onCheckedChange={x=>set('autoTrain',x)}/><span>每次下井结束后自动训练<small>所有策略共用实战数据；样本不足或验证未改善时保留旧模型。</small></span></label>
   </fieldset>
  </section>
  <section className="learning-panel search-usage" aria-label="策略五至七参数说明"><h2>三种策略如何使用这些参数</h2><dl>
   <div><dt>策略五 · 纯 MCTS</dt><dd>使用“策略五搜索次数”，通过 UCT 和随机前推比较后续行动，不读取网络选牌。适合先用较低预算观察耗时。</dd></div>
   <div><dt>策略六 · 实战自校准</dt><dd>使用“策略六搜索次数”，结合已确认实战修正预测。战斗前推的采样更多，相同搜索次数也可能比策略五慢。长路径尤其吃 CPU。</dd></div>
   <div><dt>策略七 · 网络与 PUCT</dt><dd>使用“策略七搜索次数”。至少需要 4 次完整下井的 64 条训练样本，以及另 2 次下井的 16 条验证样本；通过验证后才启用网络。冷启动仍用 UCT。</dd></div>
  </dl><p>训练轮数、训练设备和自动训练开关作用于所有策略共享的网络训练。小网络、少量样本时，CUDA 可能因初始化与传输开销更慢。手动调参建议先改搜索次数，再观察路径步数与并发的实际耗时。</p></section>
 </>;
}
