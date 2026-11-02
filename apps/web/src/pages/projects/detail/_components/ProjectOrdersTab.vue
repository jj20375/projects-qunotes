<script setup lang="ts">
/**
 * @component ProjectOrdersTab
 * 專案的客戶訂單（可多筆）；每張訂單必須對應這個專案的定案（客戶已接受）報價。展開列可管理訂單附件（例如 PO 掃描檔）。
 */
import { reactive, ref } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { message, Modal } from 'ant-design-vue';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import RemoteSelect, { type RemoteOption } from '@/app/components/RemoteSelect.vue';
import { dataColumns } from '@/shared/table/data-table';
import AttachmentList from '@/app/components/AttachmentList.vue';
import type { ProjectDetail } from '@/shared/services/business-service';
import { deleteOrderApi, listOrdersApi, saveOrderApi, type Order } from '@/shared/services/document-service';
import { searchQuotesApi, type QuoteRow } from '@/shared/services/quote-service';
import { formatMoney, quoteNumber } from '@/shared/types/business';

const props = defineProps<{ project: ProjectDetail }>();
const emit = defineEmits<{ changed: [] }>();
const queryClient = useQueryClient();
// 搜尋與分頁由後端處理：換頁、換每頁筆數都會重新向 API 取資料。
const paging = reactive({ page: 1, pageSize: 20 });
const orders = useQuery({
  queryKey: ['orders', props.project.id, paging],
  queryFn: () => listOrdersApi(props.project.id, { ...paging }),
  placeholderData: keepPreviousData,
});

// 訂單只能對應定案（客戶已接受）的報價：狀態與關鍵字都由後端處理。
const fetchQuotes = async (keyword: string): Promise<RemoteOption<QuoteRow>[]> =>
  (await searchQuotesApi({ projectId: props.project.id, status: ['accepted'], latestOnly: false, keyword, pageSize: 20 })).items.map((quote) => ({
    value: quote.id,
    label: `${quoteNumber(quote)}（${formatMoney(quote.total)}）`,
    raw: quote,
  }));

const statusLabels: Record<Order['status'], [string, string]> = {
  received: ['已收單', 'blue'],
  in_progress: ['執行中', 'processing'],
  delivered: ['已交貨', 'green'],
  cancelled: ['已取消', 'default'],
};

const formOpen = ref(false);
const form = reactive<Partial<Order> & { date?: Dayjs }>({});
const saving = ref(false);

function open(row: Order | null) {
  Object.keys(form).forEach((key) => delete (form as Record<string, unknown>)[key]);
  Object.assign(form, row ? { ...row, date: dayjs(row.orderDate) } : { date: dayjs(), status: 'received', taxAmount: 0 });
  formOpen.value = true;
}

/** 依接受的報價帶入金額（未稅、稅額）。 */
function fillFromQuote(raw: unknown) {
  const quote = raw as QuoteRow | undefined;
  if (quote && !form.amount) Object.assign(form, { amount: quote.netAmount, taxAmount: quote.total - quote.netAmount });
}

function onTableChange(change: TableChange) {
  Object.assign(paging, { page: change.page, pageSize: change.pageSize });
}

