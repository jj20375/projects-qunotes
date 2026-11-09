<script setup lang="ts">
/**
 * @component QuoteFitTotalModal
 * 議價用「調整至目標總價」：輸入客戶要的含稅總價（或未稅金額），由後端試算整單折讓或分攤到未鎖定品項，
 * 預覽調整前後的金額，確認後才寫回編輯中的報價（仍需按「儲存」）。
 */
import { computed, ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import DataTable from '@/app/components/DataTable.vue';
import { fitQuoteTotalApi, type QuoteFitBasis, type QuoteFitMode, type QuoteFitResult, type QuoteHeader, type QuoteItem } from '@/shared/services/quote-service';
import { dataColumns } from '@/shared/table/data-table';
import { formatMoney } from '@/shared/types/business';
import { calculate, digitsOf, lineAmount } from '@/shared/utils/quote-calc';

const props = defineProps<{ open: boolean; items: QuoteItem[]; header: QuoteHeader; showCost: boolean }>();
const emit = defineEmits<{
  close: [];
  apply: [value: { items: QuoteItem[]; discountType: QuoteHeader['discountType']; discountValue: number }];
}>();

const target = ref<number | null>(null);
const basis = ref<QuoteFitBasis>('total');
const mode = ref<QuoteFitMode>('discount');
const result = ref<QuoteFitResult | null>(null);
const error = ref('');
const loading = ref(false);

const taxable = computed(() => (props.header.taxType ?? 'taxable') === 'taxable' && (props.header.taxRate ?? 0) > 0);
const currency = computed(() => props.header.currency ?? 'TWD');
const before = computed(() =>
  calculate(props.items, {
    currency: currency.value,
    discountType: props.header.discountType ?? 'none',
    discountValue: props.header.discountValue,
    taxType: props.header.taxType ?? 'taxable',
    taxIncluded: !!props.header.taxIncluded,
    taxRate: props.header.taxRate,
  }),
);
const lockedCount = computed(() => props.items.filter((item) => item.lineType === 'item' && item.priceLocked).length);

watch(
  () => props.open,
  (open) => {
    if (!open) return;
    result.value = null;
    error.value = '';
    target.value = null;
    basis.value = taxable.value ? 'total' : 'net';
  },
);

let timer: ReturnType<typeof setTimeout> | undefined;
let requestNo = 0;
watch([target, basis, mode], () => {
  clearTimeout(timer);
  result.value = null;
  error.value = '';
  if (!target.value || target.value <= 0) return;
  timer = setTimeout(preview, 300);
});

async function preview() {
  const current = ++requestNo;
  loading.value = true;
  try {
    const value = await fitQuoteTotalApi({ ...props.header, items: props.items }, target.value!, basis.value, mode.value);
    if (current === requestNo) result.value = value;
  } catch (failure) {
    if (current === requestNo) error.value = (failure as Error).message;
  } finally {
    if (current === requestNo) loading.value = false;
  }
}

const basisLabel = computed(() => (basis.value === 'total' ? '含稅總計' : '未稅金額'));

type Row = { index: number; no: number; name: string; quantity: number; beforePrice: number; afterPrice: number; beforeAmount: number; afterAmount: number; locked: boolean; belowCost: boolean };
const rows = computed<Row[]>(() => {
  if (!result.value?.exact) return [];
  const digits = digitsOf(currency.value);
  const numbers = new Map<number, number>();
  props.items.forEach((item, index) => item.lineType === 'item' && numbers.set(index, numbers.size + 1));
  return result.value.items
    .filter((line) => line.changed || line.belowCost)
    .map((line) => {
      const item = props.items[line.index];
      return {
        index: line.index,
        no: numbers.get(line.index) ?? 0,
        name: item.name || '（未命名）',
        quantity: item.quantity ?? 0,
        beforePrice: item.unitPrice ?? 0,
        afterPrice: line.unitPrice,
        beforeAmount: lineAmount(item, digits),
        afterAmount: line.amount,
        locked: !!item.priceLocked,
        belowCost: line.belowCost,
      };
    });
});
const belowCostCount = computed(() => result.value?.items.filter((line) => line.belowCost).length ?? 0);

const col = dataColumns<Row>();
const columns = [
  col.accessor('no', { header: '#', meta: { width: 44, align: 'center' } }),
  col.accessor('name', { header: '品名' }),
  col.accessor('quantity', { header: '數量', meta: { width: 64, align: 'right' } }),
  col.accessor('afterPrice', { header: '單價（原 → 新）', meta: { width: 180, align: 'right' } }),
  col.accessor('afterAmount', { header: '金額（原 → 新）', meta: { width: 200, align: 'right' } }),
];

const discountNote = computed(() => {
  const value = result.value;
  if (!value?.exact) return '';
  const changed = value.discountType !== (props.header.discountType ?? 'none') || value.discountValue !== (props.header.discountValue ?? 0);
  if (!changed) return '';
  return value.discountType === 'none' ? '整單折扣會改為「無」。' : `整單折扣會改為折讓金額 ${formatMoney(value.discountValue)}。`;
});

function useTarget(value: number) {
  target.value = value;
}

function apply() {
  const value = result.value;
  if (!value?.exact) return;
  const prices = new Map(value.items.map((line) => [line.index, line.unitPrice]));
  const items = props.items.map((item, index) => (prices.has(index) ? { ...item, unitPrice: prices.get(index)! } : item));
  emit('apply', { items, discountType: value.discountType, discountValue: value.discountValue });
  message.success('已套用調整，記得按「儲存」');
}

const moneyFormatter = (value: number | string) => `${value}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
const moneyParser = (value: string) => value.replace(/,/g, '');
const margin = (gross: number | null | undefined, net: number) => (gross === null || gross === undefined || !net ? '' : `（${((gross / net) * 100).toFixed(1)}%）`);
</script>

<template>
  <a-modal :open="open" title="調整至目標總價" :width="760" ok-text="套用" :ok-button-props="{ disabled: !result?.exact }" @ok="apply" @cancel="emit('close')">
    <a-form layout="vertical">
      <a-row :gutter="16">
        <a-col :xs="24" :md="10">
          <a-form-item label="目標金額">
            <a-input-number v-model:value="target" :min="0" :precision="digitsOf(currency)" :formatter="moneyFormatter" :parser="moneyParser" placeholder="例如 1,520,000" style="width: 100%" autofocus />
          </a-form-item>
        </a-col>
        <a-col :xs="24" :md="14">
          <a-form-item label="目標是">
            <a-radio-group v-model:value="basis" :disabled="!taxable">
              <a-radio-button value="total">含稅總計</a-radio-button>
              <a-radio-button value="net">未稅金額</a-radio-button>
            </a-radio-group>
          </a-form-item>
        </a-col>
        <a-col :span="24">
          <a-form-item label="調整方式" :extra="mode === 'allocate' ? `鎖定的品項（${lockedCount} 列）不變，其餘品項依金額比例調整單價；可在明細右側用鎖頭切換。` : '品項單價都不動，差額放在整單折扣（折讓金額）。'">
            <a-radio-group v-model:value="mode">
              <a-radio-button value="discount">整單折讓</a-radio-button>
              <a-radio-button value="allocate">分攤到未鎖定品項</a-radio-button>
            </a-radio-group>
          </a-form-item>
        </a-col>
      </a-row>
    </a-form>

    <a-spin :spinning="loading">
      <a-alert v-if="error" type="error" show-icon :message="error" />
      <a-alert v-else-if="result && !result.exact" type="warning" show-icon>
        <template #message>因為稅額要四捨五入到{{ currency === 'TWD' ? '元' : '分' }}，{{ basisLabel }}剛好 {{ formatMoney(target) }} 湊不到。</template>
        <template #description>
          最接近的是
          <a-space :size="4">
            <a-button v-if="result.nearestBelow !== null" size="small" @click="useTarget(result.nearestBelow)">{{ formatMoney(result.nearestBelow) }}</a-button>
            <span v-if="result.nearestBelow !== null && result.nearestAbove !== null">或</span>
            <a-button v-if="result.nearestAbove !== null" size="small" @click="useTarget(result.nearestAbove)">{{ formatMoney(result.nearestAbove) }}</a-button>
          </a-space>
        </template>
      </a-alert>
      <template v-else-if="result?.exact && result.totals">
        <!-- 只有幾個合計數字的對照，不是資料表，用 grid 排版。 -->
        <div class="compare">
          <span /><span class="head">目前</span><span class="head">調整後</span>
          <span>小計</span><span>{{ formatMoney(before.subtotal) }}</span><span>{{ formatMoney(result.totals.subtotal) }}</span>
          <span>折扣</span><span>{{ before.discountAmount ? `−${formatMoney(before.discountAmount)}` : '—' }}</span><span>{{ result.totals.discountAmount ? `−${formatMoney(result.totals.discountAmount)}` : '—' }}</span>
          <span>未稅金額</span><span>{{ formatMoney(before.netAmount) }}</span><span :class="{ hit: basis === 'net' }">{{ formatMoney(result.totals.netAmount) }}</span>
          <span>稅額</span><span>{{ formatMoney(before.taxAmount) }}</span><span>{{ formatMoney(result.totals.taxAmount) }}</span>
          <span>總計</span><span>{{ formatMoney(before.total) }}</span><span :class="{ hit: basis === 'total' }">{{ formatMoney(result.totals.total) }}</span>
          <template v-if="showCost && result.totals.costTotal !== null">
            <span>毛利</span>
            <span class="muted">{{ formatMoney(before.grossMargin) }}{{ margin(before.grossMargin, before.netAmount) }}</span>
            <span class="muted">{{ formatMoney(result.totals.grossMargin) }}{{ margin(result.totals.grossMargin, result.totals.netAmount) }}</span>
          </template>
        </div>
        <a-alert v-if="discountNote" type="info" show-icon :message="discountNote" class="gap" />
        <a-alert v-if="belowCostCount" type="warning" show-icon :message="`有 ${belowCostCount} 個品項調整後的單價低於成本。`" class="gap" />
        <template v-if="mode === 'allocate'">
          <div class="caption">變動的品項（{{ rows.length }} 列）</div>
          <!-- 試算結果是這份報價自己的明細（不是列表 API），直接顯示。 -->
          <DataTable :data="rows" :columns="columns" row-key="index" size="small" :scroll-y="260" empty-text="沒有品項需要變動">
            <template #cell="{ columnId, record }">
              <template v-if="columnId === 'name'">
                {{ record.name }}
                <a-tag v-if="record.belowCost" color="red">低於成本</a-tag>
              </template>
              <template v-else-if="columnId === 'afterPrice'">
                <span class="muted">{{ formatMoney(record.beforePrice) }} →</span> {{ formatMoney(record.afterPrice) }}
              </template>
              <template v-else-if="columnId === 'afterAmount'">
                <span class="muted">{{ formatMoney(record.beforeAmount) }} →</span> {{ formatMoney(record.afterAmount) }}
              </template>
            </template>
          </DataTable>
        </template>
      </template>
      <div v-else-if="!target" class="hint">
        目前未稅 {{ formatMoney(before.netAmount) }}、稅額 {{ formatMoney(before.taxAmount) }}、總計 {{ formatMoney(before.total) }}。輸入目標金額後會自動試算。
      </div>
    </a-spin>
  </a-modal>
</template>

<style scoped>
.compare {
  display: grid;
  grid-template-columns: auto 1fr 1fr;
  gap: 6px 24px;
  font-variant-numeric: tabular-nums;
}
.compare > span {
  text-align: right;
}
.compare > span:nth-child(3n + 1) {
  text-align: left;
  color: #555;
}
.compare .head {
  color: #888;
  font-weight: 500;
}
.compare .hit {
  color: #1f4e79;
  font-weight: 600;
}
.muted {
  color: #999;
}
.gap {
  margin-top: 12px;
}
.caption {
  margin: 16px 0 8px;
  font-weight: 500;
}
.hint {
  color: #888;
}
</style>
