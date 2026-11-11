import React, {useEffect,useRef,useState} from 'react';
import {DotsSixVertical} from '@phosphor-icons/react';
import {moveTo} from './customStrategy.js';

// Only the handle captures touch input; the rest of each row remains scrollable.
// Commit once on release, so a long drag produces one autosave operation.
export default function StrategyOrderList({items,labelFor,onReorder,disabled,children,classFor=()=>'',label}) {
 const list=useRef(null),gesture=useRef(null),latest=useRef({items,onReorder});
 latest.current={items,onReorder};
 const [drag,setDrag]=useState(null),[message,setMessage]=useState('');
 function cancel(){const g=gesture.current;if(g)clearTimeout(g.timer);gesture.current=null;setDrag(null);}
 useEffect(()=>{if(disabled)cancel();},[disabled]);
 useEffect(()=>{const escape=e=>{if(e.key==='Escape')cancel();};window.addEventListener('keydown',escape);return()=>{window.removeEventListener('keydown',escape);if(gesture.current)clearTimeout(gesture.current.timer);};},[]);
 function down(e,id){
  if(disabled||e.button!==0)return;
  cancel();const handle=e.currentTarget;
  const g={id,over:id,pointer:e.pointerId,x:e.clientX,y:e.clientY,active:false};gesture.current=g;
  handle.setPointerCapture(e.pointerId);
  g.timer=setTimeout(()=>{if(gesture.current===g){g.active=true;setDrag({id,over:id});setMessage(`正在移动${labelFor(id)}，拖到目标位置后松开，按退出键取消。`);}},320);
 }
 function moving(e){
  const g=gesture.current;if(!g||g.pointer!==e.pointerId)return;
  if(!g.active){if(Math.hypot(e.clientX-g.x,e.clientY-g.y)>9)cancel();return;}
  e.preventDefault();
  const row=document.elementsFromPoint(e.clientX,e.clientY).map(n=>n.closest('[data-sort-id]')).find(n=>n&&n.closest('ol')===list.current);
  if(row&&row.dataset.sortId!==g.over){g.over=row.dataset.sortId;setDrag({id:g.id,over:g.over});}
  if(e.clientY<75)window.scrollBy(0,-24);else if(e.clientY>window.innerHeight-75)window.scrollBy(0,24);
 }
 function up(e){
  const g=gesture.current;if(!g||g.pointer!==e.pointerId)return;
  const {items,onReorder}=latest.current;
  if(g.active&&g.id!==g.over){const to=items.indexOf(g.over);onReorder(moveTo(items,items.indexOf(g.id),to));setMessage(`${labelFor(g.id)}已移动至第${to+1}位。`);}
  cancel();
 }
 function keyboard(e,id){
  const {items,onReorder}=latest.current,from=items.indexOf(id);
  const to=e.key==='ArrowUp'?from-1:e.key==='ArrowDown'?from+1:e.key==='Home'?0:e.key==='End'?items.length-1:null;
  if(to===null||disabled)return;e.preventDefault();
  if(to>=0&&to<items.length){onReorder(moveTo(items,from,to));setMessage(`${labelFor(id)}已移动至第${to+1}位。`);}
 }
 return <><ol ref={list} aria-label={label}>{items.map((id,index)=><li key={id} data-sort-id={id} className={`${classFor(id)} ${drag?.id===id?'is-dragging':''} ${drag?.over===id?'is-drop-target':''}`}>
  <button type="button" className="custom-drag-handle" disabled={disabled} aria-label={`长按拖动${labelFor(id)}排序，也可用方向键`} title="长按拖动排序；方向键也可移动" onPointerDown={e=>down(e,id)} onPointerMove={moving} onPointerUp={up} onPointerCancel={cancel} onLostPointerCapture={cancel} onKeyDown={e=>keyboard(e,id)} onContextMenu={e=>e.preventDefault()}><DotsSixVertical size={18}/></button>
  {children(id,index)}
 </li>)}</ol><span className="custom-sr-only" role="status" aria-live="polite">{message}</span></>;
}
