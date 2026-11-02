<script setup lang="ts">
/**
 * @component QuoteImportModal
 * 匯入報價（兩階段）：上傳 → 預覽（可修正表頭與明細）→ 確認後建立報價草稿並開啟編輯頁。
 * 來源可以是系統範本 Excel、舊報價 Excel，或報價單的照片／掃描檔／PDF（圖像識別，由後端呼叫 Claude 或 Gemini）。
 * 解析結果不會直接寫入正式資料；有疑慮的列會列在提醒中，圖像識別的預覽會並排顯示原圖方便核對。
 */
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import DataTable from '@/app/components/DataTable.vue';
import TermsEditor from '@/app/components/TermsEditor.vue';
import ProjectSelect from '@/app/components/ProjectSelect.vue';
import { useMe } from '@/app/composables/use-me';
import {
  confirmQuoteImportApi,
  getQuoteImportOptionsApi,
  listMappingProfilesApi,
  uploadQuoteImportApi,
  type ImportJob,
  type ImportKind,
  type MappingProfile,
  type ParsedQuoteItem,
} from '@/shared/services/quote-service';
import { dataColumns } from '@/shared/table/data-table';
import { formatMoney } from '@/shared/types/business';
import { downloadFile } from '@/shared/utils/download';
import MappingProfileDrawer from './MappingProfileDrawer.vue';

const props = defineProps<{ open: boolean; projectId?: string; projectLabel?: string }>();
const emit = defineEmits<{ close: [] }>();
const { canOption } = useMe();

const canTemplate = computed(() => canOption('quotes', 'import_excel'));
const canLegacy = computed(() => canOption('quotes', 'import_legacy'));
const ocrEnabled = ref(false);
const ocrProviderLabel = ref('');
const providerLabels: Record<string, string> = { claude: 'Claude（Anthropic）', gemini: 'Gemini（Google）' };
const canOcr = computed(() => canOption('quotes', 'import_ocr') && ocrEnabled.value);
const kind = ref<ImportKind>('excel_template');
/** 圖像識別時在預覽旁顯示原圖（只在瀏覽器本機，不另外上傳保存）。 */
const previewUrl = ref<string | null>(null);
const isOcr = computed(() => kind.value === 'ocr');
const acceptTypes = computed(() => (isOcr.value ? '.jpg,.jpeg,.png,.webp,.gif,.pdf' : '.xlsx'));
const profileId = ref<string>();
const profiles = ref<MappingProfile[]>([]);
const profilesOpen = ref(false);
const file = ref<File | null>(null);
const targetProjectId = ref<string | undefined>(props.projectId);
const job = ref<ImportJob | null>(null);
const items = ref<ParsedQuoteItem[]>([]);
const header = reactive({ title: '', quoteDate: '', validUntil: '', paymentTerms: '', terms: null as string | null });
const busy = ref(false);

watch(
  () => props.open,
  async (open) => {
    if (!open) return;
    job.value = null;
    setFile(null);
    targetProjectId.value = props.projectId;
    if (canOption('quotes', 'import_ocr')) {
      const options = await getQuoteImportOptionsApi().catch(() => ({ ocrEnabled: false, ocrProvider: null }));
      ocrEnabled.value = options.ocrEnabled;
      ocrProviderLabel.value = providerLabels[options.ocrProvider ?? ''] ?? '';
    }
    kind.value = canTemplate.value ? 'excel_template' : canLegacy.value ? 'legacy_excel' : 'ocr';
    if (canLegacy.value) profiles.value = await listMappingProfilesApi().catch(() => []);
  },
);

watch(kind, () => setFile(null));
onBeforeUnmount(() => setFile(null));

function setFile(selected: File | null) {
  if (previewUrl.value) URL.revokeObjectURL(previewUrl.value);
  file.value = selected;
  previewUrl.value = selected && isOcr.value ? URL.createObjectURL(selected) : null;
}

function beforeUpload(selected: File) {
  setFile(selected);
  return false;
}

async function upload() {
  if (!file.value) {
    message.warning(isOcr.value ? '請選擇報價單的圖片或 PDF' : '請選擇 Excel 檔案');
    return;
  }
  busy.value = true;
  try {
    job.value = await uploadQuoteImportApi(file.value, kind.value, kind.value === 'legacy_excel' ? profileId.value : undefined);
    items.value = job.value.preview.items.map((item) => ({ ...item }));
    const h = job.value.preview.header;
    Object.assign(header, { title: h.Title ?? '', quoteDate: h.QuoteDate ?? '', validUntil: h.ValidUntil ?? '', paymentTerms: h.PaymentTerms ?? '', terms: h.Terms ?? null });
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    busy.value = false;
  }
}

