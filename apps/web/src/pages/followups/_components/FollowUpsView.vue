<script setup lang="ts">
/**
 * @component FollowUpsView
 * 跨專案跟進紀錄總表：列出目前資料範圍內所有專案的紀錄，主管可一次看完部屬的跟進。
 * 篩選、排序、分頁全部送給 API；這頁只讀，修改請點專案連到該專案的「跟進紀錄」頁籤。
 */
import { computed, reactive, ref } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import UserSelect from '@/app/components/UserSelect.vue';
import { useMe } from '@/app/composables/use-me';
import { listDirectoryDepartmentsApi } from '@/shared/services/business-service';
import {
  FOLLOW_UP_TYPES,
  followUpDue,
  followUpTypeLabel,
  listFollowUpsApi,
  type FollowUpRow,
  type FollowUpSearchQuery,
} from '@/shared/services/followup-service';
import { dataColumns, type SortingState } from '@/shared/table/data-table';
import { DATA_SCOPE_LABELS } from '@/shared/types/permissions';
import { withBasePath } from '@/shared/utils/base-path';

const { me } = useMe();
const filters = reactive({
  keyword: '',
  type: undefined as string | undefined,
  createdBy: undefined as string | undefined,
  departmentId: undefined as string | undefined,
  range: [] as Dayjs[],
});
const keywordInput = ref('');
const paging = reactive({ page: 1, pageSize: 20, sort: 'occurredAt' as 'occurredAt' | 'createdAt', desc: true });

const query = computed<FollowUpSearchQuery>(() => ({
  keyword: filters.keyword || undefined,
  type: filters.type,
  createdBy: filters.createdBy,
  departmentId: filters.departmentId,
  from: filters.range[0]?.format('YYYY-MM-DD'),
  to: filters.range[1]?.format('YYYY-MM-DD'),
  ...paging,
}));
const list = useQuery({ queryKey: ['followups-all', query], queryFn: () => listFollowUpsApi(query.value), placeholderData: keepPreviousData });

// 只有部門樹與全公司範圍才有其他部門可選；部門樹是已完整載入的小型樹狀結構，前端只組裝顯示。
const canFilterDepartment = computed(() => ['company', 'department_tree'].includes(me.value?.scope.dataScope ?? ''));
const departments = useQuery({ queryKey: ['directory-departments'], queryFn: listDirectoryDepartmentsApi, enabled: canFilterDepartment });
const departmentTree = computed(() => {
  const rows = departments.data.value ?? [];
  const build = (parentId: string | null): unknown[] => rows.filter((row) => row.parentId === parentId).map((row) => ({ value: row.id, title: row.name, children: build(row.id) }));
  return build(null);
});

function onFilterChange() {
  paging.page = 1;
}

function search() {
  filters.keyword = keywordInput.value.trim();
  onFilterChange();
}

function reset() {
  keywordInput.value = '';
  Object.assign(filters, { keyword: '', type: undefined, createdBy: undefined, departmentId: undefined, range: [] });
  onFilterChange();
}

function onTableChange(change: TableChange) {
  paging.page = change.page;
  paging.pageSize = change.pageSize;
  paging.sort = change.sort === 'createdAt' ? 'createdAt' : 'occurredAt';
  paging.desc = change.sort ? change.desc : true;
}

/** 這筆的下次聯絡是不是專案目前的那一筆（只有它才需要標示逾期）。 */
function isCurrent(row: FollowUpRow) {
  return !!row.nextFollowUpOn && !row.nextDismissedAt && row.project?.nextFollowUpOn === row.nextFollowUpOn;
}

const sorting = computed<SortingState>(() => [{ id: paging.sort, desc: paging.desc }]);
const col = dataColumns<FollowUpRow>();
const columns = [
  col.accessor('occurredAt', { header: '發生時間', enableSorting: true, meta: { width: 140 } }),
  col.accessor('type', { header: '類型', meta: { width: 80 } }),
  col.accessor((row) => row.project?.name ?? '', { id: 'project', header: '專案', meta: { width: 220 } }),
  col.accessor('clientName', { header: '客戶', meta: { width: 160 } }),
  col.accessor('contactName', { header: '對象', meta: { width: 100 } }),
  col.accessor('summary', { header: '主旨' }),
  col.accessor('nextFollowUpOn', { header: '下次聯絡', meta: { width: 120 } }),
  col.accessor((row) => row.createdBy.displayName ?? '', { id: 'createdBy', header: '撰寫人', meta: { width: 100 } }),
  col.accessor('createdAt', { header: '建立時間', enableSorting: true, meta: { width: 140 } }),
];
</script>

