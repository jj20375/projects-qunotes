/**
 * @file quote-calc.ts
 * 報價金額即時試算，公式與後端 QuoteCalculator 完全相同；存檔時一律以後端結果為準。
 */

/**
 * 試算明細項目介面
 */
export interface CalcItem {
  /** 明細列類型：item（品項）、section（分組）、note（備註） */
  lineType: string;
  /** 數量 */
  quantity?: number | null;
  /** 單價 */
  unitPrice?: number | null;
  /** 折扣率（百分比，如 5 代表 5%） */
  discountRate?: number | null;
  /** 單位成本 */
  unitCost?: number | null;
}

/**
 * 試算表頭設定介面
 */
export interface CalcHeader {
  /** 幣別代碼（如 TWD, USD） */
  currency: string;
  /** 整體折扣類型：none（無）、percent（比例）、amount（固定折讓） */
  discountType: string;
  /** 折扣數值 */
  discountValue?: number | null;
  /** 稅別：taxable（應稅）、zero（零稅率）、exempt（免稅） */
  taxType: string;
  /** 報價單價是否為含稅價 */
  taxIncluded: boolean;
  /** 營業稅率百分比（預設 5%） */
  taxRate?: number | null;
}

/**
 * 試算合計結果
 */
export interface QuoteTotals {
  /** 明細小計（尚未扣除整筆折扣） */
  subtotal: number;
  /** 整筆折扣金額 */
  discountAmount: number;
  /** 折扣後未稅金額 */
  netAmount: number;
  /** 營業稅額 */
  taxAmount: number;
  /** 報價總計金額（含稅） */
  total: number;
  /** 總成本（若無成本權限或無填成本則為 null） */
  costTotal: number | null;
  /** 毛利額（netAmount - costTotal） */
  grossMargin: number | null;
}

/**
 * 取得指定幣別之小數位數（台幣為 0，外幣為 2）
 *
 * @param currency 幣別字串（如 'TWD', 'USD'）
 * @returns 小數位數整數
 */
export const digitsOf = (currency: string) => (currency === 'TWD' ? 0 : 2);

/**
 * 四捨五入（0.5 遠離零）
 *
 * 與 .NET 的 MidpointRounding.AwayFromZero 運算法則保持完全一致；
 * 加上 Number.EPSILON 抵銷 JavaScript 二進位浮點數誤差。
 *
 * @param value 欲進行四捨五入之數值
 * @param digits 保留之小數位數
 * @returns 四捨五入後之結果數值
 */
export function roundAway(value: number, digits: number): number {
  const factor = 10 ** digits;
  return (Math.sign(value) * Math.round((Math.abs(value) + Number.EPSILON) * factor)) / factor;
}

/**
 * 計算單一明細列之折後金額
 *
 * 公式：數量 × 單價 × (1 − 折扣率 / 100)
 *
 * @param item 報價品項明細資料
 * @param digits 幣別小數位數
 * @returns 單列折後金額數值
 */
export function lineAmount(item: CalcItem, digits: number): number {
  if (item.lineType !== 'item') return 0;
  return roundAway((item.quantity ?? 0) * (item.unitPrice ?? 0) * (1 - (item.discountRate ?? 0) / 100), digits);
}

/**
 * 依據品項列表與表頭稅率設定即時試算完整報價金額結構
 *
 * 演算法完全對齊後端 QuoteCalculator 核心服務。
 *
 * @param items 報價品項明細清單
 * @param header 報價表頭條件（幣別、折扣與稅額設定）
 * @returns 傳回完整試算合計金額物件
 */
export function calculate(items: CalcItem[], header: CalcHeader): QuoteTotals {
  const digits = digitsOf(header.currency);
  const priced = items.filter((item) => item.lineType === 'item');
  const subtotal = priced.reduce((sum, item) => sum + lineAmount(item, digits), 0);
  const discountValue = header.discountValue ?? 0;
  const discountAmount =
    header.discountType === 'percent' ? roundAway((subtotal * discountValue) / 100, digits) : header.discountType === 'amount' ? Math.min(roundAway(discountValue, digits), subtotal) : 0;
  const after = subtotal - discountAmount;
  const rate = header.taxRate ?? 0;

  let netAmount = after;
  let taxAmount = 0;
  let total = after;
  if (header.taxType === 'taxable' && rate !== 0) {
    if (header.taxIncluded) {
      netAmount = roundAway(after / (1 + rate / 100), digits);
      taxAmount = after - netAmount;
    } else {
      taxAmount = roundAway((after * rate) / 100, digits);
      total = after + taxAmount;
    }
  }

  const hasCost = priced.some((item) => item.unitCost !== null && item.unitCost !== undefined);
  const costTotal = hasCost ? roundAway(priced.reduce((sum, item) => sum + (item.quantity ?? 0) * (item.unitCost ?? 0), 0), digits) : null;
  return { subtotal, discountAmount, netAmount, taxAmount, total, costTotal, grossMargin: costTotal === null ? null : netAmount - costTotal };
}
