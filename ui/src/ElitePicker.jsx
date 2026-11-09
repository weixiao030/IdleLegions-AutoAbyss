import React, {useState} from 'react';
import {Popover, Button, Checkbox, TextField, Badge} from '@radix-ui/themes';
import {CaretDown, MagnifyingGlass} from '@phosphor-icons/react';
import elites from '../../shared/elites.json';

export default function ElitePicker({value = [], onChange, disabled}) {
  const [search, setSearch] = useState('');
  const selected = Array.isArray(value) ? value : [];
  const visible = elites.filter(e => (e.name + e.group).includes(search.trim()));
  const groups = [...new Set(visible.map(e => e.group))];
  return <div className="elite-picker">
    <div className="elite-picker-heading"><strong id="elite-picker-label">优先跳过的精英怪</strong><Badge color="gray" variant="soft">{selected.length ? `已选 ${selected.length} 种` : '默认不选'}</Badge></div>
    <p id="elite-picker-help">不选时沿用常规路线。选中只降低遇到它的路线评分；必经或明显更安全时仍会前进。</p>
    <Popover.Root onOpenChange={open => { if (!open) setSearch(''); }}>
      <Popover.Trigger><Button variant="outline" disabled={disabled} aria-labelledby="elite-picker-label" aria-describedby="elite-picker-help">{selected.length ? '调整避让名单' : '选择精英，可多选'}<CaretDown/></Button></Popover.Trigger>
      <Popover.Content className="elite-popover" align="start" aria-label="精英避让名单">
        <div className="elite-menu-heading"><strong>精英避让名单</strong><Button variant="ghost" size="1" disabled={disabled||!selected.length} onClick={() => onChange([])}>清空选择</Button></div>
        <TextField.Root aria-label="搜索精英" placeholder="搜索名称或大厅" disabled={disabled} value={search} onChange={e => setSearch(e.target.value)}><TextField.Slot><MagnifyingGlass/></TextField.Slot></TextField.Root>
        <div className="elite-options" role="group" aria-label="精英怪多选">
          {groups.map(group => <fieldset key={group}><legend>{group}</legend>{visible.filter(e => e.group === group).map(e => <label className="elite-option" key={e.id}><Checkbox aria-label={e.name} disabled={disabled} checked={selected.includes(e.id)} onCheckedChange={checked => onChange(checked ? [...selected, e.id] : selected.filter(id => id !== e.id))}/><span>{e.name}</span></label>)}</fieldset>)}
          {!visible.length && <p className="elite-empty">没有匹配的精英，试试其他名称。</p>}
        </div>
        <p className="elite-menu-note">共 {elites.length} 种，含伏击精英。关闭后点击“保存参数”生效。</p>
      </Popover.Content>
    </Popover.Root>
    {selected.length > 0 && <p className="elite-selected" aria-live="polite">{elites.filter(e => selected.includes(e.id)).map(e => e.name).join('、')}</p>}
  </div>;
}
