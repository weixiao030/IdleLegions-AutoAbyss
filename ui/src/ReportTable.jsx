import React, {useEffect, useState} from 'react';
import {Button} from '@radix-ui/themes';
import {CaretLeft, CaretRight} from '@phosphor-icons/react';
import {reportPage, REPORT_PAGE_SIZE} from './reportRows.js';

export default function ReportTable({scopeKey, ...props}) {
  // New filters reset to page one; polling in the same scope keeps the page.
  return <TablePage key={scopeKey} {...props}/>;
}

function TablePage({label, headings, rows, renderRow, className = '', numericColumns = []}) {
  const [page, setPage] = useState(1);
  const {pageCount, currentPage, start, end, rows: visibleRows} = reportPage(rows, page);
  useEffect(() => {setPage(p => Math.min(p, pageCount));}, [pageCount]);
  return <>
    <div className="table-scroll">
      <table aria-label={label} className={className}>
        <colgroup>{headings.map((heading, index) => <col key={heading} className={numericColumns.includes(index) ? 'report-numeric-column' : undefined}/>)}</colgroup>
        <thead><tr>{headings.map((heading, index) => <th key={heading} scope="col" className={numericColumns.includes(index) ? 'report-numeric-heading' : undefined}>{heading}</th>)}</tr></thead>
        <tbody>{visibleRows.length ? visibleRows.map((row, index) => renderRow(row, start + index)) :
          <tr><td colSpan={headings.length}>所选范围没有明细。</td></tr>}</tbody>
      </table>
    </div>
    {rows.length > 0 && <nav className="report-pagination" aria-label={`${label}分页`}>
      <div className="report-page-summary" aria-live="polite" aria-atomic="true">
        <span>第 {currentPage} / {pageCount} 页</span>
        <span>第 {start + 1} 至 {end} 条，共 {rows.length} 条 · 每页 {REPORT_PAGE_SIZE} 条</span>
      </div>
      <div className="report-page-buttons">
        <Button type="button" variant="outline" color="gray" size="2" aria-label={`${label}上一页`} disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>
          <CaretLeft size={15} aria-hidden="true"/>上一页
        </Button>
        <Button type="button" variant="outline" color="gray" size="2" aria-label={`${label}下一页`} disabled={currentPage === pageCount} onClick={() => setPage(currentPage + 1)}>
          下一页<CaretRight size={15} aria-hidden="true"/>
        </Button>
      </div>
    </nav>}
  </>;
}
