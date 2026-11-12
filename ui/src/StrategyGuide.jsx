import React from 'react';
import {Button, Dialog, Tabs, IconButton} from '@radix-ui/themes';
import {BookOpen, X} from '@phosphor-icons/react';

const sharedOpening='优先冰川（单卡冻结＋全体弹跳）；没有冰川就先凑冰系4张，不单独追普通冻结。确认继承冰川或4张冰系遗物后，再执行所选策略；本层新牌、徽章与临时赐福不算已继承遗物。';
const opening=[sharedOpening,'之后补齐七系各4张，已满系列让位；全部达标后忽略系列顺序，比较单卡新增收益。','本层获取与章末继承分开计数，徽章、赐福不算遗物张数；没有冰牌的强制菜单合法过渡。'];
export const strategies=[
 {id:1,name:'策略一',short:'当前默认',detail:'冰川优先，否则先冰系4张；再补齐七系各4张，全部达标后比较单卡收益。',steps:[...opening,'使用当前战斗模拟比较候选与目标分配；有冻结时按奖励阶段偏好战斗，之后按设置避战。'],pros:'阶段明确、无需设置单卡顺序，适合希望自动补羁绊的玩家。',cons:'固定的补系阶段可能让位给短期更强的单卡；模拟规则仍有误差。',suitable:'日常自动下潜，先从默认参数观察。'},
 {id:2,name:'策略二',short:'经典修正版',detail:'保留经典战斗模型，修正继承识别与重复冻结偏置。',steps:[sharedOpening,'使用经典1.3.1战斗与选牌模型，不读取策略四的系列、单卡或光系配置。','按到达回执区分已继承与本层新牌；已有普通冻结或减速时，重复效果降权。','比较可用的遗物、徽章、商店和战斗目标；无正常候选的强制菜单使用合法兜底。','保持经典构筑优先级，不采用策略一、三、四的新七系4张阶段。'],pros:'保留熟悉的经典行为，方便与新版策略对照。',cons:'旧模型覆盖的效果较少，也没有后续多步树搜索；不能通过策略四排序改变它。',suitable:'偏好经典行为或需要做版本对照的玩家。'},
 {id:3,name:'策略三',short:'冰系后期',detail:'冰川或冰系4张前置，满系让位，补齐七系各4张后比较单卡收益。',steps:[...opening,'本版本与策略一共用修正后的补羁绊与新增收益规则；不读取策略四手动排序和光系开关。'],pros:'避免冰系不断叠张而其他系未满4张，保留冰系开局入口。',cons:'当前选牌阶段与策略一一致，不代表独立算法或更高通关率；不做多步树搜索。',suitable:'沿用策略三习惯，关注冰系开局与后期补系的玩家。'},
 {id:4,name:'策略四',short:'收益比较 / 自定义',detail:'相同冰系和七系4张阶段，可自行排序；光系特殊继承默认关闭。',steps:[...opening,'默认“收益比较”；手动“系列排序”按未满系列、细分类和单卡比较。七系达标后系列排序不再参与。','支持目标张数、软避让、拉黑和专用导入导出；偏好不会把不可用卡变为可用。','可点击“使用此配置”：优先继承狂热、连祷各一份，其他光牌本层可凑羁绊、章末优先让位。此开关默认关闭，且不改已有单卡设置。'],pros:'能够表达个人构筑偏好，配置可保存分享；保留合法兜底。',cons:'自定义目标和避让可能牺牲羁绊或短期收益；光系特殊继承是七系4张目标的可选例外。',suitable:'清楚自己想要哪些卡、需要调整排序或继承规则的玩家。'},
 {id:5,name:'策略五',short:'纯 MCTS',detail:'随机前推、UCT选择和回传，比较后续行动；不用系列置顶和神经网络。',steps:[sharedOpening,'从游戏实际合法操作建立搜索根节点，覆盖选牌、路线、战斗和补给。','以UCT平衡探索和利用，随机前推后回传结果，继承前遵守共用冰川或冰系4卡前置，继承后不强制七系4张。','默认1024次搜索、路径最多20步、并发最多默认8；可在策略参数调整。','搜索使用CPU。所有策略仍记录已确认实战；自动训练可为策略七积累模型，但模型不参与策略五选牌。'],pros:'会比较多步后果，不受手动系列置顶束缚；不需要有效网络即可运行。',cons:'CPU计算量与等待时间更高；未见地图、菜单和未完整建模事件会限制前推准确度。',suitable:'希望尝试多步搜索，并愿意接受决策等待的玩家。'},
 {id:6,name:'策略六',short:'实战自校准 MCTS',detail:'UCT搜索结合全部历史实战校准，持续记录战斗与决策。',steps:[sharedOpening,'使用UCT多步搜索，并结合历史战斗和已确认决策校正预测残差。','冰川或冰系4卡继承后不强制七系4张或手动系列排序；搜索按当前可用构筑与后续行动比较。','默认4096次搜索，路径20步；搜索仍用CPU。建议以16核32线程、32GB内存和SSD作为观察起点。','新实战会积累校准数据；CUDA只可用于共享网络训练，不会让策略六的树搜索改用GPU。'],pros:'可用实际回执修正部分模型偏差，不必手工指定构筑顺序。',cons:'极度吃CPU；历史样本少或分布变化时校准帮助有限，不能修复所有缺失规则。',suitable:'有较强CPU、愿意持续积累实战并观察误差的玩家。'},
 {id:7,name:'策略七',short:'神经网络 + PUCT',detail:'小型策略／价值网络辅助PUCT；模型未通过验证时用UCT。',steps:[sharedOpening,'使用112维输入、64单元tanh隐层、策略与价值双头网络辅助PUCT搜索。默认2048次搜索、路径20步。','没有通过验证的模型时明确用UCT冷启动，不用随机权重决定选牌。','所有策略共同积累实战；只使用已确认、已结束的下井轨迹，按整次下井分割训练与验证。','至少需要4次完整下井的64条训练样本，以及另2次完整下井的16条验证样本；验证改善且价值误差不退化才启用模型。','训练设备可选CPU或CUDA，默认CPU；CUDA需要兼容的NVIDIA驱动，失败从初始权重重做CPU训练。搜索、战斗模拟、推理和验证仍用CPU。'],pros:'能够利用真实轨迹学习行动和价值，通过独立验证后才更新模型。',cons:'冷启动需要数据；小网络不保证强于UCT或规则策略，CUDA少量样本也可能更慢。',suitable:'愿意长期积累数据、对照模型版本和实际成绩的玩家。'}
];

