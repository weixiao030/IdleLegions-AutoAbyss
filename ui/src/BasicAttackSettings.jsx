import React from 'react';
import {Switch} from '@radix-ui/themes';
import {Flame, Lightning} from '@phosphor-icons/react';

export default function BasicAttackSettings({config, onChange}) {
  return <section className="settings-section basic-attack-settings" aria-labelledby="basic-attack-title">
    <h2 id="basic-attack-title">普攻状态</h2>
    <p className="section-desc">冻结未确认继承前可优先多拿，不因本章已有而降权；确认继承一份后不再追纯冻结，混合牌按新增弹跳和附带增益比较；随后依次优先全体弹跳、独立普攻各一份、引爆器一份、火增伤及其他增伤。冻结继承前优先只拿冰系（含冰减速），非冰系弹跳和火牌不抢选；本章已拿到冻结仍可优先多拿，等章末继承确认，临时徽章不算继承；继承后优先不再选冰减速，尽量避开冰伤。冻结收集阶段不因重复而降权；继承后独立状态不重复加分，附带增伤仍可降权比较；数值牌重复降权，防御少量补充并遵守前X层优先避盾。</p>
    <div className="field">
      <span className="field-label">
        <strong className="attack-rule-label"><Flame size={18} aria-hidden="true"/>允许普攻点燃</strong>
        <small>默认允许。仅控制普攻施加点燃；开启后提高引爆器优先级，引爆器正常优先只拿并继承一份。确认冻结继承后，其他火系增伤与弹跳仍可选；此开关不会提前解除冰系优先阶段。</small>
      </span>
      <Switch aria-label="允许普攻点燃" checked={config.allowBasicBurn??true} onCheckedChange={value=>onChange('allowBasicBurn',value)}/>
    </div>
    <div className="field">
      <span className="field-label">
        <strong className="attack-rule-label"><Lightning size={18} aria-hidden="true"/>允许普攻带电</strong>
        <small>默认禁止。仅控制普攻施加带电，冻结继承后电系全体弹跳与其他增益照常比较；可单独开启。</small>
      </span>
      <Switch aria-label="允许普攻带电" checked={config.allowBasicLightning??false} onCheckedChange={value=>onChange('allowBasicLightning',value)}/>
    </div>
    <p className="footnote">全部选牌策略无合适候选时，从游戏实际可选项择优兜底，不因偏好暂停；优先避盾、独立开关、效果去重及冰系阶段均适用。保留全部游戏卡，不移除已持有效果。</p>
  </section>;
}
