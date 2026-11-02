/**
 * @file chart-theme.ts
 * Dashboard 圖表的共用樣式與數字格式。
 *
 * 顏色：今年＝系列 1（藍）、去年＝系列 2（橙），已用 dataviz 驗證腳本在白底檢查過
 * （色弱 ΔE 24.7、一般視覺 ΔE 33.6、對比 ≥ 3:1）。單一數列的長條一律用系列 1。
 * 文字一律用文字色，不用系列色；格線與座標軸淡化；只用一個 y 軸。
 */

export const SERIES = ['#2a78d6', '#eb6834'] as const;
export const TEXT_PRIMARY = '#1f1f1f';
export const TEXT_SECONDARY = '#595959';
export const GRID_LINE = '#f0f0f0';
export const AXIS_LINE = '#d9d9d9';

const full = new Intl.NumberFormat('zh-TW', { maximumFractionDigits: 0 });

/** 座標軸用的簡寫：萬、億。 */
export function compactMoney(value: number): string {
  const abs = Math.abs(value);
  if (abs >= 1e8) return `${+(value / 1e8).toFixed(1)}億`;
  if (abs >= 1e4) return `${+(value / 1e4).toFixed(1)}萬`;
  return full.format(value);
}

export function fullMoney(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : full.format(value);
}

/** 共用的座標軸、格線、提示框與圖例設定。 */
export const baseOption = {
  textStyle: { fontFamily: "-apple-system, 'PingFang TC', 'Noto Sans TC', 'Microsoft JhengHei', sans-serif", color: TEXT_SECONDARY },
  grid: { left: 8, right: 16, top: 36, bottom: 8, containLabel: true },
  legend: { top: 0, left: 0, icon: 'roundRect', itemWidth: 12, itemHeight: 4, textStyle: { color: TEXT_PRIMARY } },
  tooltip: {
    backgroundColor: '#fff',
    borderColor: AXIS_LINE,
    textStyle: { color: TEXT_PRIMARY },
    extraCssText: 'box-shadow: 0 4px 12px rgba(0,0,0,.08); border-radius: 6px;',
  },
};

export const valueAxis = {
  type: 'value' as const,
  axisLabel: { color: TEXT_SECONDARY, formatter: compactMoney },
  splitLine: { lineStyle: { color: GRID_LINE } },
  axisLine: { show: false },
  axisTick: { show: false },
};

export const categoryAxis = (data: string[]) => ({
  type: 'category' as const,
  data,
  axisLabel: { color: TEXT_SECONDARY },
  axisLine: { lineStyle: { color: AXIS_LINE } },
  axisTick: { show: false },
});
