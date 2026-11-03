<script setup lang="ts">
/**
 * @component ProjectInvoicesTab
 * 專案的發票（可多筆）：開立、登記收款、作廢；買受人預設帶專案客戶的發票抬頭與統編。
 * 號碼開錯時：未收款可刪除後重開，已處理過的請作廢。
 */
import { reactive, ref, watch } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { message, Modal } from 'ant-design-vue';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import RemoteSelect, { type RemoteOption } from '@/app/components/RemoteSelect.vue';
import { dataColumns } from '@/shared/table/data-table';
import AttachmentList from '@/app/components/AttachmentList.vue';
import InvoicePreviewDrawer from '@/app/components/InvoicePreviewDrawer.vue';
import { useMe } from '@/app/composables/use-me';
import type { ProjectDetail } from '@/shared/services/business-service';
import { deleteInvoiceApi, listInvoicesApi, listOrdersApi, lookupInvoiceNoApi, recordPaymentApi, saveInvoiceApi, voidInvoiceApi, type Invoice, type InvoiceLookup, type Order } from '@/shared/services/document-service';
import { formatMoney } from '@/shared/types/business';

const props = defineProps<{ project: ProjectDetail }>();
const emit = defineEmits<{ changed: [] }>();
const { can } = useMe();
const queryClient = useQueryClient();
// 搜尋與分頁由後端處理：換頁、換每頁筆數都會重新向 API 取資料。
const paging = reactive({ page: 1, pageSize: 20 });
const invoices = useQuery({
  queryKey: ['invoices', props.project.id, paging],
  queryFn: () => listInvoicesApi(props.project.id, { ...paging }),
  placeholderData: keepPreviousData,
});
// 對應訂單：排除已取消的訂單（activeOnly）與關鍵字搜尋都由後端處理。
const fetchOrders = async (keyword: string): Promise<RemoteOption<Order>[]> =>
  (await listOrdersApi(props.project.id, { activeOnly: true, keyword, pageSize: 20 })).items.map((order) => ({ value: order.id, label: `${order.orderNo}（${formatMoney(order.total)}）`, raw: order }));
function onTableChange(change: TableChange) {
  Object.assign(paging, { page: change.page, pageSize: change.pageSize });
}
const typeLabels = { duplicate: '二聯式', triplicate: '三聯式', e_invoice: '電子發票' };
const paymentLabels = { unpaid: ['未收款', 'default'], partial: ['部分收款', 'orange'], paid: ['已收款', 'green'] } as const;

const refresh = async () => {
  await queryClient.invalidateQueries({ queryKey: ['invoices', props.project.id] });
  emit('changed');
};

const formOpen = ref(false);
const form = reactive<Partial<Invoice> & { date?: Dayjs; due?: Dayjs | null }>({});
const saving = ref(false);

// ---------- 發票號碼防呆 ----------
// 號碼在公司內唯一（含已作廢的）；輸入完整號碼後即時向後端查詢，重複時擋下儲存，並可查看那張發票。
const invoiceNoPattern = /^[A-Za-z]{2}-?\d{8}$/;
const duplicate = ref<InvoiceLookup | null>(null);
const checking = ref(false);
const previewOpen = ref(false);
let checkTimer: ReturnType<typeof setTimeout> | undefined;
let checkTicket = 0;
watch(
  () => form.invoiceNo,
  (value) => {
    clearTimeout(checkTimer);
    duplicate.value = null;
    const text = (value ?? '').trim();
    if (!invoiceNoPattern.test(text)) {
      checking.value = false;
      return;
    }
    checking.value = true;
    const ticket = ++checkTicket;
    checkTimer = setTimeout(async () => {
      try {
        const result = await lookupInvoiceNoApi(text, form.id);
        if (ticket === checkTicket) duplicate.value = result.exists ? result : null;
      } catch {
        // 查詢失敗不擋輸入，儲存時後端仍會檢查唯一性。
      } finally {
        if (ticket === checkTicket) checking.value = false;
      }
    }, 350);
  },
);

function open(row: Invoice | null) {
  duplicate.value = null;
  Object.keys(form).forEach((key) => delete (form as Record<string, unknown>)[key]);
  Object.assign(form, row ? { ...row, date: dayjs(row.invoiceDate), due: row.dueDate ? dayjs(row.dueDate) : null } : { date: dayjs(), invoiceType: 'triplicate', taxAmount: 0, due: dayjs().add(30, 'day') });
  formOpen.value = true;
}

/** 選訂單時帶入未稅與稅額。 */
function fillFromOrder(raw: unknown) {
  const order = raw as Order | undefined;
  if (order && !form.salesAmount) Object.assign(form, { salesAmount: order.amount, taxAmount: order.taxAmount });
}

