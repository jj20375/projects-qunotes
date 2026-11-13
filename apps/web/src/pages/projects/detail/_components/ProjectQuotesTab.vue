<script setup lang="ts">
/**
 * @component ProjectQuotesTab
 * 專案底下的報價單：同一報價單號的各版本收合在一起，最新版本在最上面。
 */
import { computed, reactive, ref } from 'vue';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { dataColumns } from '@/shared/table/data-table';
import { useMe } from '@/app/composables/use-me';
import type { ProjectDetail } from '@/shared/services/business-service';
import { listProjectQuotesApi, type QuoteRow } from '@/shared/services/quote-service';
import { formatMoney, quoteNumber, quoteStatusColor, quoteStatusLabel } from '@/shared/types/business';
import QuoteImportModal from '../../../quotes/_components/QuoteImportModal.vue';
import { withBasePath } from '@/shared/utils/base-path';

const props = defineProps<{ project: ProjectDetail }>();
const { can, canOption } = useMe();
// 搜尋與分頁由後端處理：換頁、換每頁筆數都會重新向 API 取資料。
const paging = reactive({ page: 1, pageSize: 20 });
const quotes = useQuery({
  queryKey: ['project-quotes', props.project.id, paging],
  queryFn: () => listProjectQuotesApi(props.project.id, { ...paging }),
  placeholderData: keepPreviousData,
});
function onTableChange(change: TableChange) {
  Object.assign(paging, { page: change.page, pageSize: change.pageSize });
}
const importOpen = ref(false);

/**
 * 同一報價單號的各版本收合成一組（只是顯示結構）：排序由後端處理，
 * API 已依單號、版次由新到舊排好，所以每組第一筆就是最新版本。
 */
interface QuoteGroup extends QuoteRow {
  /** 同一單號的舊版本，收在最新版本底下。 */
  children?: QuoteRow[];
}

const rows = computed<QuoteGroup[]>(() => {
  const groups = new Map<string, QuoteRow[]>();
  (quotes.data.value?.items ?? []).forEach((quote) => groups.set(quote.quoteNo, [...(groups.get(quote.quoteNo) ?? []), quote]));
  return [...groups.values()].map(([latest, ...older]) => (older.length ? { ...latest, children: older } : latest));
});

const canCreate = computed(() => can('quotes', 'create') && props.project.canWrite && !['closed', 'cancelled'].includes(props.project.status));
const canImport = computed(() => canCreate.value && (canOption('quotes', 'import_excel') || canOption('quotes', 'import_legacy') || canOption('quotes', 'import_ocr')));
const col = dataColumns<QuoteGroup>();
const columns = [
  col.accessor('quoteNo', { header: '報價單號', meta: { width: 200 } }),
  col.accessor('title', { header: '案名' }),
  col.accessor('status', { header: '狀態', meta: { width: 130 } }),
  col.accessor('quoteDate', { header: '報價日期', meta: { width: 110 } }),
  col.accessor('netAmount', { header: '未稅', meta: { width: 120, align: 'right' } }),
  col.accessor('total', { header: '總計', meta: { width: 120, align: 'right' } }),
];
</script>

<template>
  <div class="toolbar">
    <a-space>
      <a-button v-if="canImport" @click="importOpen = true">匯入報價</a-button>
      <a-button v-if="canCreate" type="primary" :href="withBasePath(`/quotes/edit?projectId=${project.id}`)">新增報價單</a-button>
    </a-space>
  </div>
  <DataTable
    :data="rows"
    :columns="columns"
    row-key="id"
    size="small"
    :loading="quotes.isFetching.value"
    :pagination="{ page: paging.page, pageSize: paging.pageSize, total: quotes.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 筆（含舊版本）` }"
    :scroll-x="760"
    :get-sub-rows="(row: QuoteGroup) => row.children as QuoteGroup[] | undefined"
    @change="onTableChange"
  >
    <template #cell="{ columnId, record }">
      <template v-if="columnId === 'quoteNo'">
        <a :href="withBasePath(`/quotes/edit?id=${record.id}`)">{{ quoteNumber(record) }}</a>
      </template>
      <template v-else-if="columnId === 'status'">
        <a-tag :color="quoteStatusColor(record.status)">{{ quoteStatusLabel(record.status) }}</a-tag>
        <a-tag v-if="record.isExpired" color="red">逾期</a-tag>
      </template>
      <template v-else-if="columnId === 'netAmount' || columnId === 'total'">{{ formatMoney(record[columnId as 'total']) }}</template>
    </template>
    <template #empty><a-empty description="還沒有報價單" /></template>
  </DataTable>
  <QuoteImportModal :open="importOpen" :project-id="project.id" :project-label="`${project.projectNo} ${project.name}`" @close="importOpen = false" />
</template>

<style scoped>
.toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 12px;
}
</style>
