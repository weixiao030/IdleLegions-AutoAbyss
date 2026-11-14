import React, {useEffect, useState} from 'react';
import {AlertDialog, Button, Checkbox} from '@radix-ui/themes';
import {Database, ArrowClockwise, Archive, Trash, CircleNotch} from '@phosphor-icons/react';
import './storage.css';

const size=bytes=>bytes>=1073741824?`${(bytes/1073741824).toFixed(2)} GB`:bytes>=1048576?`${(bytes/1048576).toFixed(1)} MB`:`${(bytes/1024).toFixed(1)} KB`;

export default function DataStoragePage({api,active,onAction,message}) {
 const [data,setData]=useState(null),[error,setError]=useState(''),[selected,setSelected]=useState([]),[confirm,setConfirm]=useState(false),[refreshing,setRefreshing]=useState(false);
 async function refresh(){setRefreshing(true);try{setData(await api('storage'));setError('');}catch(e){setError(e.message)}finally{setRefreshing(false)}}
 useEffect(()=>{let alive=true;const update=async()=>{try{const next=await api('storage');if(alive){setData(next);setError('')}}catch(e){if(alive)setError(e.message)}};update();const timer=setInterval(update,3000);return()=>{alive=false;clearInterval(timer)}},[]);
 const usage=data?.usage,groups=usage?.groups||[],chosen=groups.filter(g=>selected.includes(g.id)),bytes=chosen.reduce((sum,g)=>sum+g.bytes,0),progress=data?.progress;
 return <>
  <div className="page-title"><div><h1>数据管理</h1><p>查看本机占用，无损压缩历史数据，或删除不再需要的记录。</p></div><Button variant="outline" disabled={refreshing} onClick={refresh}><ArrowClockwise className={refreshing?'spin':''}/>刷新占用</Button></div>
  {error&&<p role="alert" className="notice error">{error}</p>}
  <section className="storage-summary learning-panel" aria-label="本机数据占用"><Database size={28}/><div><span>工具数据总占用</span><strong>{usage?`${usage.totalGiB.toFixed(2)} GB`:'正在统计…'}</strong><small>1 GB = 1024³ 字节{usage?` · 共 ${groups.reduce((sum,g)=>sum+g.files,0).toLocaleString()} 个文件`:''}</small></div><p>占用会随实战记录增加。压缩保留完整内容；删除记录会减少可回看和可训练的数据，已发布的网络和参数继续保留。</p></section>
  <section className="learning-panel storage-panel"><h2>历史精简</h2><p>将历史战报和实战学习记录无损压缩，逐文件校验后替换原文件。工具可直接读取压缩后的记录继续学习和回看；新增记录可再次压缩。需要使用此版本或更新版本的工具读取。</p><Button disabled={active||!usage} onClick={()=>onAction('storage',{action:'compact'})}><Archive/>无损压缩历史数据</Button>{active&&<p className="footnote">运行、训练或维护期间不能清理数据；请先暂停下潜并等待训练完成。</p>}
   {progress?.active&&<div role="status" className="storage-progress"><CircleNotch className="spin"/><span>{progress.message} · {progress.completed.toLocaleString()} / {progress.total.toLocaleString()} · 已释放 {size(progress.freedBytes)}</span></div>}
   {message&&<p role="status" className="footnote">{message}</p>}
  </section>
  <section className="learning-panel storage-panel"><h2>按类别删除</h2><div className="storage-table-wrap"><table className="storage-table"><thead><tr><th>选择</th><th>数据类别</th><th>文件数</th><th>占用空间</th></tr></thead><tbody>{groups.map(g=><tr key={g.id}><td>{g.deletable?<Checkbox aria-label={`选择${g.name}`} disabled={active||!g.files} checked={selected.includes(g.id)} onCheckedChange={checked=>setSelected(s=>checked?[...s,g.id]:s.filter(id=>id!==g.id))}/>:<span className="muted">保留</span>}</td><th scope="row">{g.name}</th><td>{g.files.toLocaleString()}</td><td title={`${g.bytes.toLocaleString()} 字节`}>{size(g.bytes)}</td></tr>)}</tbody></table></div><p className="footnote">删除战报会清除对应的经验模型缓存；删除实战学习记录会减少后续训练样本。加载缓存可自动重建。上次运行展示记录与完整战报分开管理。</p><div className="storage-actions"><Button color="red" variant="soft" disabled={active||!bytes} onClick={()=>setConfirm(true)}><Trash/>删除所选数据</Button><span>已选 {chosen.length} 类 · {size(bytes)}</span></div><p className="storage-path">数据目录：{usage?.path||'正在读取…'}</p></section>
  <AlertDialog.Root open={confirm} onOpenChange={setConfirm}><AlertDialog.Content maxWidth="500px"><AlertDialog.Title>确认删除所选数据</AlertDialog.Title><AlertDialog.Description>将永久删除以下记录，预计释放 {size(bytes)}。操作无法撤销；参数和已训练神经网络保留。</AlertDialog.Description><ul>{chosen.map(g=><li key={g.id}>{g.name}：{g.files.toLocaleString()} 个文件，{size(g.bytes)}</li>)}</ul><div className="storage-actions"><AlertDialog.Cancel><Button color="gray" variant="soft">取消</Button></AlertDialog.Cancel><AlertDialog.Action><Button color="red" disabled={active} onClick={()=>{onAction('storage',{action:'delete',groups:chosen.map(g=>g.id),confirm:true});setSelected([])}}>确认永久删除</Button></AlertDialog.Action></div></AlertDialog.Content></AlertDialog.Root>
 </>;
}
