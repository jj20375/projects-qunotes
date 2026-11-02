<script setup lang="ts">
/**
 * @component ClientImportModal
 * 客戶 Excel 匯入：先預覽每一列會新增、更新或錯誤（不寫入），確認後才正式匯入。
 * 欄位格式與「匯出」相同，建議先匯出一份當範本。
 */
import { computed, ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import DataTable from '@/app/components/DataTable.vue';
import { importClientsApi, type ClientImportResult } from '@/shared/services/quote-service';
import { dataColumns } from '@/shared/table/data-table';

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ close: []; imported: [] }>();

const file = ref<File | null>(null);
const preview = ref<ClientImportResult | null>(null);
const col = dataColumns<ClientImportResult['rows'][number]>();
const columns = [
  col.accessor('row', { header: '列', meta: { width: 60 } }),
  col.accessor('action', { header: '動作', meta: { width: 80 } }),
  col.accessor('code', { header: '代碼', meta: { width: 100 } }),
  col.accessor('name', { header: '客戶名稱' }),
  col.accessor('message', { header: '說明' }),
];
const busy = ref(false);

watch(
  () => props.open,
  (open) => {
    if (open) {
      file.value = null;
      preview.value = null;
    }
  },
);

async function run(dryRun: boolean) {
  if (!file.value) {
    message.warning('請選擇 Excel 檔案');
    return;
  }
  busy.value = true;
  try {
    const result = await importClientsApi(file.value, dryRun);
    if (dryRun) {
      preview.value = result;
    } else {
      message.success(`匯入完成：新增 ${result.summary.created} 筆、更新 ${result.summary.updated} 筆${result.summary.errors ? `，${result.summary.errors} 筆未匯入` : ''}`);
      emit('imported');
      emit('close');
    }
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    busy.value = false;
  }
}

const actionLabel = { create: '新增', update: '更新', error: '錯誤' } as const;
const actionColor = { create: 'green', update: 'blue', error: 'red' } as const;
const importable = computed(() => (preview.value ? preview.value.summary.created + preview.value.summary.updated : 0));
</script>

<template>
  <a-modal :open="open" title="匯入客戶" :width="760" :footer="null" @cancel="emit('close')">
    <p class="hint">欄位與「匯出」的檔案相同；有客戶代碼時以代碼比對，否則以統一編號比對，比對不到就新增。</p>
    <a-space>
      <a-upload :before-upload="(selected: File) => ((file = selected), (preview = null), false)" :max-count="1" accept=".xlsx" :show-upload-list="false">
        <a-button>選擇檔案</a-button>
      </a-upload>
      <span v-if="file">{{ file.name }}</span>
      <a-button type="primary" :disabled="!file" :loading="busy && !preview" @click="run(true)">預覽</a-button>
    </a-space>

    <template v-if="preview">
      <a-alert
        class="summary"
        :type="preview.summary.errors ? 'warning' : 'success'"
        show-icon
        :message="`預計新增 ${preview.summary.created} 筆、更新 ${preview.summary.updated} 筆${preview.summary.errors ? `；${preview.summary.errors} 筆有錯誤，不會匯入` : ''}`"
      />
      <!-- 匯入檔的預覽整份已在瀏覽器，所以在前端切頁（client-page-size）；一般列表不可這樣做。 -->
      <DataTable :data="preview.rows" :columns="columns" row-key="row" size="small" :scroll-y="320" :client-page-size="50">
        <template #cell="{ columnId, record }">
          <template v-if="columnId === 'action'">
            <a-tag :color="actionColor[record.action as keyof typeof actionColor]">{{ actionLabel[record.action as keyof typeof actionLabel] }}</a-tag>
          </template>
        </template>
      </DataTable>
      <div class="footer">
        <a-button type="primary" :disabled="!importable" :loading="busy" @click="run(false)">匯入 {{ importable }} 筆</a-button>
      </div>
    </template>
  </a-modal>
</template>

<style scoped>
.hint {
  color: #888;
}
.summary {
  margin: 16px 0 12px;
}
.footer {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
}
</style>
