export const REPORT_PAGE_SIZE = 10;

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
