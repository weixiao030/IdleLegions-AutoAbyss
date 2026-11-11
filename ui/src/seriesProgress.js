export function seriesStatus(series, activeRun = true) {
  if (!activeRun) return '尚未进入深渊，等待本轮遗物数据。';
  if (!Array.isArray(series) || series.length !== 7 || series.some(f => !Number.isFinite(f.count)))
    return '等待完整的七系遗物数据，暂不判断是否集齐。';
  const missing = series.filter(f => f.count < 4);
  return missing.length
    ? `本层还缺：${missing.map(f => `${f.name}${4-f.count}张`).join('、')}。`
    : '本层七系已齐，继续核对继承进度。';
}
