<script setup lang="ts">
/**
 * @component QuoteItemsTable
 * 報價明細編輯：品項／分段標題／備註列、品名自動完成（品項價目）、即時金額、上下移動。
 * 金額只是即時試算，存檔後以後端重算結果為準。
 */
import { ref } from 'vue';
import type { ProductRow, QuoteItem } from '@/shared/services/quote-service';
import { searchProductsApi } from '@/shared/services/quote-service';
import { digitsOf, lineAmount } from '@/shared/utils/quote-calc';
import { formatMoney } from '@/shared/types/business';

const props = defineProps<{ modelValue: QuoteItem[]; readonly: boolean; showCost: boolean; canSearchProducts: boolean; currency: string }>();
const emit = defineEmits<{ 'update:modelValue': [value: QuoteItem[]] }>();

const lineTypes = [
  { value: 'item', label: '品項' },
  { value: 'section', label: '分段' },
  { value: 'note', label: '備註' },
];

const productOptions = ref<{ value: string; label: string; product: ProductRow }[]>([]);
let timer: ReturnType<typeof setTimeout> | undefined;

function searchProducts(keyword: string) {
  if (!props.canSearchProducts) return;
  clearTimeout(timer);
  timer = setTimeout(async () => {
    if (!keyword.trim()) {
      productOptions.value = [];
      return;
    }
    const result = await searchProductsApi({ keyword, activeOnly: true, pageSize: 15 });
    productOptions.value = result.items.map((product) => ({
      value: product.id,
      label: `${product.code}　${product.name}${product.spec ? `（${product.spec}）` : ''}　$${formatMoney(product.defaultPrice)}`,
      product,
    }));
  }, 250);
}

function update(index: number, patch: Partial<QuoteItem>) {
  const next = props.modelValue.map((item, i) => (i === index ? { ...item, ...patch } : item));
  emit('update:modelValue', next);
}

/** 選了價目表品項：帶入品號、品名、規格、單位、單價（與成本），數量預設 1。 */
function pickProduct(index: number, productId: unknown) {
  const option = productOptions.value.find((value) => value.value === productId);
  if (!option) return;
  const { product } = option;
  update(index, {
    productId: product.id,
    itemCode: product.code,
    name: product.name,
    spec: product.spec,
    unit: product.unit,
    unitPrice: product.defaultPrice,
    unitCost: product.defaultCost ?? props.modelValue[index].unitCost,
    quantity: props.modelValue[index].quantity ?? 1,
  });
}

function add(lineType: QuoteItem['lineType']) {
  emit('update:modelValue', [...props.modelValue, lineType === 'item' ? { lineType, name: '', quantity: 1, unitPrice: 0, discountRate: 0 } : { lineType, name: '' }]);
}

function remove(index: number) {
  emit('update:modelValue', props.modelValue.filter((_, i) => i !== index));
}

function move(index: number, offset: number) {
  const target = index + offset;
  if (target < 0 || target >= props.modelValue.length) return;
  const next = [...props.modelValue];
  [next[index], next[target]] = [next[target], next[index]];
  emit('update:modelValue', next);
}

const itemNumber = (index: number) => props.modelValue.slice(0, index + 1).filter((item) => item.lineType === 'item').length;
</script>