async function confirm() {
  if (!targetProjectId.value) {
    message.warning('請選擇要匯入到哪個專案');
    return;
  }
  const h = job.value!.preview.header;
  busy.value = true;
  try {
    const created = await confirmQuoteImportApi(job.value!.id, targetProjectId.value, {
      title: header.title || null,
      quoteDate: header.quoteDate || null,
      validUntil: header.validUntil || null,
      paymentTerms: header.paymentTerms || null,
      terms: header.terms || null,
      taxType: (h.TaxType as 'taxable' | 'zero' | 'exempt' | null) ?? undefined,
      taxIncluded: h.TaxIncluded ? h.TaxIncluded === 'true' : undefined,
      taxRate: h.TaxRate ? Number(h.TaxRate) : undefined,
      discountType: (h.DiscountType as 'percent' | 'amount' | null) ?? undefined,
      discountValue: h.DiscountValue ? Number(h.DiscountValue) : undefined,
      items: items.value.map(({ lineType, itemCode, name, spec, unit, quantity, unitPrice, discountRate, unitCost, remark }) => ({
        lineType,
        itemCode,
        name,
        spec,
        unit,
        quantity,
        unitPrice,
        discountRate,
        unitCost,
        remark,
      })),
    });
    message.success(`已建立報價單 ${created.quoteNo}`);
    window.location.assign(`/quotes/edit?id=${created.id}`);
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    busy.value = false;
  }
}

const col = dataColumns<ParsedQuoteItem>();
const columns = [
  col.accessor('row', { header: '列', meta: { width: 50 } }),
  col.accessor('lineType', { header: '類型', meta: { width: 80 } }),
  col.accessor('name', { header: '品名' }),
  col.accessor('unit', { header: '單位', meta: { width: 70 } }),
  col.accessor('quantity', { header: '數量', meta: { width: 100 } }),
  col.accessor('unitPrice', { header: '單價', meta: { width: 120 } }),
  col.accessor('discountRate', { header: '折扣%', meta: { width: 80 } }),
  col.display({ id: 'ops', header: '', meta: { width: 50 } }),
];
const fieldNames: Record<string, string> = { ItemCode: '品號', Name: '品名', Spec: '規格', Unit: '單位', Quantity: '數量', UnitPrice: '單價', DiscountRate: '折扣', Amount: '金額', UnitCost: '成本', Remark: '備註' };
</script>

