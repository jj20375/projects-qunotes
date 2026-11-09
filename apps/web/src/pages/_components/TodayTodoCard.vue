<script setup lang="ts">
/**
 * @component TodayTodoCard
 * 首頁「今日待辦」：下次聯絡日已逾期、今天或 7 天內到期的專案。預設只看自己的
 * （自己負責，或排定下次聯絡的人是自己），可切換成資料範圍內全部。區間、範圍與分頁都由後端處理。
 */
import { computed, reactive, watch } from 'vue';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { FOLLOW_UP_TODO_RANGES, followUpDue, listFollowUpTodoApi, type FollowUpTodoRow } from '@/shared/services/followup-service';
import { dataColumns } from '@/shared/table/data-table';
import { projectStatusColor, projectStatusLabel } from '@/shared/types/business';

const query = reactive({ range: 'overdue' as 'overdue' | 'today' | 'upcoming', mine: true, page: 1, pageSize: 5 });
const list = useQuery({ queryKey: ['followup-todo', query], queryFn: () => listFollowUpTodoApi({ ...query }), placeholderData: keepPreviousData });
const counts = computed(() => list.data.value?.counts ?? { overdue: 0, today: 0, upcoming: 0 });

// 第一次載入時，如果逾期沒有案子就停在有案子的第一個區間，使用者不必先看到空白清單。
let picked = false;
watch(
  () => list.data.value,
  (data) => {
    if (!data || picked) return;
    picked = true;
    const first = FOLLOW_UP_TODO_RANGES.find((range) => data.counts[range.value] > 0);
    if (first && first.value !== query.range) query.range = first.value;
  },
);

function onRangeChange() {
  query.page = 1;
}

function onTableChange(change: TableChange) {
  query.page = change.page;
  query.pageSize = change.pageSize;
}

/** 與今天的距離，用來顯示「逾期 3 天」「明天」。 */
function dueText(row: FollowUpTodoRow) {
  const due = followUpDue(row.nextFollowUpOn, row.status);
  if (due.state === 'overdue') return `逾期 ${-due.days} 天`;
  if (due.state === 'today') return '今天';
  return due.days === 1 ? '明天' : `${due.days} 天後`;
}

const col = dataColumns<FollowUpTodoRow>();
const columns = [
  col.accessor('nextFollowUpOn', { header: '下次聯絡', meta: { width: 150 } }),
  col.accessor('name', { header: '專案' }),
  col.accessor('clientName', { header: '客戶', meta: { width: 140 } }),
  col.accessor('nextAction', { header: '下次要做的事' }),
  col.accessor((row) => row.owner.displayName ?? '', { id: 'owner', header: '負責人', meta: { width: 100 } }),
  col.display({ id: 'ops', header: '', meta: { width: 90 } }),
];
</script>

<template>
  <a-card title="今日待辦" :bordered="false" class="todo">
    <template #extra>
      <a-space>
        <span class="muted">只看我的</span>
        <a-switch v-model:checked="query.mine" size="small" @change="onRangeChange" />
      </a-space>
    </template>
    <a-radio-group v-model:value="query.range" button-style="solid" class="ranges" @change="onRangeChange">
      <a-radio-button v-for="range in FOLLOW_UP_TODO_RANGES" :key="range.value" :value="range.value">
        {{ range.label }}
        <a-badge :count="counts[range.value]" :number-style="{ backgroundColor: range.value === 'overdue' ? '#cf1322' : '#8c8c8c', boxShadow: 'none' }" :offset="[6, -2]" show-zero />
      </a-radio-button>
    </a-radio-group>
    <DataTable
      :data="list.data.value?.items ?? []"
      :columns="columns"
      row-key="id"
      size="small"
      :loading="list.isFetching.value"
      :pagination="{ page: query.page, pageSize: query.pageSize, total: list.data.value?.total ?? 0 }"
      :scroll-x="760"
      @change="onTableChange"
    >
      <template #cell="{ columnId, record }">
        <template v-if="columnId === 'nextFollowUpOn'">
          <span :class="['due', followUpDue(record.nextFollowUpOn, record.status).state]">{{ record.nextFollowUpOn }}</span>
          <div class="muted small">{{ dueText(record) }}</div>
        </template>
        <template v-else-if="columnId === 'name'">
          <a :href="`/projects/detail?id=${record.id}&tab=followups`">{{ record.projectNo }} {{ record.name }}</a>
          <a-tag :color="projectStatusColor(record.status)" class="status">{{ projectStatusLabel(record.status) }}</a-tag>
        </template>
        <template v-else-if="columnId === 'nextAction'">{{ record.nextAction ?? '—' }}</template>
        <template v-else-if="columnId === 'ops'">
          <a :href="`/projects/detail?id=${record.id}&tab=followups`">寫跟進</a>
        </template>
      </template>
      <template #empty>
        <a-empty :description="query.mine ? '這個區間沒有需要跟進的案子' : '資料範圍內這個區間沒有需要跟進的案子'" />
      </template>
    </DataTable>
  </a-card>
</template>

<style scoped>
.muted {
  color: #888;
}
.small {
  font-size: 12px;
}
.ranges {
  margin-bottom: 12px;
}
.due.overdue {
  color: #cf1322;
  font-weight: 500;
}
.status {
  margin-left: 6px;
}
</style>
