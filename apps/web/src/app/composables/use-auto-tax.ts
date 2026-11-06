/**
 * @file use-auto-tax.ts
 * 單據表單（訂單、發票）的稅額自動計算：未稅金額變動時依稅率重算稅額，使用者手動改過稅額後就不再覆蓋。
 * 只是輸入輔助；實際存檔的金額仍以後端檢查與計算為準。
 */
import { ref, watch } from 'vue';

/** 沒有來源單據可推算稅率時使用的營業稅率。 */
export const DEFAULT_TAX_RATE = 0.05;

/**
 * 讓表單的稅額跟著未稅金額重算。
 * @param amount 讀取目前的未稅金額
 * @param setTax 寫回計算後的稅額
 * @returns `reset` 開啟表單並填好初始值後呼叫；`useRateOf` 選到來源單據時以它的稅額／未稅推算稅率；`markEdited` 使用者手動改稅額時呼叫
 */
export function useAutoTax(amount: () => number | null | undefined, setTax: (value: number) => void) {
  const rate = ref(DEFAULT_TAX_RATE);
  const edited = ref(false);
  // 開表單時填入既有金額也會觸發 watch；記下當時的金額，沒有變動就不重算，避免改掉原本的稅額。
  let baseline: number | null | undefined;

  watch(amount, (value) => {
    if (edited.value || value == null || value === baseline) return;
    baseline = value;
    setTax(Math.round(value * rate.value));
  });

  return {
    reset(initialAmount?: number | null, initialTax?: number | null) {
      edited.value = false;
      baseline = initialAmount;
      rate.value = initialAmount && initialTax != null ? initialTax / initialAmount : DEFAULT_TAX_RATE;
    },
    useRateOf(sourceAmount: number, sourceTax: number) {
      if (sourceAmount > 0) rate.value = sourceTax / sourceAmount;
      const value = amount();
      if (!edited.value && value != null) setTax(Math.round(value * rate.value));
    },
    markEdited() {
      edited.value = true;
    },
  };
}
