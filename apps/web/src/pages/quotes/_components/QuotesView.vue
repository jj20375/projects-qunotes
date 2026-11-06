<script setup lang="ts">
/**
 * @component QuotesView
 * 報價單列表：只列出目前資料範圍內專案的報價；預設只看最新版本。
 */
import { computed, reactive, ref } from 'vue';
import type { Dayjs } from 'dayjs';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { useMe } from '@/app/composables/use-me';
import { searchQuotesApi, type QuoteRow, type QuoteSearchQuery } from '@/shared/services/quote-service';
import { dataColumns } from '@/shared/table/data-table';
import { formatMoney, QUOTE_STATUSES, quoteNumber, quoteStatusColor, quoteStatusLabel } from '@/shared/types/business';
import QuoteImportModal from './QuoteImportModal.vue';

const { canOption } = useMe();
const filters = reactive({ keyword: '', status: [] as string[], range: [] as Dayjs[], amountMin: null as number | null, amountMax: null as number | null, latestOnly: true });
const keywordInput = ref('');
const paging = reactive({ page: 1, pageSize: 20 });
const query = computed<QuoteSearchQuery>(() => ({
  keyword: filters.keyword || undefined,
  status: filters.status,
  from: filters.range[0]?.format('YYYY-MM-DD'),
  to: filters.range[1]?.format('YYYY-MM-DD'),
  amountMin: filters.amountMin,
  amountMax: filters.amountMax,
  latestOnly: filters.latestOnly,
  ...paging,
}));
const quotes = useQuery({ queryKey: ['quotes', query], queryFn: () => searchQuotesApi(query.value), placeholderData: keepPreviousData });
const importOpen = ref(false);
const canImport = computed(() => canOption('quotes', 'import_excel') || canOption('quotes', 'import_legacy') || canOption('quotes', 'import_ocr'));

const col = dataColumns<QuoteRow>();
const columns = [
  col.accessor('quoteNo', { header: '報價單號', meta: { width: 160 } }),
  col.accessor('projectName', { header: '專案' }),
  col.accessor('clientName', { header: '客戶', meta: { width: 200 } }),
  col.accessor('status', { header: '狀態', meta: { width: 120 } }),
  col.accessor('quoteDate', { header: '報價日期', meta: { width: 120 } }),
  col.accessor('validUntil', { header: '有效期限', meta: { width: 120 } }),
  col.accessor('total', { header: '總計', meta: { width: 130, align: 'right' } }),
];

function onTableChange(change: TableChange) {
  paging.page = change.page;
  paging.pageSize = change.pageSize;
}
</script>

<template>
  <a-card :bordered="false">
    <div class="toolbar">
      <a-space wrap>
        <a-input-search v-model:value="keywordInput" placeholder="報價單號、客戶、案名" allow-clear style="width: 220px" @search="() => ((filters.keyword = keywordInput), (paging.page = 1))" />
        <a-select
          v-model:value="filters.status"
          mode="multiple"
          allow-clear
          placeholder="狀態"
          style="min-width: 200px"
          :options="QUOTE_STATUSES.map((s) => ({ value: s.value, label: s.label }))"
          @change="paging.page = 1"
        />
        <a-range-picker v-model:value="filters.range" :placeholder="['報價日期起', '迄']" @change="paging.page = 1" />
        <a-input-number v-model:value="filters.amountMin" :min="0" placeholder="總計最低" style="width: 120px" @change="paging.page = 1" />
        <a-input-number v-model:value="filters.amountMax" :min="0" placeholder="總計最高" style="width: 120px" @change="paging.page = 1" />
        <a-checkbox v-model:checked="filters.latestOnly" @change="paging.page = 1">只看最新版本</a-checkbox>
      </a-space>
      <a-space>
        <a-button v-if="canImport" @click="importOpen = true">匯入報價</a-button>
        <a-tooltip title="報價單從專案頁新增"><a-button type="primary" href="/projects">到專案新增報價</a-button></a-tooltip>
      </a-space>
    </div>
    <DataTable
      :data="quotes.data.value?.items ?? []"
      :columns="columns"
      row-key="id"
      :loading="quotes.isFetching.value"
      :pagination="{ page: paging.page, pageSize: paging.pageSize, total: quotes.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 筆` }"
      :scroll-x="1060"
      @change="onTableChange"
    >
      <template #cell="{ columnId, record }">
        <template v-if="columnId === 'quoteNo'">
          <a :href="`/quotes/edit?id=${record.id}`">{{ quoteNumber(record) }}</a>
        </template>
        <template v-else-if="columnId === 'projectName'">
          <a :href="`/projects/detail?id=${record.projectId}`">{{ record.projectName }}</a>
          <div class="muted">{{ record.projectNo }}{{ record.title && record.title !== record.projectName ? ` · ${record.title}` : '' }}</div>
        </template>
        <template v-else-if="columnId === 'status'">
          <a-tag :color="quoteStatusColor(record.status)">{{ quoteStatusLabel(record.status) }}</a-tag>
          <a-tag v-if="record.isExpired" color="red">逾期</a-tag>
        </template>
        <template v-else-if="columnId === 'total'">{{ formatMoney(record.total) }}</template>
      </template>
    </DataTable>
  </a-card>
  <QuoteImportModal :open="importOpen" @close="importOpen = false" />
</template>

<style scoped>
.toolbar {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}
.muted {
  color: #999;
  font-size: 12px;
}
</style>
