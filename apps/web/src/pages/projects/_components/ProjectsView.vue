<script setup lang="ts">
/**
 * @component ProjectsView
 * 專案搜尋：狀態（多選，顯示各狀態筆數）、時間（建立／開工／完工日）、金額（預估／成交）、客戶、負責人、只看我的、跟進狀態（待跟進／已逾期／沒有下次聯絡）。
 * 結果只包含目前資料範圍看得到的專案；條件同步到網址，方便分享或從客戶列表帶入。
 */
import { computed, reactive, ref } from 'vue';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import ClientSelect from '@/app/components/ClientSelect.vue';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import UserSelect from '@/app/components/UserSelect.vue';
import { useMe } from '@/app/composables/use-me';
import { FOLLOW_UP_FILTERS, followUpDue, type FollowUpFilter } from '@/shared/services/followup-service';
import { searchProjectsApi, type ProjectRow, type ProjectSearchQuery } from '@/shared/services/business-service';
import { dataColumns, type SortingState } from '@/shared/table/data-table';
import { formatMoney, PROJECT_STATUSES, projectStatusColor, projectStatusLabel } from '@/shared/types/business';
import ProjectFormModal from './ProjectFormModal.vue';

const { can } = useMe();
const canFollowUps = can('followups');
const initial = new URLSearchParams(window.location.search);

const filters = reactive({
  keyword: initial.get('keyword') ?? '',
  status: initial.getAll('status'),
  dateField: (initial.get('dateField') ?? 'created') as 'created' | 'start' | 'end',
  range: [initial.get('from'), initial.get('to')].every(Boolean) ? [dayjs(initial.get('from')!), dayjs(initial.get('to')!)] : ([] as Dayjs[]),
  amountField: (initial.get('amountField') ?? 'expected') as 'expected' | 'contract',
  amountMin: initial.get('amountMin') ? Number(initial.get('amountMin')) : null,
  amountMax: initial.get('amountMax') ? Number(initial.get('amountMax')) : null,
  clientId: initial.get('clientId') ?? undefined,
  ownerUserId: initial.get('ownerUserId') ?? undefined,
  mine: initial.get('mine') === 'true',
  followUp: (initial.get('followUp') ?? undefined) as FollowUpFilter | undefined,
});
const paging = reactive({ page: 1, pageSize: 20, sort: 'createdAt', desc: true });
/** 關鍵字按 Enter／搜尋才套用，避免每打一個字就查詢一次。 */
const keywordInput = ref(filters.keyword);

const query = computed<ProjectSearchQuery>(() => ({
  keyword: filters.keyword.trim() || undefined,
  status: filters.status,
  dateField: filters.dateField,
  from: filters.range[0]?.format('YYYY-MM-DD'),
  to: filters.range[1]?.format('YYYY-MM-DD'),
  amountField: filters.amountField,
  amountMin: filters.amountMin,
  amountMax: filters.amountMax,
  clientId: filters.clientId,
  ownerUserId: filters.ownerUserId,
  mine: filters.mine || undefined,
  followUp: filters.followUp,
  ...paging,
}));

const projects = useQuery({ queryKey: ['projects', query], queryFn: () => searchProjectsApi(query.value), placeholderData: keepPreviousData });

/** 條件改變時回到第 1 頁並寫回網址（不含分頁），重新整理或分享連結都能還原。 */
function onFilterChange() {
  paging.page = 1;
  const params = new URLSearchParams();
  const { page: _page, pageSize: _pageSize, sort: _sort, desc: _desc, ...rest } = query.value;
  Object.entries(rest).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return;
    if (Array.isArray(value)) value.forEach((item) => params.append(key, item));
    else if (!(key === 'dateField' && value === 'created') && !(key === 'amountField' && value === 'expected')) params.set(key, String(value));
  });
  history.replaceState(null, '', params.toString() ? `?${params}` : window.location.pathname);
}

function toggleStatus(status: string, checked: boolean) {
  filters.status = checked ? [...filters.status, status] : filters.status.filter((value) => value !== status);
  onFilterChange();
}

function reset() {
  keywordInput.value = '';
  Object.assign(filters, { keyword: '', status: [], dateField: 'created', range: [], amountField: 'expected', amountMin: null, amountMax: null, clientId: undefined, ownerUserId: undefined, mine: false, followUp: undefined });
  onFilterChange();
}

