const valid = value => typeof value === 'number' && Number.isFinite(value);
const decimal = (value, digits) => value.toLocaleString('zh-CN', {maximumFractionDigits: digits, useGrouping: false});

// Display only. Calculations, pagination, raw records and exports keep full values.
export function formatReportNumber(value) {
  if (!valid(value)) return '未确认';
  const rounded = Math.round(value);
  const magnitude = Math.abs(rounded);
  if (magnitude >= 100000000) return decimal(rounded / 100000000, 2) + '亿';
  if (magnitude >= 10000) {
    const wan = Math.round(rounded / 1000) / 10;
    return Math.abs(wan) >= 10000 ? decimal(rounded / 100000000, 2) + '亿' : decimal(wan, 1) + '万';
  }
  return rounded.toLocaleString('zh-CN');
}

export function exactReportNumber(value) {
  return valid(value) ? value.toLocaleString('zh-CN', {maximumFractionDigits: 20}) : '未确认';
}

export function reportShare(value, total) {
  if (!valid(value) || !valid(total) || value < 0 || total <= 0) return null;
  return Math.min(100, Math.max(0, 100 * value / total));
}