async function save() {
  if (duplicate.value) return message.error(`發票號碼 ${duplicate.value.invoiceNo} 已登錄過，請確認號碼。`);
  saving.value = true;
  try {
    await saveInvoiceApi(props.project.id, { ...form, invoiceDate: form.date?.format('YYYY-MM-DD'), dueDate: form.due?.format('YYYY-MM-DD') ?? null });
    message.success('發票已儲存');
    formOpen.value = false;
    await refresh();
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

const payment = reactive<{ invoice: Invoice | null; amount: number; date: Dayjs }>({ invoice: null, amount: 0, date: dayjs() });
async function savePayment() {
  try {
    await recordPaymentApi(payment.invoice!.id, payment.amount, payment.date.format('YYYY-MM-DD'));
    message.success('收款已登記');
    payment.invoice = null;
    await refresh();
  } catch (error) {
    message.error((error as Error).message);
  }
}

const voiding = reactive<{ invoice: Invoice | null; reason: string }>({ invoice: null, reason: '' });
async function saveVoid() {
  try {
    await voidInvoiceApi(voiding.invoice!.id, voiding.reason);
    message.success('發票已作廢');
    voiding.invoice = null;
    await refresh();
  } catch (error) {
    message.error((error as Error).message);
  }
}

function remove(row: Invoice) {
  Modal.confirm({
    title: `刪除發票 ${row.invoiceNo}？`,
    content: '只適用於輸入錯誤；刪除後號碼可重新登錄。已開出給客戶的發票請改用「作廢」。',
    okType: 'danger',
    onOk: async () => {
      try {
        await deleteInvoiceApi(row.id);
        await refresh();
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}

const col = dataColumns<Invoice>();
const columns = [
  col.accessor('invoiceNo', { header: '發票號碼', meta: { width: 130 } }),
  col.accessor('invoiceDate', { header: '日期', meta: { width: 105 } }),
  col.accessor('invoiceType', { header: '類型', meta: { width: 90 } }),
  col.accessor('orderNo', { header: '對應訂單', meta: { width: 130 } }),
  col.accessor('buyerName', { header: '買受人' }),
  col.accessor('totalAmount', { header: '總額', meta: { width: 110, align: 'right' } }),
  col.accessor('paymentStatus', { header: '收款', meta: { width: 150 } }),
  col.accessor('dueDate', { header: '到期日', meta: { width: 105 } }),
  col.accessor('attachmentCount', { header: '附件', meta: { width: 60 } }),
  col.display({ id: 'ops', header: '', meta: { width: 210 } }),
];
</script>

<template>
  <div class="toolbar">
    <span class="muted">已開立 {{ formatMoney(invoices.data.value?.totalAmount) }} · 已收款 {{ formatMoney(invoices.data.value?.paidAmount) }}</span>
    <a-button v-if="invoices.data.value?.canCreate" type="primary" @click="open(null)">開立發票</a-button>
  </div>
  <DataTable
    :data="invoices.data.value?.items ?? []"
    :columns="columns"
    row-key="id"
    size="small"
    :loading="invoices.isFetching.value"
    :pagination="{ page: paging.page, pageSize: paging.pageSize, total: invoices.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 筆` }"
    :scroll-x="1170"
    @change="onTableChange"
  >
    <template #cell="{ columnId, record }">
      <template v-if="columnId === 'invoiceNo'">
        <span :class="{ voided: record.status === 'void' }">{{ record.invoiceNo }}</span>
        <a-tag v-if="record.status === 'void'" color="red">作廢</a-tag>
      </template>
      <template v-else-if="columnId === 'invoiceType'">{{ typeLabels[record.invoiceType as Invoice['invoiceType']] }}</template>
      <template v-else-if="columnId === 'buyerName'">
        {{ record.buyerName }}<div class="muted small">{{ record.buyerTaxId }}</div>
      </template>
      <template v-else-if="columnId === 'totalAmount'">{{ formatMoney(record.totalAmount) }}</template>
      <template v-else-if="columnId === 'paymentStatus'">
        <template v-if="record.status === 'issued'">
          <a-tag :color="paymentLabels[record.paymentStatus as Invoice['paymentStatus']][1]">{{ paymentLabels[record.paymentStatus as Invoice['paymentStatus']][0] }}</a-tag>
          <span v-if="record.paymentStatus === 'partial'" class="small">{{ formatMoney(record.paidAmount) }}</span>
          <a-tag v-if="record.isOverdue" color="red">逾期</a-tag>
        </template>
        <span v-else class="muted small">{{ record.voidReason }}</span>
      </template>
      <template v-else-if="columnId === 'ops'">
        <template v-if="record.status === 'issued'">
          <a-button v-if="invoices.data.value?.canMarkPaid" type="link" size="small" @click="Object.assign(payment, { invoice: record, amount: record.totalAmount, date: dayjs() })">收款</a-button>
          <a-button v-if="invoices.data.value?.canUpdate" type="link" size="small" @click="open(record)">編輯</a-button>
          <a-button v-if="invoices.data.value?.canVoid && record.paidAmount === 0" type="link" size="small" danger @click="Object.assign(voiding, { invoice: record, reason: '' })">作廢</a-button>
          <a-button v-if="invoices.data.value?.canDelete && record.paidAmount === 0" type="link" size="small" danger @click="remove(record)">刪除</a-button>
        </template>
      </template>
    </template>
    <template #expanded="{ record }">
      <AttachmentList owner-type="invoice" :owner-id="record.id" category="發票" @changed="queryClient.invalidateQueries({ queryKey: ['invoices', project.id] })" />
    </template>
    <template #empty><a-empty description="還沒有發票" /></template>
  </DataTable>

  <a-modal v-model:open="formOpen" :title="form.id ? '編輯發票' : '開立發票'" :confirm-loading="saving" ok-text="儲存" :ok-button-props="{ disabled: !!duplicate || checking }" @ok="save">
    <a-form layout="vertical">
      <a-row :gutter="12">
        <a-col :span="12">
          <a-form-item label="發票號碼" required :extra="duplicate ? undefined : '例如 AB-12345678'" :validate-status="duplicate ? 'error' : checking ? 'validating' : undefined" :has-feedback="checking">
            <a-input v-model:value="form.invoiceNo" :maxlength="11" />
            <template v-if="duplicate" #help>
              <span class="dup">
                {{ duplicate.invoiceNo }} 已登錄過{{ duplicate.visible ? '' : '（屬於您沒有權限檢視的專案）' }}
                <a v-if="duplicate.visible" @click="previewOpen = true">查看這張發票</a>
              </span>
            </template>
          </a-form-item>
        </a-col>
        <a-col :span="12"><a-form-item label="發票日期" required><a-date-picker v-model:value="form.date" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="12">
          <a-form-item label="類型"><a-select v-model:value="form.invoiceType" :options="Object.entries(typeLabels).map(([value, label]) => ({ value, label }))" /></a-form-item>
        </a-col>
        <a-col :span="12"><a-form-item label="收款到期日"><a-date-picker v-model:value="form.due" style="width: 100%" /></a-form-item></a-col>
        <a-col v-if="can('orders', 'read')" :span="24">
          <a-form-item label="對應訂單" :required="!form.id || !!form.orderId"><RemoteSelect v-model:value="form.orderId" :fetch="fetchOrders" :initial-label="form.orderNo ?? undefined" placeholder="輸入訂單號碼搜尋，選擇後帶入金額" @pick="fillFromOrder" /></a-form-item>
        </a-col>
        <a-col :span="12"><a-form-item label="買受人" extra="留空帶客戶發票抬頭"><a-input v-model:value="form.buyerName" /></a-form-item></a-col>
        <a-col :span="12"><a-form-item label="買受人統編"><a-input v-model:value="form.buyerTaxId" :maxlength="8" /></a-form-item></a-col>
        <a-col :span="12"><a-form-item label="銷售額（未稅）" required><a-input-number v-model:value="form.salesAmount" :min="0" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="12"><a-form-item label="稅額"><a-input-number v-model:value="form.taxAmount" :min="0" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="24"><a-form-item label="總計"><strong>{{ formatMoney((form.salesAmount ?? 0) + (form.taxAmount ?? 0)) }}</strong></a-form-item></a-col>
        <a-col :span="24"><a-form-item label="備註"><a-textarea v-model:value="form.note" :rows="2" /></a-form-item></a-col>
      </a-row>
    </a-form>
  </a-modal>

  <InvoicePreviewDrawer :open="previewOpen" :lookup="duplicate" :current-project-id="project.id" @close="previewOpen = false" />

  <a-modal :open="!!payment.invoice" :title="`登記收款：${payment.invoice?.invoiceNo ?? ''}`" ok-text="儲存" @ok="savePayment" @cancel="payment.invoice = null">
    <a-form layout="vertical">
      <a-form-item :label="`累計已收金額（發票總額 ${formatMoney(payment.invoice?.totalAmount)}）`">
        <a-input-number v-model:value="payment.amount" :min="0" :max="payment.invoice?.totalAmount" style="width: 100%" />
      </a-form-item>
      <a-form-item label="收款日"><a-date-picker v-model:value="payment.date" style="width: 100%" /></a-form-item>
    </a-form>
  </a-modal>

  <a-modal :open="!!voiding.invoice" :title="`作廢發票：${voiding.invoice?.invoiceNo ?? ''}`" ok-text="作廢" :ok-button-props="{ danger: true }" @ok="saveVoid" @cancel="voiding.invoice = null">
    <a-textarea v-model:value="voiding.reason" :rows="3" placeholder="作廢原因（必填）" />
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
.small {
  font-size: 12px;
}
.dup {
  color: #ff4d4f;
}
.dup a {
  margin-left: 6px;
}
.voided {
  text-decoration: line-through;
  color: #999;
}
</style>
