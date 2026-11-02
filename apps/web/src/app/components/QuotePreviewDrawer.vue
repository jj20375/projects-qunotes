<script setup lang="ts">
/**
 * @component QuotePreviewDrawer
 * 報價單唯讀預覽（側邊抽屜）：表頭、明細、金額與條款。用在建立訂單選定案報價時，不離開表單就能確認內容。
 * 需要完整操作（改版、送出等）時用「開啟報價單」到報價編輯頁（新分頁）。
 */
import { computed } from 'vue';
import { useQuery } from '@tanstack/vue-query';
import { message } from 'ant-design-vue';
import DataTable from '@/app/components/DataTable.vue';
import TermsEditor from '@/app/components/TermsEditor.vue';
import { dataColumns } from '@/shared/table/data-table';
import { getQuoteApi, type QuoteItem } from '@/shared/services/quote-service';
import { formatMoney, quoteNumber, quoteStatusColor, quoteStatusLabel } from '@/shared/types/business';
import { downloadFile } from '@/shared/utils/download';
import { useMe } from '@/app/composables/use-me';

const props = defineProps<{ open: boolean; quoteId?: string | null }>();
const emit = defineEmits<{ close: [] }>();
const { canOption } = useMe();

const quote = useQuery({
  queryKey: computed(() => ['quote', props.quoteId]),
  queryFn: () => getQuoteApi(props.quoteId!),
  enabled: computed(() => props.open && !!props.quoteId),
});
const q = computed(() => quote.data.value);

const taxLabel = computed(() => {
  if (!q.value) return '';
  if (q.value.taxType === 'zero') return '零稅率';
  if (q.value.taxType === 'exempt') return '免稅';
  return `${q.value.taxIncluded ? '內含' : '外加'} ${q.value.taxRate ?? 0}%`;
});

type Row = QuoteItem & { rowKey: string; no: number | null };
// 明細已隨報價一次載入（不是列表 API），直接顯示；分段標題列不編號。
const rows = computed<Row[]>(() => {
  let no = 0;
  return (q.value?.items ?? []).map((item, index) => ({ ...item, rowKey: item.id ?? String(index), no: item.lineType === 'item' ? ++no : null }));
});
const col = dataColumns<Row>();
const columns = [
  col.accessor('no', { header: '項次', meta: { width: 56, align: 'center' } }),
  col.accessor('name', { header: '品名' }),
  col.accessor('spec', { header: '規格', meta: { width: 140 } }),
  col.accessor('unit', { header: '單位', meta: { width: 60 } }),
  col.accessor('quantity', { header: '數量', meta: { width: 70, align: 'right' } }),
  col.accessor('unitPrice', { header: '單價', meta: { width: 100, align: 'right' } }),
  col.accessor('amount', { header: '金額', meta: { width: 110, align: 'right' } }),
];

async function downloadPdf() {
  try {
    await downloadFile(`/api/quotes/${props.quoteId}/pdf`, `報價單_${q.value ? quoteNumber(q.value) : ''}.pdf`);
  } catch (error) {
    message.error((error as Error).message);
  }
}
</script>

<template>
  <a-drawer :open="open" :width="760" title="報價單內容" @close="emit('close')">
    <a-spin :spinning="quote.isFetching.value">
      <template v-if="q">
        <div class="head">
          <div>
            <div class="no">
              {{ quoteNumber(q) }}
              <a-tag :color="quoteStatusColor(q.status)">{{ quoteStatusLabel(q.status) }}</a-tag>
            </div>
            <div class="muted">{{ q.project.projectNo }} {{ q.project.name }}</div>
          </div>
          <a-space>
            <a-button v-if="canOption('quotes', 'export_pdf')" @click="downloadPdf">下載 PDF</a-button>
            <a-button :href="`/quotes/edit?id=${q.id}`" target="_blank">開啟報價單</a-button>
          </a-space>
        </div>

        <a-descriptions :column="{ xs: 1, sm: 2 }" size="small" bordered class="gap">
          <a-descriptions-item label="案名" :span="2">{{ q.title || '—' }}</a-descriptions-item>
          <a-descriptions-item label="客戶">{{ q.clientName }}</a-descriptions-item>
          <a-descriptions-item label="統一編號">{{ q.clientTaxId || '—' }}</a-descriptions-item>
          <a-descriptions-item label="聯絡人">{{ q.contactName || '—' }}</a-descriptions-item>
          <a-descriptions-item label="報價日期">{{ q.quoteDate }}{{ q.validUntil ? ` ～ ${q.validUntil}` : '' }}</a-descriptions-item>
          <a-descriptions-item v-if="q.acceptedAt" label="客戶接受">{{ q.acceptedAt.slice(0, 10) }}</a-descriptions-item>
          <a-descriptions-item label="稅別">{{ taxLabel }}</a-descriptions-item>
        </a-descriptions>

        <DataTable :data="rows" :columns="columns" row-key="rowKey" size="small" :scroll-x="640" class="gap" empty-text="沒有明細">
          <template #cell="{ columnId, record }">
            <template v-if="columnId === 'name'">
              <strong v-if="record.lineType !== 'item'">{{ record.name }}</strong>
              <template v-else>{{ record.name }}</template>
            </template>
            <template v-else-if="columnId === 'unitPrice' || columnId === 'amount'">
              {{ record.lineType === 'item' ? formatMoney(record[columnId as 'amount']) : '' }}
            </template>
          </template>
        </DataTable>

        <div class="totals">
          <div><span>小計</span>{{ formatMoney(q.subtotal) }}</div>
          <div v-if="q.discountAmount"><span>折扣</span>-{{ formatMoney(q.discountAmount) }}</div>
          <div><span>未稅金額</span>{{ formatMoney(q.netAmount) }}</div>
          <div><span>稅額</span>{{ formatMoney(q.taxAmount) }}</div>
          <div class="total"><span>總計</span>{{ formatMoney(q.total) }}</div>
        </div>

        <a-descriptions :column="1" size="small" bordered class="gap">
          <a-descriptions-item label="付款條件">{{ q.paymentTerms || '—' }}</a-descriptions-item>
          <a-descriptions-item label="交貨條件">{{ q.deliveryTerms || '—' }}</a-descriptions-item>
          <a-descriptions-item label="備註條款"><TermsEditor :model-value="q.terms" readonly /></a-descriptions-item>
        </a-descriptions>
      </template>
      <a-empty v-else-if="!quote.isFetching.value" description="找不到報價單" />
    </a-spin>
  </a-drawer>
</template>

<style scoped>
.head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  flex-wrap: wrap;
}
.no {
  font-size: 18px;
  font-weight: 600;
}
.muted {
  color: #8c8c8c;
}
.gap {
  margin-top: 16px;
}
.totals {
  margin-top: 12px;
  margin-left: auto;
  width: 260px;
  max-width: 100%;
}
.totals > div {
  display: flex;
  justify-content: space-between;
  padding: 2px 0;
}
.totals span {
  color: #595959;
}
.totals .total {
  font-weight: 600;
  font-size: 16px;
  border-top: 1px solid #f0f0f0;
  margin-top: 4px;
  padding-top: 6px;
}
</style>
