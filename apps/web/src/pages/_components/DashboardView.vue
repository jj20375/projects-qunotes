<script setup lang="ts">
/**
 * @component DashboardView
 * 登入後首頁：目前身分與工作範圍；公司範圍且有 followups:read 時顯示今日待辦、有 dashboard:read 時顯示戰情 Dashboard，
 * 平台範圍顯示常用功能。
 */
import { computed, defineAsyncComponent } from 'vue';
import DataTable from '@/app/components/DataTable.vue';
import { useMe } from '@/app/composables/use-me';
import { routeManifest } from '@/app/route-manifest';
import { dataColumns } from '@/shared/table/data-table';
import { ACTION_LABELS, DATA_SCOPE_LABELS, meetsRequirement, type PermissionGrant } from '@/shared/types/permissions';

const col = dataColumns<PermissionGrant>();
const grantColumns = [
  col.accessor('code', { header: '資源' }),
  col.accessor('actions', { header: '動作' }),
  col.accessor('options', { header: '子功能' }),
];

const CompanyDashboard = defineAsyncComponent(() => import('./CompanyDashboard.vue'));
const TodayTodoCard = defineAsyncComponent(() => import('./TodayTodoCard.vue'));
const { me, can } = useMe();
const showDashboard = computed(() => scope.value.kind === 'company' && can('dashboard'));
// 今日待辦只看跟進紀錄權限，不需要戰情 Dashboard 的權限。
const showTodo = computed(() => scope.value.kind === 'company' && can('followups', 'read'));
const scope = computed(() => me.value!.scope);
const shortcuts = computed(() =>
  routeManifest.filter(
    (route) => route.key !== 'dashboard' && !route.menuPath && (!route.scope || route.scope === scope.value.kind) && (!route.requirement || meetsRequirement(me.value!.grants, route.requirement)),
  ),
);
</script>

<template>
  <a-space direction="vertical" :size="16" style="width: 100%">
    <a-card :bordered="false">
      <h2 class="greeting">{{ me!.user.displayName }}，您好</h2>
      <a-descriptions :column="{ xs: 1, sm: 2, lg: 4 }" size="small">
        <a-descriptions-item label="工作範圍">{{ scope.kind === 'platform' ? '平台管理' : scope.companyName }}</a-descriptions-item>
        <a-descriptions-item v-if="scope.kind === 'company'" label="部門">{{ scope.departmentName ?? '—' }}</a-descriptions-item>
        <a-descriptions-item v-if="scope.kind === 'company'" label="資料範圍">{{ DATA_SCOPE_LABELS[scope.dataScope] }}</a-descriptions-item>
        <a-descriptions-item v-if="scope.isRootAdmin" label="身分"><a-tag color="gold">主管理員</a-tag></a-descriptions-item>
      </a-descriptions>
    </a-card>

    <TodayTodoCard v-if="showTodo" />

    <CompanyDashboard v-if="showDashboard" />

    <a-card v-if="shortcuts.length && !showDashboard" title="常用功能" :bordered="false">
      <a-space wrap>
        <a-button v-for="route in shortcuts" :key="route.key" :href="route.path">{{ route.title }}</a-button>
      </a-space>
    </a-card>

    <a-collapse :bordered="false" ghost>
      <a-collapse-panel key="grants" header="我的有效權限">
      <DataTable :data="me!.grants" :columns="grantColumns" row-key="code" size="small" :scroll-x="560">
        <template #cell="{ columnId, record }">
          <template v-if="columnId === 'actions'">
            <a-tag v-for="action in record.actions" :key="action">{{ ACTION_LABELS[action] }}</a-tag>
          </template>
          <template v-else-if="columnId === 'options'">
            <a-tag v-for="option in record.options" :key="option" color="blue">{{ option }}</a-tag>
          </template>
        </template>
      </DataTable>
      </a-collapse-panel>
    </a-collapse>
  </a-space>
</template>

<style scoped>
.greeting {
  margin: 0 0 12px;
  font-size: 20px;
}
</style>
