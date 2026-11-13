<script setup lang="ts">
/**
 * @component QuoteEditorView
 * 報價單編輯與檢視。
 * - 新增：/quotes/edit?projectId=…（客戶資料由後端依專案客戶帶入快照）
 * - 既有：/quotes/edit?id=…；只有草稿可編輯，其他狀態依後端回傳的 actions 顯示可用動作。
 */
import { computed, reactive, ref } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { message, Modal } from 'ant-design-vue';
import { useMe } from '@/app/composables/use-me';
import { ApiError } from '@/shared/api/http-client';
import { getClientApi, getProjectApi, type ProjectDetail } from '@/shared/services/business-service';
import {
  createQuoteApi,
  deleteQuoteApi,
  getQuoteApi,
  reviseQuoteApi,
  transitionQuoteApi,
  updateQuoteApi,
  type QuoteAction,
  type QuoteDetail,
  type QuoteHeader,
  type QuoteItem,
} from '@/shared/services/quote-service';
import { formatDateTime, formatMoney, quoteNumber, quoteStatusColor, quoteStatusLabel } from '@/shared/types/business';
import { downloadFile } from '@/shared/utils/download';
import { calculate } from '@/shared/utils/quote-calc';
import QuoteItemsTable from './QuoteItemsTable.vue';
import QuoteFitTotalModal from './QuoteFitTotalModal.vue';
import AttachmentList from '@/app/components/AttachmentList.vue';
import TermsEditor from '@/app/components/TermsEditor.vue';
import { withBasePath } from '@/shared/utils/base-path';

const params = new URLSearchParams(window.location.search);
const quoteId = ref(params.get('id'));
const projectId = params.get('projectId');
const { can, canOption } = useMe();

const quote = ref<QuoteDetail | null>(null);
const project = ref<ProjectDetail | null>(null);
const notFound = ref(false);
const saving = ref(false);
const items = ref<QuoteItem[]>([]);
const header = reactive<QuoteHeader & { dates: Dayjs[] }>({ dates: [], currency: 'TWD', discountType: 'none', taxType: 'taxable', taxIncluded: false, taxRate: 5 });

const isNew = computed(() => !quoteId.value);
const editable = computed(() => (isNew.value ? can('quotes', 'create') : !!quote.value?.actions.includes('save')));
const showCost = computed(() => canOption('quotes', 'view_cost'));
const totals = computed(() =>
  calculate(items.value, {
    currency: header.currency ?? 'TWD',
    discountType: header.discountType ?? 'none',
    discountValue: header.discountValue,
    taxType: header.taxType ?? 'taxable',
    taxIncluded: !!header.taxIncluded,
    taxRate: header.taxRate,
  }),
);

const fitOpen = ref(false);
const fitHeader = computed<QuoteHeader>(() => ({
  currency: header.currency,
  discountType: header.discountType,
  discountValue: header.discountValue,
  taxType: header.taxType,
  taxIncluded: header.taxIncluded,
  taxRate: header.taxRate,
}));

function applyFit(value: { items: QuoteItem[]; discountType: QuoteHeader['discountType']; discountValue: number }) {
  items.value = value.items;
  header.discountType = value.discountType;
  header.discountValue = value.discountValue;
  fitOpen.value = false;
}

const headerFields = ['title', 'clientName', 'clientTaxId', 'clientAddress', 'contactName', 'contactPhone', 'contactEmail', 'currency', 'discountType', 'discountValue', 'taxType', 'taxIncluded', 'taxRate', 'paymentTerms', 'deliveryTerms', 'terms', 'note'] as const;

function fillFrom(detail: QuoteDetail) {
  quote.value = detail;
  headerFields.forEach((field) => ((header as Record<string, unknown>)[field] = detail[field]));
  header.dates = [dayjs(detail.quoteDate), ...(detail.validUntil ? [dayjs(detail.validUntil)] : [])];
  items.value = detail.items.map((item) => ({ ...item }));
}

async function load() {
  try {
    if (quoteId.value) {
      fillFrom(await getQuoteApi(quoteId.value));
    } else if (projectId) {
      project.value = await getProjectApi(projectId);
      header.title = project.value.name;
      header.dates = [dayjs()];
      items.value = [{ lineType: 'item', name: '', quantity: 1, unitPrice: 0, discountRate: 0 }];
    } else {
      notFound.value = true;
    }
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound.value = true;
    else message.error((error as Error).message);
  }
}
load();