<template>
  <div class="wrap">
    <table class="items">
      <thead>
        <tr>
          <th class="no">#</th>
          <th v-if="!readonly" class="type">類型</th>
          <th class="code">品號</th>
          <th class="name">品名</th>
          <th class="spec">規格</th>
          <th class="unit">單位</th>
          <th class="num">數量</th>
          <th class="num">單價</th>
          <th class="pct">折扣%</th>
          <th v-if="showCost" class="num">成本</th>
          <th class="amount">金額</th>
          <th class="remark">備註</th>
          <th v-if="!readonly" class="ops" />
        </tr>
      </thead>
      <tbody>
        <tr v-for="(item, index) in modelValue" :key="item.id ?? index" :class="item.lineType">
          <td class="no">{{ item.lineType === 'item' ? itemNumber(index) : '' }}</td>
          <td v-if="!readonly" class="type">
            <a-select :value="item.lineType" :options="lineTypes" size="small" @change="(value: unknown) => update(index, { lineType: value as QuoteItem['lineType'] })" />
          </td>
          <template v-if="item.lineType === 'item'">
            <td class="code"><a-input :value="item.itemCode" size="small" :readonly="readonly" @change="(e: Event) => update(index, { itemCode: (e.target as HTMLInputElement).value })" /></td>
            <td class="name">
              <a-auto-complete
                :value="item.name"
                :options="productOptions"
                size="small"
                :disabled="readonly"
                placeholder="輸入品名，或搜尋品項價目"
                style="width: 100%"
                @search="searchProducts"
                @select="(value: unknown) => pickProduct(index, value)"
                @change="(value: unknown) => typeof value === 'string' && !productOptions.some((option) => option.value === value) && update(index, { name: value, productId: null })"
              />
            </td>
            <td class="spec"><a-input :value="item.spec" size="small" :readonly="readonly" @change="(e: Event) => update(index, { spec: (e.target as HTMLInputElement).value })" /></td>
            <td class="unit"><a-input :value="item.unit" size="small" :readonly="readonly" @change="(e: Event) => update(index, { unit: (e.target as HTMLInputElement).value })" /></td>
            <td class="num"><a-input-number :value="item.quantity" size="small" :min="0" :readonly="readonly" @change="(value: unknown) => update(index, { quantity: value as number })" /></td>
            <td class="num"><a-input-number :value="item.unitPrice" size="small" :min="0" :readonly="readonly" @change="(value: unknown) => update(index, { unitPrice: value as number })" /></td>
            <td class="pct"><a-input-number :value="item.discountRate" size="small" :min="0" :max="100" :readonly="readonly" @change="(value: unknown) => update(index, { discountRate: value as number })" /></td>
            <td v-if="showCost" class="num"><a-input-number :value="item.unitCost" size="small" :min="0" :readonly="readonly" @change="(value: unknown) => update(index, { unitCost: value as number | null })" /></td>
            <td class="amount">{{ formatMoney(lineAmount(item, digitsOf(currency))) }}</td>
            <td class="remark"><a-input :value="item.remark" size="small" :readonly="readonly" @change="(e: Event) => update(index, { remark: (e.target as HTMLInputElement).value })" /></td>
          </template>
          <template v-else>
            <td :colspan="showCost ? 10 : 9">
              <a-input
                :value="item.name"
                size="small"
                :readonly="readonly"
                :placeholder="item.lineType === 'section' ? '分段標題，例如：一、設備' : '備註文字'"
                @change="(e: Event) => update(index, { name: (e.target as HTMLInputElement).value })"
              />
            </td>
          </template>
          <td v-if="!readonly" class="ops">
            <a-button size="small" type="text" :disabled="index === 0" aria-label="上移" @click="move(index, -1)">↑</a-button>
            <a-button size="small" type="text" :disabled="index === modelValue.length - 1" aria-label="下移" @click="move(index, 1)">↓</a-button>
            <a-button size="small" type="text" danger aria-label="刪除" @click="remove(index)">✕</a-button>
          </td>
        </tr>
        <tr v-if="!modelValue.length">
          <td :colspan="13" class="empty">尚無明細</td>
        </tr>
      </tbody>
    </table>
  </div>
  <a-space v-if="!readonly" class="add">
    <a-button @click="add('item')">新增品項</a-button>
    <a-button @click="add('section')">新增分段標題</a-button>
    <a-button @click="add('note')">新增備註列</a-button>
  </a-space>
</template>

<style scoped>
.wrap {
  overflow-x: auto;
}
.items {
  width: 100%;
  min-width: 1080px;
  border-collapse: collapse;
  font-size: 13px;
}
.items th,
.items td {
  border-bottom: 1px solid #f0f0f0;
  padding: 4px;
  text-align: left;
}
.items th {
  background: #fafafa;
  font-weight: 500;
  white-space: nowrap;
}
.items tr.section td {
  background: #f7f9fc;
  font-weight: 600;
}
.items tr.note td {
  background: #fffbe6;
}
.no {
  width: 32px;
  text-align: center !important;
  color: #888;
}
.type {
  width: 78px;
}
.code {
  width: 96px;
}
.name {
  min-width: 200px;
}
.spec {
  width: 150px;
}
.unit {
  width: 60px;
}
.num {
  width: 104px;
}
.num :deep(.ant-input-number),
.pct :deep(.ant-input-number) {
  width: 100%;
}
.pct {
  width: 72px;
}
.amount {
  width: 100px;
  text-align: right !important;
  font-variant-numeric: tabular-nums;
}
.remark {
  width: 140px;
}
.ops {
  width: 96px;
  white-space: nowrap;
}
.empty {
  text-align: center !important;
  color: #aaa;
  padding: 24px !important;
}
.add {
  margin-top: 12px;
}
</style>
