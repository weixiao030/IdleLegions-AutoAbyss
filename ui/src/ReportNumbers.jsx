import React from 'react';
import {Progress} from '@radix-ui/themes';
import {exactReportNumber, formatReportNumber, reportShare} from './reportNumbers.js';

export function ReportNumber({value, suffix = ''}) {
  const known = typeof value === 'number' && Number.isFinite(value);
  return <span className="report-number" title={known ? exactReportNumber(value) + suffix : undefined}>
    {formatReportNumber(value)}{known ? suffix : ''}
  </span>;
}

export function ReportShare({value, total, label}) {
  const share = reportShare(value, total);
  if (share == null) return <span className="report-number">未确认</span>;
  return <span className="report-share" title={`${share.toFixed(1)}%`}>
    <Progress className="report-share-track" value={share} size="1" radius="full" variant="soft" duration="0ms"
      aria-label={label} getValueLabel={n => `${n.toFixed(1)}%`}/>
    <span className="report-share-value">{Math.round(share)}%</span>
  </span>;
}