function payload() {
  const [quoteDate, validUntil] = header.dates;
  const body: Record<string, unknown> = Object.fromEntries(headerFields.map((field) => [field, (header as Record<string, unknown>)[field]]));
  return {
    ...body,
    quoteDate: quoteDate?.format('YYYY-MM-DD'),
    validUntil: validUntil?.format('YYYY-MM-DD'),
    // 新報價的客戶快照留空，由後端依專案客戶帶入。
    ...(isNew.value ? { clientName: undefined, clientTaxId: undefined, clientAddress: undefined, contactName: undefined, contactPhone: undefined, contactEmail: undefined } : {}),
    items: items.value.filter((item) => item.name?.trim()),
  } as Parameters<typeof updateQuoteApi>[1];
}

async function save(): Promise<boolean> {
  saving.value = true;
  try {
    if (isNew.value) {
      const created = await createQuoteApi(projectId!, payload());
      message.success(`已建立報價單 ${created.quoteNo}`);
      quoteId.value = created.id;
      history.replaceState(null, '', withBasePath(`/quotes/edit?id=${created.id}`));
    } else {
      await updateQuoteApi(quoteId.value!, payload());
      message.success('已儲存');
    }
    fillFrom(await getQuoteApi(quoteId.value!));
    return true;
  } catch (error) {
    message.error((error as Error).message);
    return false;
  } finally {
    saving.value = false;
  }
}

/** 重新以客戶主檔與主要聯絡人覆寫快照（只在草稿可用）。 */
async function refreshClientSnapshot() {
  const clientId = quote.value?.clientId ?? project.value?.client.id;
  if (!clientId) return;
  const client = await getClientApi(clientId);
  const contact = client.contacts.find((value) => value.isPrimary) ?? client.contacts[0];
  Object.assign(header, {
    clientName: client.invoiceTitle ?? client.name,
    clientTaxId: client.taxId,
    clientAddress: client.invoiceAddress ?? client.address,
    contactName: contact?.name ?? null,
    contactPhone: contact?.mobile ?? contact?.phone ?? client.phone,
    contactEmail: contact?.email ?? client.email,
    paymentTerms: header.paymentTerms ?? client.paymentTerms,
  });
  message.info('已帶入客戶資料，記得儲存');
}

const actionLabels: Record<QuoteAction, string> = {
  save: '儲存',
  submit: '送審',
  delete: '刪除',
  approve: '核准',
  return: '退回',
  send: '標記已送出',
  accept: '客戶接受',
  reject: '客戶拒絕',
  revise: '改版',
};
const reasonAction = ref<'return' | 'reject' | null>(null);
const reason = ref('');

async function run(action: QuoteAction) {
  const id = quoteId.value!;
  if (action === 'save') return void save();
  if (action === 'return' || action === 'reject') {
    reasonAction.value = action;
    reason.value = '';
    return;
  }
  if (action === 'delete') {
    Modal.confirm({
      title: '刪除這份報價草稿？',
      okType: 'danger',
      onOk: async () => {
        await deleteQuoteApi(id);
        window.location.assign(withBasePath(`/projects/detail?id=${quote.value!.project.id}`));
      },
    });
    return;
  }
  if (action === 'submit' && !(await save())) return;
  if (action === 'accept') {
    const confirmed = await new Promise<boolean>((resolve) =>
      Modal.confirm({
        title: '確認客戶接受此報價？',
        content: '成交金額會回寫到專案，專案改為「得標」，同專案其他進行中的報價會作廢。',
        onOk: () => resolve(true),
        onCancel: () => resolve(false),
      }),
    );
    if (!confirmed) return;
  }
  try {
    if (action === 'revise') {
      const revision = await reviseQuoteApi(id);
      message.success(`已建立第 ${revision.revisionNo} 版草稿`);
      window.location.assign(withBasePath(`/quotes/edit?id=${revision.id}`));
      return;
    }
    await transitionQuoteApi(id, action);
    message.success(`已${actionLabels[action]}`);
    fillFrom(await getQuoteApi(id));
  } catch (error) {
    message.error((error as Error).message);
  }
}