const totalCount = computed(() => Object.values(projects.data.value?.statusCounts ?? {}).reduce((sum, count) => sum + count, 0));

/** 換頁、改每頁筆數、點表頭排序：表格只回報狀態，資料由 API 重新查詢。沒有指定排序時用預設的建立日（新到舊）。 */
function onTableChange(change: TableChange) {
  paging.page = change.page;
  paging.pageSize = change.pageSize;
  paging.sort = change.sort ?? 'createdAt';
  paging.desc = change.sort ? change.desc : true;
}

const sorting = computed<SortingState>(() => [{ id: paging.sort, desc: paging.desc }]);
const col = dataColumns<ProjectRow>();
const columns = [
  col.accessor('projectNo', { header: '專案編號', enableSorting: true, meta: { width: 120 } }),
  col.accessor('name', { header: '專案名稱', enableSorting: true }),
  col.accessor((row) => row.client.name, { id: 'client', header: '客戶', meta: { width: 180 } }),
  col.accessor('status', { header: '狀態', meta: { width: 90 } }),
  ...(canFollowUps ? [col.accessor('nextFollowUpOn', { header: '下次聯絡', enableSorting: true, meta: { width: 120 } })] : []),
  col.accessor((row) => row.owner.displayName, { id: 'owner', header: '負責人', meta: { width: 100 } }),
  col.accessor('departmentName', { header: '部門', meta: { width: 100 } }),
  col.accessor('startDate', { header: '工期', enableSorting: true, meta: { width: 190 } }),
  col.accessor('expectedAmount', { header: '預估金額', enableSorting: true, meta: { width: 120, align: 'right' } }),
  col.accessor('contractAmount', { header: '成交金額', enableSorting: true, meta: { width: 120, align: 'right' } }),
  col.accessor('createdAt', { header: '建立日', enableSorting: true, meta: { width: 110 } }),
];

const formOpen = ref(false);
function onCreated(id: string) {
  window.location.assign(`/projects/detail?id=${id}`);
}
</script>