<template>
  <a-space direction="vertical" :size="16" style="width: 100%">
    <a-card :bordered="false" :body-style="{ paddingBottom: '8px' }">
      <a-row :gutter="[12, 12]">
        <a-col :xs="24" :md="8">
          <a-input-search v-model:value="keywordInput" placeholder="主旨、內容、對象、專案、客戶名稱或簡稱" allow-clear @search="search" />
        </a-col>
        <a-col :xs="24" :md="4">
          <a-select v-model:value="filters.type" :options="FOLLOW_UP_TYPES" allow-clear placeholder="全部類型" style="width: 100%" @change="onFilterChange" />
        </a-col>
        <a-col :xs="24" :md="4">
          <UserSelect v-model:value="filters.createdBy" allow-clear placeholder="撰寫人" @change="onFilterChange" />
        </a-col>
        <a-col v-if="canFilterDepartment" :xs="24" :md="4">
          <a-tree-select
            v-model:value="filters.departmentId"
            :tree-data="departmentTree"
            allow-clear
            tree-default-expand-all
            placeholder="全部部門"
            style="width: 100%"
            @change="onFilterChange"
          />
        </a-col>
        <a-col :xs="24" :md="4">
          <a-range-picker v-model:value="filters.range" style="width: 100%" @change="onFilterChange" />
        </a-col>
        <a-col :span="24" class="bar">
          <span class="muted">資料範圍：{{ DATA_SCOPE_LABELS[me?.scope.dataScope ?? 'own'] }}（依專案所屬部門）</span>
          <a-button @click="reset">清除條件</a-button>
        </a-col>
      </a-row>
    </a-card>

    <a-card :bordered="false">
      <a-alert
        v-if="list.isError.value"
        type="error"
        show-icon
        style="margin-bottom: 12px"
        :message="(list.error.value as Error | null)?.message || '跟進紀錄載入失敗'"
      >
        <template #action>
          <a-button size="small" @click="list.refetch()">重新載入</a-button>
        </template>
      </a-alert>
      <DataTable
        :data="list.data.value?.items ?? []"
        :columns="columns"
        row-key="id"
        :loading="list.isFetching.value"
        :pagination="{ page: paging.page, pageSize: paging.pageSize, total: list.data.value?.total ?? 0, showSizeChanger: true }"
        :sorting="sorting"
        :scroll-x="1300"
        @change="onTableChange"
      >
        <template #cell="{ columnId, record }">
          <template v-if="columnId === 'occurredAt'">{{ dayjs(record.occurredAt).format('YYYY-MM-DD HH:mm') }}</template>
          <template v-else-if="columnId === 'createdAt'">{{ dayjs(record.createdAt).format('YYYY-MM-DD HH:mm') }}</template>
          <template v-else-if="columnId === 'type'">{{ followUpTypeLabel(record.type) }}</template>
          <template v-else-if="columnId === 'project'">
            <a v-if="record.project" :href="withBasePath(`/projects/detail?id=${record.project.id}&tab=followups`)">{{ record.project.projectNo }} {{ record.project.name }}</a>
            <span v-else class="muted">—</span>
          </template>
          <template v-else-if="columnId === 'nextFollowUpOn'">
            <span v-if="record.nextFollowUpOn" :class="['due', isCurrent(record) ? followUpDue(record.nextFollowUpOn, record.project?.status).state : 'inactive']">
              {{ record.nextFollowUpOn }}
            </span>
            <span v-else class="muted">—</span>
          </template>
        </template>
        <template #expanded="{ record }">
          <div class="content">{{ record.content ?? '（沒有內容）' }}</div>
          <div v-if="record.nextAction" class="muted">下次要做的事：{{ record.nextAction }}</div>
        </template>
      </DataTable>
    </a-card>
  </a-space>
</template>

<style scoped>
.bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.muted {
  color: #888;
}
.due.overdue {
  color: #cf1322;
  font-weight: 500;
}
.due.inactive {
  color: #bbb;
}
.content {
  white-space: pre-wrap;
}
</style>
