import React from 'react';
import {Switch} from '@radix-ui/themes';
import {Flame, Lightning} from '@phosphor-icons/react';

export default function BasicAttackSettings({config, onChange}) {
  return <section className="settings-section basic-attack-settings" aria-labelledby="basic-attack-title">
    <h2 id="basic-attack-title">普攻状态</h2>
    <p className="section-desc">冻结普攻先拿一份并继承；随后依次优先全体弹跳、独立普攻各一份、引爆器一份、火增伤及其他增伤。尚无冻结时冰系作前期兜底，不压过弹跳；继承后不再选冰减速，尽量避开冰伤。数值牌重复降权，防御少量补充并遵守前X层护盾限制。</p>
    <div className="field">
      <span className="field-label">
        <strong className="attack-rule-label"><Flame size={18} aria-hidden="true"/>允许普攻点燃</strong>
        <small>默认允许。仅控制普攻施加点燃；开启后提高引爆器优先级，引爆器只拿并继承一份。关闭后，其他火系增伤与弹跳仍可选。</small>
      </span>
      <Switch aria-label="允许普攻点燃" checked={config.allowBasicBurn??true} onCheckedChange={value=>onChange('allowBasicBurn',value)}/>
    </div>
    <div className="field">
      <span className="field-label">
        <strong className="attack-rule-label"><Lightning size={18} aria-hidden="true"/>允许普攻带电</strong>
        <small>默认禁止。仅控制普攻施加带电，电系全体弹跳与其他增益照常比较；可单独开启。</small>
      </span>
      <Switch aria-label="允许普攻带电" checked={config.allowBasicLightning??false} onCheckedChange={value=>onChange('allowBasicLightning',value)}/>
    </div>
    <p className="footnote">保留游戏中的全部卡牌。开关只影响自动选择，不移除已持有效果，也不修改卡牌本身。</p>
  </section>;
}
