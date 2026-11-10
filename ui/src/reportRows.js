export const REPORT_PAGE_SIZE = 10;

// Latest observation per round/depth; never replace unknown latest values with older ones.
export function latestResourceRows(battles) {
  const latest = new Map();
  for (const battle of battles) latest.set(`${battle.round}:${battle.depth}`, battle);
  return [...latest.values()].sort((a, b) => b.round - a.round || b.depth - a.depth);
}

export function aggregateHeroes(battles) {
  const rows = new Map();
  for (const battle of battles) for (const hero of battle.heroes || []) {
    const row = rows.get(hero.name) || {name:hero.name,damage:0,taken:0,crit:0,skills:0,reactions:0,hp:0,hpKnown:0,count:0,duration:0,sources:{reaction:0,relic:0,skill:0,other:0},sourcesKnown:0,details:new Map()};
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
      const entry = row.details.get(key) || {...detail,damage:0,events:0};
      entry.damage += detail.damage || 0;
      entry.events = entry.events == null || detail.events == null ? null : entry.events + detail.events;
      // Prefer an observed display name over an ID-only label from an older bridge.
      if (detail.name && detail.name !== detail.id) entry.name = detail.name;
      row.details.set(key, entry);
    }
    rows.set(hero.name, row);
  }
  return [...rows.values()].map(row => ({...row,details:[...row.details.values()].sort((a,b)=>b.damage-a.damage)})).sort((a,b)=>b.damage-a.damage);
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