<template>
  <a-space direction="vertical" :size="16" style="width: 100%">
    <a-card :bordered="false" :body-style="{ paddingBottom: '8px' }">
      <a-row :gutter="[12, 12]">
        <a-col :xs="24" :md="8">
          <a-input-search
            v-model:value="keywordInput"
            placeholder="專案名稱、編號、客戶名稱或簡稱"
            allow-clear
            @search="() => ((filters.keyword = keywordInput), onFilterChange())"
          />
        </a-col>
        <a-col :xs="24" :md="8">
          <ClientSelect v-model:value="filters.clientId" allow-clear placeholder="客戶" @update:value="onFilterChange" />
        </a-col>
        <a-col :xs="24" :md="5">
          <UserSelect v-model:value="filters.ownerUserId" allow-clear placeholder="負責人" @change="onFilterChange" />
        </a-col>
        <a-col :xs="24" :md="3" class="mine">
          <a-checkbox v-model:checked="filters.mine" @change="onFilterChange">只看我的</a-checkbox>
        </a-col>
        <a-col :xs="24" :md="8">
          <a-input-group compact class="compact">
            <a-select v-model:value="filters.dateField" style="width: 110px" :options="[{ value: 'created', label: '建立日' }, { value: 'start', label: '開工日' }, { value: 'end', label: '完工日' }]" @change="onFilterChange" />
            <a-range-picker v-model:value="filters.range" style="width: calc(100% - 110px)" @change="onFilterChange" />
          </a-input-group>
        </a-col>
        <a-col v-if="canFollowUps" :xs="24" :md="6">
          <a-select v-model:value="filters.followUp" :options="FOLLOW_UP_FILTERS" allow-clear placeholder="跟進狀態" style="width: 100%" @change="onFilterChange" />
        </a-col>
        <a-col :xs="24" :md="7">
          <a-input-group compact class="compact">
            <a-select v-model:value="filters.amountField" style="width: 110px" :options="[{ value: 'expected', label: '預估金額' }, { value: 'contract', label: '成交金額' }]" @change="onFilterChange" />
            <a-input-number v-model:value="filters.amountMin" :min="0" placeholder="最低" style="width: calc((100% - 110px) / 2)" @change="onFilterChange" />
            <a-input-number v-model:value="filters.amountMax" :min="0" placeholder="最高" style="width: calc((100% - 110px) / 2)" @change="onFilterChange" />
          </a-input-group>
        </a-col>
        <a-col :xs="24" :md="3" class="actions">
          <a-button @click="reset">清除條件</a-button>
        </a-col>
        <a-col :span="24">
          <a-space wrap :size="[4, 8]">
            <a-checkable-tag :checked="filters.status.length === 0" @change="() => ((filters.status = []), onFilterChange())">全部 {{ totalCount }}</a-checkable-tag>
            <a-checkable-tag
              v-for="status in PROJECT_STATUSES"
              :key="status.value"
              :checked="filters.status.includes(status.value)"
              @change="(checked: boolean) => toggleStatus(status.value, checked)"
            >
              {{ status.label }} {{ projects.data.value?.statusCounts[status.value] ?? 0 }}
            </a-checkable-tag>
          </a-space>
        </a-col>
      </a-row>
    </a-card>

    <a-card :bordered="false">
      <div class="toolbar">
        <span class="summary">
          共 {{ projects.data.value?.total ?? 0 }} 筆 · 預估合計 {{ formatMoney(projects.data.value?.sumExpectedAmount) }} · 成交合計 {{ formatMoney(projects.data.value?.sumContractAmount) }}
        </span>
        <a-button v-if="can('projects', 'create')" type="primary" @click="formOpen = true">新增專案</a-button>
      </div>
      <DataTable
        :data="projects.data.value?.items ?? []"
        :columns="columns"
        row-key="id"
        :loading="projects.isFetching.value"
        :pagination="{ page: paging.page, pageSize: paging.pageSize, total: projects.data.value?.total ?? 0, showSizeChanger: true }"
        :sorting="sorting"
        :scroll-x="1420"
        @change="onTableChange"
      >
        <template #cell="{ columnId, record }">
          <template v-if="columnId === 'projectNo'">
            <a :href="`/projects/detail?id=${record.id}`">{{ record.projectNo }}</a>
          </template>
          <template v-else-if="columnId === 'name'">
            <a :href="`/projects/detail?id=${record.id}`">{{ record.name }}</a>
          </template>
          <template v-else-if="columnId === 'status'">
            <a-tag :color="projectStatusColor(record.status)">{{ projectStatusLabel(record.status) }}</a-tag>
          </template>
          <template v-else-if="columnId === 'nextFollowUpOn'">
            <template v-if="record.nextFollowUpOn">
              <span :class="['due', followUpDue(record.nextFollowUpOn, record.status).state]">{{ record.nextFollowUpOn }}</span>
              <a-tag v-if="followUpDue(record.nextFollowUpOn, record.status).state === 'today'" color="orange" class="today">今天</a-tag>
            </template>
            <span v-else class="muted">—</span>
          </template>
          <template v-else-if="columnId === 'startDate'">
            <span v-if="record.startDate || record.endDate">{{ record.startDate ?? '?' }} ~ {{ record.endDate ?? '?' }}</span>
            <span v-else class="muted">—</span>
          </template>
          <template v-else-if="columnId === 'expectedAmount' || columnId === 'contractAmount'">
            {{ formatMoney(record[columnId as 'expectedAmount' | 'contractAmount']) }}
          </template>
          <template v-else-if="columnId === 'createdAt'">{{ dayjs(record.createdAt).format('YYYY-MM-DD') }}</template>
        </template>
      </DataTable>
    </a-card>
  </a-space>

  <ProjectFormModal :open="formOpen" :default-client-id="filters.clientId" @close="formOpen = false" @saved="onCreated" />
</template>

<style scoped>
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}
.summary {
  color: #555;
}
.muted {
  color: #bbb;
}
.due.overdue {
  color: #cf1322;
  font-weight: 500;
}
.due.inactive {
  color: #bbb;
}
.today {
  margin-left: 4px;
}
.compact {
  display: flex !important;
}
.mine,
.actions {
  display: flex;
  align-items: center;
}
</style>
