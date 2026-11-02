/**
 * @file quote-calc.ts
 * 報價金額即時試算，公式與後端 QuoteCalculator 完全相同；存檔時一律以後端結果為準。
 */

export interface CalcItem {
  lineType: string;
  quantity?: number | null;
  unitPrice?: number | null;
  discountRate?: number | null;
  unitCost?: number | null;
}

export interface CalcHeader {
  currency: string;
  discountType: string;
  discountValue?: number | null;
  taxType: string;
  taxIncluded: boolean;
  taxRate?: number | null;
}

export interface QuoteTotals {
  subtotal: number;
  discountAmount: number;
  netAmount: number;
  taxAmount: number;
  total: number;
  costTotal: number | null;
  grossMargin: number | null;
}

export const digitsOf = (currency: string) => (currency === 'TWD' ? 0 : 2);

/** 四捨五入（0.5 遠離零），與 .NET MidpointRounding.AwayFromZero 相同；加上 EPSILON 抵銷浮點誤差。 */
export function roundAway(value: number, digits: number): number {
  const factor = 10 ** digits;
  return (Math.sign(value) * Math.round((Math.abs(value) + Number.EPSILON) * factor)) / factor;
}

export function lineAmount(item: CalcItem, digits: number): number {
  if (item.lineType !== 'item') return 0;
  return roundAway((item.quantity ?? 0) * (item.unitPrice ?? 0) * (1 - (item.discountRate ?? 0) / 100), digits);
}

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
