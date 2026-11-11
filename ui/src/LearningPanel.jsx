import React from 'react';
import {Badge} from '@radix-ui/themes';
import {Brain, Database} from '@phosphor-icons/react';
import './learning.css';

export function LearningStatus({learning,strategy=1}) {
 const c=learning?.corpus||{},n=learning?.neural||{};
 return <section className="learning-panel" aria-label="实战学习状态">
  <div className="section-bar"><h2><Database size={20}/>实战学习</h2><Badge color={n.training?'orange':n.ready?'green':'gray'}>{n.training?'训练中':n.ready?`模型 v${n.version}`:'积累样本'}</Badge></div>
  <p>所有策略都会记录已确认的实战。整次下井结束后回填最终层数，自动训练与验证。</p>
  <dl className="learning-metrics"><div><dt>历史战斗</dt><dd>{c.archivedBattles??0}</dd></div><div><dt>已确认决策</dt><dd>{c.confirmed??0}</dd></div><div><dt>完整下井</dt><dd>{c.completedEpisodes??0}</dd></div><div><dt>训练 / 验证</dt><dd>{n.trainSamples??0} / {n.validationSamples??0}</dd></div></dl>
  <p className="learning-state" role="status"><Brain size={18}/>{n.message||'首次推演时读取历史战斗；新的完整轨迹从本版本开始保存。'}</p>
  <p>本次 / 最近训练设备：{n.device||'未开始'}{n.deviceName?` · ${n.deviceName}`:''}。搜索与选牌推理使用 CPU。</p>
  {n.fallbackReason&&<p role="status" className="device-fallback">{n.fallbackReason}</p>}
  {strategy>=5&&<p className="footnote">{strategy===5?'纯 UCT 搜索，不使用神经网络和手动系列排序。':strategy===6?'UCT 结合历次实战校准，持续纠正战斗预测。':'网络通过整次下井留出验证后参与 PUCT；样本不足或验证未改善时继续使用搜索。'} 未见菜单和地图按历史样本近似；事件或契约的后果未完整建模时，会截止该条前推。实际胜负以游戏回执为准。</p>}
 </section>;
}