export default function StrategyGuide({strategyId=1}) {
 return <Dialog.Root><Dialog.Trigger><Button variant="outline"><BookOpen size={18}/>策略说明</Button></Dialog.Trigger>
  <Dialog.Content className="strategy-guide-dialog" maxWidth="880px">
   <Dialog.Close><IconButton variant="ghost" className="strategy-guide-close" aria-label="关闭策略说明"><X size={20}/></IconButton></Dialog.Close>
   <Dialog.Title>七套策略说明</Dialog.Title>
   <Dialog.Description>选择编号查看决策规则、优点与局限。浏览说明不会切换当前策略。</Dialog.Description>
   <Tabs.Root defaultValue={String(strategyId)} key={strategyId} className="strategy-guide-tabs">
    <Tabs.List aria-label="策略说明目录">{strategies.map(s=><Tabs.Trigger key={s.id} value={String(s.id)}>{s.name}</Tabs.Trigger>)}</Tabs.List>
    {strategies.map(s=><Tabs.Content key={s.id} value={String(s.id)} className="strategy-guide-panel">
     <h2>{s.name} · {s.short}</h2><p>{s.detail}</p><h3>如何决策</h3><ol>{s.steps.map((step,i)=><li key={i}>{step}</li>)}</ol>
     <dl><dt>优点</dt><dd>{s.pros}</dd><dt>局限</dt><dd>{s.cons}</dd><dt>适用情况</dt><dd>{s.suitable}</dd></dl>
    </Tabs.Content>)}
   </Tabs.Root>
   <p className="strategy-guide-common">全部策略都遵守游戏实际可用菜单、正常消费与回执核验，全程禁止撤离。预测结果不保证真实胜负；单次层数与耗时也受角色、装备、随机菜单和网络影响。</p>
   <div className="strategy-guide-footer"><Dialog.Close><Button>关闭说明</Button></Dialog.Close></div>
  </Dialog.Content>
 </Dialog.Root>;
}
