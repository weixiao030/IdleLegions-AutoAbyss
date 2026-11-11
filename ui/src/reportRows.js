import {gameName, gameText} from './gameText.js';
export const REPORT_PAGE_SIZE = 10;

// Resource totals are computed from server settlement receipts by the backend.
// Depth filters must never turn a completed round into a partial-round total.
export function roundResourceRows(rounds, round = 'all') {
  return rounds.filter(row => round === 'all' || String(row.round) === String(round))
    .slice().sort((a, b) => b.round - a.round);
}

export function aggregateHeroes(battles) {
  const rows = new Map();
  for (const battle of battles) for (const hero of battle.heroes || []) {
    const row = rows.get(hero.name) || {name:gameText(hero.name),defId:hero.defId,damage:0,taken:0,crit:0,skills:0,reactions:0,hp:0,hpKnown:0,count:0,duration:0,sources:{reaction:0,relic:0,skill:0,other:0},sourcesKnown:0,details:new Map(),skillActivity:new Map(),largestHits:[],loadouts:new Map(),loadoutMissing:0};
    if (hero.loadout) {
      const key=JSON.stringify(hero.loadout),existing=row.loadouts.get(key);
      if(existing) existing.battles.push({round:battle.round,depth:battle.depth,node:battle.node});
      else row.loadouts.set(key,{...hero.loadout,battles:[{round:battle.round,depth:battle.depth,node:battle.node}]});
    } else row.loadoutMissing++;
    row.damage += hero.damage || 0; row.taken += hero.damageTaken || 0;
    row.crit += hero.criticalEvents || 0; row.skills += hero.skillEvents || 0;
    row.reactions += hero.reactionEvents || 0; row.duration += battle.durationSec || 0; row.count++;
    if (hero.hpLoss != null) { row.hp += hero.hpLoss; row.hpKnown++; }
    if (hero.damageSources) {
      for (const key of Object.keys(row.sources)) row.sources[key] += hero.damageSources[key] || 0;
      row.sourcesKnown++;
    }
    const details = Array.isArray(hero.damageDetails) ? hero.damageDetails :
      hero.damage > 0 ? [{kind:'unrecorded',id:'legacy',name:'旧记录未细分',damage:hero.damage,events:null}] : [];
    for (const detail of details) {
      const key = `${detail.kind}:${detail.id}`;
      const existed = row.details.get(key);
      const entry = existed || {...detail,name:gameName(detail.kind,detail.id,detail.name),damage:0,events:0,descriptions:[]};
      if (existed) {
        for (const field of ['criticalEvents','criticalDamage','periodicEvents','periodicDamage','channelEvents','channelDamage','kills']) entry[field] = entry[field] == null || detail[field] == null ? null : entry[field] + detail[field];
        for (const field of ['minHit','firstHit']) entry[field] = entry[field] == null || detail[field] == null ? null : Math.min(entry[field],detail[field]);
        for (const field of ['maxHit','lastHit']) entry[field] = entry[field] == null || detail[field] == null ? null : Math.max(entry[field],detail[field]);
        for (const field of ['targets','elements','tags','timings','states']) entry[field] = mergeBreakdown(entry[field],detail[field]);
      }
      const basis=detail.descriptionBasis||'catalog',description=detail.description||'';
      const desc=entry.descriptions.find(x=>x.basis===basis&&x.text===description);
      if(desc) desc.count++; else entry.descriptions.push({basis,text:description,count:1});
      entry.damage += detail.damage || 0;
      entry.events = entry.events == null || detail.events == null ? null : entry.events + detail.events;
      // Prefer an observed display name over an ID-only label from an older bridge.
      if (detail.name && detail.name !== detail.id) entry.name = gameName(detail.kind,detail.id,detail.name);
      row.details.set(key, entry);
    }
    for (const skill of hero.skillActivity || []) {
      const entry = row.skillActivity.get(skill.id);
      if (entry) for (const field of ['castMarkers','damage','events','healing','reactionDamage','relicDamage']) entry[field] += skill[field] || 0;
      else row.skillActivity.set(skill.id,{...skill,name:gameName('skill',skill.id,skill.name)});
    }
    for (const hit of hero.largestHits || []) row.largestHits.push({...hit,round:battle.round,depth:battle.depth,node:battle.node});
    rows.set(hero.name, row);
  }
  return [...rows.values()].map(row => ({...row,loadouts:[...row.loadouts.values()],details:[...row.details.values()].sort((a,b)=>b.damage-a.damage),skillActivity:[...row.skillActivity.values()].sort((a,b)=>b.damage-a.damage),largestHits:row.largestHits.sort((a,b)=>b.damage-a.damage).slice(0,20)})).sort((a,b)=>b.damage-a.damage);
}

function mergeBreakdown(left, right) {
  if (!Array.isArray(left) || !Array.isArray(right)) return null;
  const rows = new Map(left.map(item => [`${item.kind || ''}:${item.id}`,{...item}]));
  for (const item of right) {
    const key = `${item.kind || ''}:${item.id}`, entry = rows.get(key);
    if (!entry) rows.set(key,{...item});
    else for (const field of ['damage','events','criticalEvents','kills']) if (field in item) entry[field] = (entry[field] || 0) + item[field];
  }
  return [...rows.values()].sort((a,b)=>b.damage-a.damage);
}

// Paginate display rows only; summaries and exports retain the complete data.
export function reportPage(rows, page) {
  const pageCount = Math.max(1, Math.ceil(rows.length / REPORT_PAGE_SIZE));
  const currentPage = Math.max(1, Math.min(page, pageCount));
  const start = (currentPage - 1) * REPORT_PAGE_SIZE;
  const end = Math.min(start + REPORT_PAGE_SIZE, rows.length);
  return {pageCount, currentPage, start, end, rows: rows.slice(start, end)};
}

// Show confirmed mythic equipment only; raw evidence and exports stay intact.
// Positive mailbox counts are explicitly mythic even without item names.
export function equipmentRows(drops) {
  return drops.flatMap((drop, index) => {
    const evidence = drop.evidence || {};
    const items = (evidence.items || []).filter(item => item.rarity === 'mythic').map((item, itemIndex) => ({
      key: `${index}-${itemIndex}`, drop, item
    }));
    if (evidence.mailedMythics > 0 || evidence.pendingMythics > 0) {
      items.push({key: `${index}-pending`, drop, pending: true});
    }
    return items;
  });
}
