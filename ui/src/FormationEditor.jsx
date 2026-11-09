import React, {useEffect, useState} from 'react';
import {Badge, Button, Select} from '@radix-ui/themes';
import {ArrowDown, ArrowUp} from '@phosphor-icons/react';
import {defaultFormation, names, placeProfession, positionNames, professions, validFormation} from './formation.js';

export default function FormationEditor({value, saved, onChange, disabled, hasRun, owned}) {
  const [feedback, setFeedback] = useState('');
  const valid = validFormation(value);
  const formation = Array.isArray(value) ? value : [];
  const changed = JSON.stringify(value) !== JSON.stringify(saved);
  useEffect(() => { if (!changed) setFeedback(''); }, [changed]);
  function select(index, id) {
    const next = placeProfession(valid ? formation : defaultFormation, index, id);
    const other = formation.indexOf(id);
    setFeedback(other >= 0 && other !== index ? `已交换${names[id]}与${names[formation[index]]}的位置。` : `已将${names[id]}放入${positionNames[index]}。`);
    onChange(next);
  }
  function move(index, direction) {
    const next = [...formation];
    [next[index], next[index + direction]] = [next[index + direction], next[index]];
    onChange(next);
    setFeedback(`${names[formation[index]]}已移至${positionNames[index + direction]}。`);
  }
  const missing = owned && valid ? formation.filter(id => !owned.includes(id)) : [];
  return <section className="settings-section formation-editor" aria-labelledby="formation-title">
    <div className="formation-heading"><h2 id="formation-title">职业与站位</h2><Badge color={changed ? 'amber' : 'gray'} variant="soft">{changed ? '正在自动保存' : '已保存'}</Badge></div>
    <p className="section-desc">四个位置自由选职业，从最前到最后排列。选择已上阵职业会自动交换位置。</p>
    <div className="formation-rows">
      {['前排', '后排'].map((row, rowIndex) => <fieldset className="formation-row" key={row} disabled={disabled}>
        <legend>{row}<span>{rowIndex === 0 ? '靠前的两位' : '靠后的两位'}</span></legend>
        {[rowIndex * 2, rowIndex * 2 + 1].map(index => <div className="formation-slot" key={index}>
          <label htmlFor={`formation-slot-${index}`}>{positionNames[index]}{index === 0 ? ' · 最前' : index === 3 ? ' · 最后' : ''}</label>
          <Select.Root value={Object.hasOwn(names, formation[index]) ? formation[index] : ''} onValueChange={id => select(index, id)} disabled={disabled}>
            <Select.Trigger id={`formation-slot-${index}`} aria-label={positionNames[index]} placeholder="选择职业" size="3"/>
            <Select.Content position="popper">{professions.map(p => <Select.Item key={p.id} value={p.id}>{p.name}</Select.Item>)}</Select.Content>
          </Select.Root>
          <div className="formation-move"><Button type="button" variant="ghost" color="gray" size="1" disabled={disabled || !valid || index === 0} onClick={() => move(index, -1)} aria-label={`${positionNames[index]}前移`}><ArrowUp size={14}/>前移</Button><Button type="button" variant="ghost" color="gray" size="1" disabled={disabled || !valid || index === 3} onClick={() => move(index, 1)} aria-label={`${positionNames[index]}后移`}><ArrowDown size={14}/>后移</Button></div>
        </div>)}
      </fieldset>)}
    </div>
    <p className="formation-preview"><strong>当前选择</strong><span>{valid ? formation.map(id => names[id]).join(' → ') : '请选择 4 个不同的职业'}</span></p>
    {!valid && <p role="alert" className="formation-error">阵容需要 4 个不同的有效职业。请重新选择，或恢复默认阵容。</p>}
    {missing.length > 0 && <p className="footnote">当前账号未拥有：{missing.map(id => names[id]).join('、')}。可以保存方案，入场前请更换为已拥有的职业。</p>}
    <p className="footnote">{hasRun ? '当前深渊已入场，更改自动保存，下一次入场使用。' : '更改自动保存，下次入场将按这个顺序组队。'}{disabled ? '请先暂停下潜，再调整站位。' : ''}</p>
    <div className="formation-feedback" role="status" aria-live="polite">{feedback}</div>
    <div className="formation-actions"><Button type="button" variant="ghost" color="gray" disabled={disabled || JSON.stringify(value) === JSON.stringify(defaultFormation)} onClick={() => {onChange([...defaultFormation]); setFeedback('已恢复默认阵容，正在自动保存。');}}>恢复默认</Button></div>
  </section>;
}