<template>
  <a-modal :open="open" :width="job ? (previewUrl ? 1280 : 1000) : 600" title="匯入報價" :footer="null" @cancel="emit('close')">
    <template v-if="!job">
      <a-form layout="vertical">
        <a-form-item label="檔案類型">
          <a-radio-group v-model:value="kind">
            <a-radio-button value="excel_template" :disabled="!canTemplate">系統範本／本系統匯出的報價</a-radio-button>
            <a-radio-button value="legacy_excel" :disabled="!canLegacy">舊報價</a-radio-button>
            <a-radio-button v-if="canOcr" value="ocr">照片／掃描檔</a-radio-button>
          </a-radio-group>
          <div class="hint">
            <a v-if="canTemplate" @click="downloadFile('/api/quotes/import-template', '報價匯入範本.xlsx')">下載匯入範本</a>
          </div>
        </a-form-item>
        <a-form-item v-if="kind === 'legacy_excel'" label="對照設定" extra="不選則依表頭文字（品名、數量、單價…）自動辨識">
          <a-space style="width: 100%">
            <a-select v-model:value="profileId" allow-clear placeholder="自動辨識" style="width: 280px" :options="profiles.map((p) => ({ value: p.id, label: p.name }))" />
            <a-button @click="profilesOpen = true">管理對照設定</a-button>
          </a-space>
        </a-form-item>
        <a-alert
          v-if="isOcr"
          type="info"
          show-icon
          style="margin-bottom: 16px"
          :message="`圖像識別會把檔案送到 ${ocrProviderLabel} 辨識，約需 30 秒到 2 分鐘；結果只是預覽，請對照原圖核對後再建立。`"
        />
        <a-form-item :label="isOcr ? '報價單圖片或 PDF（JPG、PNG、WebP 5 MB 以內；PDF 10 MB 以內）' : 'Excel 檔（.xlsx，10 MB 以內）'">
          <a-upload :before-upload="beforeUpload" :max-count="1" :accept="acceptTypes" :show-upload-list="false">
            <a-button>選擇檔案</a-button>
          </a-upload>
          <span v-if="file" class="file">{{ file.name }}</span>
        </a-form-item>
        <img v-if="previewUrl && file?.type.startsWith('image/')" :src="previewUrl" alt="選擇的報價單圖片" class="thumb" />
        <a-button type="primary" :loading="busy" block @click="upload">{{ isOcr ? (busy ? '辨識中…' : '辨識並預覽') : '解析並預覽' }}</a-button>
      </a-form>
    </template>

    <template v-else>
      <a-alert
        v-if="job.preview.warnings.length"
        type="warning"
        show-icon
        :message="`有 ${job.preview.warnings.length} 項需要確認`"
        style="margin-bottom: 12px"
      >
        <template #description>
          <ul class="warnings"><li v-for="warning in job.preview.warnings" :key="warning">{{ warning }}</li></ul>
        </template>
      </a-alert>
      <p v-if="job.kind === 'ocr'" class="hint">圖像辨識結果：{{ job.fileName }}。請對照原圖核對品名、數量與單價。</p>
      <p v-else class="hint">
        工作表「{{ job.preview.sheetName }}」，表頭在第 {{ job.preview.headerRow }} 列；辨識到的欄位：
        <a-tag v-for="(letter, field) in job.preview.detectedColumns" :key="field">{{ fieldNames[field] ?? field }} {{ letter }}</a-tag>
      </p>
      <div :class="{ 'with-source': previewUrl }">
        <div class="source" v-if="previewUrl">
          <img v-if="file?.type.startsWith('image/')" :src="previewUrl" alt="原始報價單" />
          <iframe v-else :src="previewUrl" title="原始報價單" />
        </div>
        <div class="editor">
          <a-form layout="vertical">
            <a-row :gutter="12">
              <a-col :xs="24" :md="12"><a-form-item label="匯入到專案" required><ProjectSelect v-model:value="targetProjectId" :initial-label="projectLabel" /></a-form-item></a-col>
              <a-col :xs="24" :md="12"><a-form-item label="案名"><a-input v-model:value="header.title" /></a-form-item></a-col>
              <a-col :xs="12" :md="6"><a-form-item label="報價日期"><a-input v-model:value="header.quoteDate" placeholder="YYYY-MM-DD" /></a-form-item></a-col>
              <a-col :xs="12" :md="6"><a-form-item label="有效期限"><a-input v-model:value="header.validUntil" placeholder="YYYY-MM-DD" /></a-form-item></a-col>
              <a-col :xs="24" :md="12"><a-form-item label="付款條件"><a-input v-model:value="header.paymentTerms" /></a-form-item></a-col>
            </a-row>
          </a-form>
          <p v-if="job.preview.header.ClientName" class="hint">檔案中的客戶名稱：{{ job.preview.header.ClientName }}（報價會使用專案客戶的資料，可在編輯頁修改）</p>
          <DataTable :data="items" :columns="columns" row-key="row" size="small" :scroll-x="760" :scroll-y="360">
            <template #cell="{ columnId, record, index }">
              <template v-if="columnId === 'lineType'">
                <a-select v-model:value="record.lineType" size="small" :options="[{ value: 'item', label: '品項' }, { value: 'section', label: '分段' }]" style="width: 70px" />
              </template>
              <template v-else-if="columnId === 'name'"><a-input v-model:value="record.name" size="small" /></template>
              <template v-else-if="columnId === 'unit'"><a-input v-model:value="record.unit" size="small" /></template>
              <template v-else-if="columnId === 'quantity' || columnId === 'unitPrice' || columnId === 'discountRate'">
                <a-input-number v-if="record.lineType === 'item'" v-model:value="record[columnId]" size="small" :min="0" style="width: 100%" />
              </template>
              <template v-else-if="columnId === 'ops'">
                <a-button type="text" size="small" danger @click="items.splice(index, 1)">✕</a-button>
              </template>
            </template>
          </DataTable>
          <div class="terms-block">
            <div class="terms-label">備註條款（{{ header.terms ? header.terms.split('\n').length : 0 }} 條，編號自動產生）</div>
            <TermsEditor v-model="header.terms" />
          </div>
        </div>
      </div>
      <div class="footer">
        <span class="hint">共 {{ items.filter((i) => i.lineType === 'item').length }} 筆品項，預估金額 {{ formatMoney(items.reduce((sum, i) => sum + (i.lineType === 'item' ? (i.quantity ?? 0) * (i.unitPrice ?? 0) * (1 - (i.discountRate ?? 0) / 100) : 0), 0)) }}（未稅、未含整單折扣）</span>
        <a-space>
          <a-button @click="job = null">重新選擇檔案</a-button>
          <a-button type="primary" :loading="busy" @click="confirm">建立報價草稿</a-button>
        </a-space>
      </div>
    </template>
  </a-modal>
  <MappingProfileDrawer :open="profilesOpen" @close="profilesOpen = false" @changed="listMappingProfilesApi().then((value) => (profiles = value))" />
</template>

<style scoped>
.terms-block {
  margin-top: 16px;
}
.terms-label {
  margin-bottom: 8px;
}
.hint {
  color: #888;
  font-size: 13px;
  margin: 4px 0;
}
.file {
  margin-left: 12px;
}
.thumb {
  display: block;
  max-width: 100%;
  max-height: 240px;
  margin: 0 auto 16px;
  border: 1px solid #f0f0f0;
  border-radius: 6px;
}
.with-source {
  display: grid;
  grid-template-columns: minmax(0, 2fr) minmax(0, 3fr);
  gap: 16px;
}
.source img,
.source iframe {
  width: 100%;
  height: 560px;
  object-fit: contain;
  border: 1px solid #f0f0f0;
  border-radius: 6px;
  background: #fafafa;
}
.editor {
  min-width: 0;
}
@media (max-width: 900px) {
  .with-source {
    grid-template-columns: 1fr;
  }
  .source img,
  .source iframe {
    height: 320px;
  }
}
.warnings {
  margin: 0;
  padding-left: 18px;
}
.footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 16px;
}
</style>