async function submitReason() {
  if (!reason.value.trim()) {
    message.warning('請填寫原因');
    return;
  }
  try {
    await transitionQuoteApi(quoteId.value!, reasonAction.value!, reason.value);
    message.success(`已${actionLabels[reasonAction.value!]}`);
    reasonAction.value = null;
    fillFrom(await getQuoteApi(quoteId.value!));
  } catch (error) {
    message.error((error as Error).message);
  }
}

async function download(kind: 'pdf' | 'excel') {
  try {
    await downloadFile(`/api/quotes/${quoteId.value}/${kind}`, `報價單.${kind === 'pdf' ? 'pdf' : 'xlsx'}`);
  } catch (error) {
    message.error((error as Error).message);
  }
}

const projectInfo = computed(() => quote.value?.project ?? (project.value ? { id: project.value.id, projectNo: project.value.projectNo, name: project.value.name } : null));
const primaryActions = computed(() => (quote.value?.actions ?? []).filter((action) => action !== 'delete' && action !== 'save'));
</script>

<template>
  <a-result v-if="notFound" status="404" title="找不到報價單" sub-title="報價單不存在，或不在您的資料範圍內。">
    <template #extra><a-button type="primary" :href="withBasePath('/quotes')">回報價單列表</a-button></template>
  </a-result>
  <a-skeleton v-else-if="!quote && !project" active />
  <a-space v-else direction="vertical" :size="16" style="width: 100%">
    <a-card :bordered="false">
      <div class="head">
        <div>
          <div class="crumb">
            <a v-if="projectInfo" :href="withBasePath(`/projects/detail?id=${projectInfo.id}`)">{{ projectInfo.projectNo }} {{ projectInfo.name }}</a>
          </div>
          <h2>
            {{ quote ? quoteNumber(quote) : '新報價單' }}
            <a-tag v-if="quote" :color="quoteStatusColor(quote.status)">{{ quoteStatusLabel(quote.status) }}</a-tag>
            <a-tag v-if="quote?.isExpired" color="red">已逾期</a-tag>
            <a-tag v-if="quote && !quote.isLatest" color="default">舊版本</a-tag>
          </h2>
          <a-space v-if="quote && quote.revisions.length > 1" wrap :size="4" class="revisions">
            <span class="muted">版本：</span>
            <a-tag v-for="revision in quote.revisions" :key="revision.id" :color="revision.id === quote.id ? 'blue' : undefined">
              <a :href="withBasePath(`/quotes/edit?id=${revision.id}`)">R{{ revision.revisionNo }} {{ quoteStatusLabel(revision.status) }}</a>
            </a-tag>
          </a-space>
        </div>
        <a-space wrap>
          <a-button v-if="isNew" type="primary" :loading="saving" @click="save">建立報價單</a-button>
          <template v-else-if="quote">
            <a-button v-if="quote.actions.includes('save')" :loading="saving" @click="run('save')">儲存</a-button>
            <a-button v-for="action in primaryActions" :key="action" :type="action === 'submit' || action === 'approve' || action === 'accept' ? 'primary' : 'default'" :danger="action === 'reject' || action === 'return'" @click="run(action)">
              {{ actionLabels[action] }}
            </a-button>
            <a-button v-if="canOption('quotes', 'export_pdf')" @click="download('pdf')">下載 PDF</a-button>
            <a-button v-if="canOption('quotes', 'export_excel')" @click="download('excel')">下載 Excel</a-button>
            <a-button v-if="quote.actions.includes('delete') && can('quotes', 'delete')" danger @click="run('delete')">刪除</a-button>
          </template>
        </a-space>
      </div>
      <a-alert v-if="quote?.statusReason" type="warning" show-icon :message="`${quote.status === 'rejected' ? '客戶拒絕原因' : '退回原因'}：${quote.statusReason}`" class="gap" />
      <a-alert v-if="quote && !editable && quote.status === 'draft'" type="info" show-icon message="您對此報價只有檢視權限。" class="gap" />
      <a-alert v-else-if="quote && !editable && quote.isLatest" type="info" show-icon message="已送審的報價內容鎖定；需要修改請按「改版」。" class="gap" />
    </a-card>

    <a-card title="客戶與報價條件" :bordered="false">
      <template #extra>
        <a-button v-if="editable && !isNew" size="small" @click="refreshClientSnapshot">重新帶入客戶資料</a-button>
      </template>
      <a-alert v-if="isNew" type="info" show-icon message="客戶名稱、統編、聯絡人會在建立時依專案客戶自動帶入，之後可再修改。" class="gap-bottom" />
      <!-- 有自己 disabled 條件的欄位要再併入 !editable：元件的 disabled 屬性會蓋過表單層級的設定。 -->
      <a-form layout="vertical" :disabled="!editable">
        <a-row :gutter="16">
          <a-col :xs="24" :md="12"><a-form-item label="案名"><a-input v-model:value="header.title" /></a-form-item></a-col>
          <a-col :xs="24" :md="12"><a-form-item label="報價日期 ／ 有效期限"><a-range-picker v-model:value="header.dates" :allow-empty="[false, true]" style="width: 100%" /></a-form-item></a-col>
          <template v-if="!isNew">
            <a-col :xs="24" :md="12"><a-form-item label="客戶名稱（報價抬頭）"><a-input v-model:value="header.clientName" /></a-form-item></a-col>
            <a-col :xs="24" :md="6"><a-form-item label="統一編號"><a-input v-model:value="header.clientTaxId" /></a-form-item></a-col>
            <a-col :xs="24" :md="6"><a-form-item label="聯絡人"><a-input v-model:value="header.contactName" /></a-form-item></a-col>
            <a-col :xs="24" :md="12"><a-form-item label="地址"><a-input v-model:value="header.clientAddress" /></a-form-item></a-col>
            <a-col :xs="24" :md="6"><a-form-item label="電話"><a-input v-model:value="header.contactPhone" /></a-form-item></a-col>
            <a-col :xs="24" :md="6"><a-form-item label="Email"><a-input v-model:value="header.contactEmail" /></a-form-item></a-col>
          </template>
          <a-col :xs="12" :md="6">
            <a-form-item label="稅別">
              <a-select v-model:value="header.taxType" :options="[{ value: 'taxable', label: '應稅' }, { value: 'zero', label: '零稅率' }, { value: 'exempt', label: '免稅' }]" />
            </a-form-item>
          </a-col>
          <a-col :xs="12" :md="6">
            <a-form-item label="稅率 %"><a-input-number v-model:value="header.taxRate" :min="0" :max="100" :disabled="!editable || header.taxType !== 'taxable'" style="width: 100%" /></a-form-item>
          </a-col>
          <a-col :xs="12" :md="6">
            <a-form-item label="單價">
              <a-radio-group v-model:value="header.taxIncluded" :disabled="!editable || header.taxType !== 'taxable'">
                <a-radio-button :value="false">未稅（外加）</a-radio-button>
                <a-radio-button :value="true">含稅</a-radio-button>
              </a-radio-group>
            </a-form-item>
          </a-col>
          <a-col :xs="12" :md="6">
            <a-form-item label="整單折扣">
              <a-input-group compact style="display: flex">
                <a-select v-model:value="header.discountType" style="width: 96px" :options="[{ value: 'none', label: '無' }, { value: 'percent', label: '%' }, { value: 'amount', label: '金額' }]" />
                <a-input-number v-model:value="header.discountValue" :min="0" :disabled="!editable || header.discountType === 'none'" style="flex: 1" />
              </a-input-group>
            </a-form-item>
          </a-col>
          <a-col :xs="24" :md="12"><a-form-item label="付款條件"><a-input v-model:value="header.paymentTerms" /></a-form-item></a-col>
          <a-col :xs="24" :md="12"><a-form-item label="交貨條件"><a-input v-model:value="header.deliveryTerms" /></a-form-item></a-col>
          <a-col :span="24">
            <a-form-item label="備註條款（會印在報價單上，編號自動產生）">
              <TermsEditor v-model="header.terms" :readonly="!editable" empty-text="沒有條款" />
            </a-form-item>
          </a-col>
          <a-col :span="24"><a-form-item label="內部備註（不印出）"><a-textarea v-model:value="header.note" :rows="2" /></a-form-item></a-col>
        </a-row>
      </a-form>
    </a-card>

    <a-card title="報價明細" :bordered="false">
      <template #extra>
        <a-button v-if="editable" size="small" @click="fitOpen = true">調整至目標總價</a-button>
      </template>
      <QuoteItemsTable v-model="items" :readonly="!editable" :show-cost="showCost" :can-search-products="can('products', 'read')" :currency="header.currency ?? 'TWD'" />
      <div class="totals">
        <dl>
          <dt>小計</dt>
          <dd>{{ formatMoney(totals.subtotal) }}</dd>
          <template v-if="totals.discountAmount">
            <dt>折扣</dt>
            <dd>−{{ formatMoney(totals.discountAmount) }}</dd>
          </template>
          <dt>未稅金額</dt>
          <dd>{{ formatMoney(totals.netAmount) }}</dd>
          <dt>稅額</dt>
          <dd>{{ formatMoney(totals.taxAmount) }}</dd>
          <dt class="strong">總計（{{ header.currency }}）</dt>
          <dd class="strong">{{ formatMoney(totals.total) }}</dd>
          <template v-if="showCost && totals.costTotal !== null">
            <dt class="muted">成本 ／ 毛利</dt>
            <dd class="muted">
              {{ formatMoney(totals.costTotal) }} ／ {{ formatMoney(totals.grossMargin) }}
              <span v-if="totals.netAmount">（{{ (((totals.grossMargin ?? 0) / totals.netAmount) * 100).toFixed(1) }}%）</span>
            </dd>
          </template>
        </dl>
      </div>
    </a-card>

    <a-card v-if="quote" title="附件（例如客戶簽回的報價單）" :bordered="false" size="small">
      <AttachmentList owner-type="quote" :owner-id="quote.id" category="簽回報價單" />
    </a-card>

    <a-card v-if="quote" title="紀錄" :bordered="false" size="small">
      <a-descriptions :column="{ xs: 1, md: 3 }" size="small">
        <a-descriptions-item label="建立">{{ quote.createdBy }} · {{ formatDateTime(quote.createdAt) }}</a-descriptions-item>
        <a-descriptions-item label="送審">{{ quote.submittedBy ?? '—' }} · {{ formatDateTime(quote.submittedAt) }}</a-descriptions-item>
        <a-descriptions-item label="核准">{{ quote.approvedBy ?? '—' }} · {{ formatDateTime(quote.approvedAt) }}</a-descriptions-item>
        <a-descriptions-item label="送出">{{ formatDateTime(quote.sentAt) }}</a-descriptions-item>
        <a-descriptions-item label="客戶接受">{{ formatDateTime(quote.acceptedAt) }}</a-descriptions-item>
        <a-descriptions-item label="來源">{{ { manual: '手動建立', excel_template: 'Excel 範本匯入', legacy_excel: '舊報價匯入', ocr: '圖像識別', copy: '改版' }[quote.source] ?? quote.source }}</a-descriptions-item>
      </a-descriptions>
    </a-card>
  </a-space>

  <QuoteFitTotalModal :open="fitOpen" :items="items" :header="fitHeader" :show-cost="showCost" @close="fitOpen = false" @apply="applyFit" />

  <a-modal :open="!!reasonAction" :title="reasonAction === 'return' ? '退回報價' : '客戶拒絕'" ok-text="確認" @ok="submitReason" @cancel="reasonAction = null">
    <a-textarea v-model:value="reason" :rows="3" placeholder="請填寫原因（必填）" />
  </a-modal>
</template>

<style scoped>
.head {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}
.crumb {
  font-size: 13px;
}
h2 {
  margin: 4px 0;
  font-size: 20px;
}
.revisions {
  margin-top: 4px;
}
.gap {
  margin-top: 12px;
}
.gap-bottom {
  margin-bottom: 16px;
}
.muted {
  color: #999;
}
.totals {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
.totals dl {
  display: grid;
  grid-template-columns: auto minmax(140px, auto);
  gap: 6px 24px;
  margin: 0;
  font-variant-numeric: tabular-nums;
}
.totals dt {
  color: #555;
}
.totals dd {
  margin: 0;
  text-align: right;
}
.totals .strong {
  font-size: 18px;
  font-weight: 600;
  color: #1f4e79;
}
</style>