async function save() {
  saving.value = true;
  try {
    await saveOrderApi(props.project.id, { ...form, orderDate: form.date?.format('YYYY-MM-DD') });
    message.success('訂單已儲存');
    formOpen.value = false;
    await queryClient.invalidateQueries({ queryKey: ['orders', props.project.id] });
    emit('changed');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

function remove(row: Order) {
  Modal.confirm({
    title: `刪除訂單 ${row.orderNo}？`,
    okType: 'danger',
    onOk: async () => {
      try {
        await deleteOrderApi(row.id);
        await queryClient.invalidateQueries({ queryKey: ['orders', props.project.id] });
        emit('changed');
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}

const col = dataColumns<Order>();
const columns = [
  col.accessor('orderNo', { header: '訂單號碼' }),
  col.accessor('orderDate', { header: '訂單日期', meta: { width: 110 } }),
  col.accessor('quoteNo', { header: '來源報價', meta: { width: 170 } }),
  col.accessor('amount', { header: '未稅', meta: { width: 110, align: 'right' } }),
  col.accessor('total', { header: '含稅合計', meta: { width: 120, align: 'right' } }),
  col.accessor('status', { header: '狀態', meta: { width: 90 } }),
  col.accessor('attachmentCount', { header: '附件', meta: { width: 70 } }),
  col.display({ id: 'ops', header: '', meta: { width: 120 } }),
];
</script>

<template>
  <div class="toolbar">
    <span class="muted">訂單合計（不含已取消）：{{ formatMoney(orders.data.value?.activeTotal) }}</span>
    <a-button v-if="orders.data.value?.canCreate" type="primary" @click="open(null)">新增訂單</a-button>
  </div>
  <DataTable
    :data="orders.data.value?.items ?? []"
    :columns="columns"
    row-key="id"
    size="small"
    :loading="orders.isFetching.value"
    :pagination="{ page: paging.page, pageSize: paging.pageSize, total: orders.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 筆` }"
    :scroll-x="860"
    @change="onTableChange"
  >
    <template #cell="{ columnId, record }">
      <template v-if="columnId === 'amount' || columnId === 'total'">{{ formatMoney(record[columnId as 'total']) }}</template>
      <template v-else-if="columnId === 'quoteNo'">
        <a v-if="record.quoteId" :href="`/quotes/edit?id=${record.quoteId}`">{{ record.quoteNo }}</a>
      </template>
      <template v-else-if="columnId === 'status'">
        <a-tag :color="statusLabels[record.status as Order['status']][1]">{{ statusLabels[record.status as Order['status']][0] }}</a-tag>
      </template>
      <template v-else-if="columnId === 'ops'">
        <a-button v-if="orders.data.value?.canUpdate" type="link" size="small" @click="open(record)">編輯</a-button>
        <a-button v-if="orders.data.value?.canDelete" type="link" size="small" danger @click="remove(record)">刪除</a-button>
      </template>
    </template>
    <template #expanded="{ record }">
      <AttachmentList owner-type="order" :owner-id="record.id" category="訂單" @changed="queryClient.invalidateQueries({ queryKey: ['orders', project.id] })" />
    </template>
    <template #empty><a-empty description="還沒有訂單" /></template>
  </DataTable>

  <a-modal v-model:open="formOpen" :title="form.id ? '編輯訂單' : '新增訂單'" :confirm-loading="saving" ok-text="儲存" @ok="save">
    <a-form layout="vertical">
      <a-row :gutter="12">
        <a-col :span="14"><a-form-item label="訂單號碼（客戶 PO）" required><a-input v-model:value="form.orderNo" /></a-form-item></a-col>
        <a-col :span="10"><a-form-item label="訂單日期" required><a-date-picker v-model:value="form.date" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="24">
          <a-form-item label="定案報價" :required="!form.id || !!form.quoteId" extra="訂單必須對應客戶已接受的報價；選擇後帶入金額。">
            <RemoteSelect v-model:value="form.quoteId" :fetch="fetchQuotes" :initial-label="form.quoteNo ?? undefined" placeholder="輸入單號搜尋已接受的報價" @pick="fillFromQuote" />
          </a-form-item>
        </a-col>
        <a-col :span="12"><a-form-item label="未稅金額" required><a-input-number v-model:value="form.amount" :min="0" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="12"><a-form-item label="稅額"><a-input-number v-model:value="form.taxAmount" :min="0" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="12">
          <a-form-item label="狀態">
            <a-select v-model:value="form.status" :options="Object.entries(statusLabels).map(([value, [label]]) => ({ value, label }))" />
          </a-form-item>
        </a-col>
        <a-col :span="12"><a-form-item label="含稅合計"><span class="total">{{ formatMoney((form.amount ?? 0) + (form.taxAmount ?? 0)) }}</span></a-form-item></a-col>
        <a-col :span="24"><a-form-item label="備註"><a-textarea v-model:value="form.note" :rows="2" /></a-form-item></a-col>
      </a-row>
    </a-form>
  </a-modal>
</template>

<style scoped>
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.muted {
  color: #666;
}
.total {
  font-size: 16px;
  font-weight: 600;
}
</style>
