<script setup lang="ts">
/**
 * @component CompanyDashboard
 * 戰情 Dashboard（公司範圍）：業績 KPI、月趨勢（今年 vs 去年）、季度、案件數與成交率、業務排行、到期保固、應收帳款。
 * 數字依登入者的資料範圍計算；沒有「檢視金額」權限時只顯示件數。
 */
import { computed, reactive, ref, watch } from 'vue';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import DataTable from '@/app/components/DataTable.vue';
import EChart from '@/app/components/EChart.vue';
import UserSelect from '@/app/components/UserSelect.vue';
import { useMe } from '@/app/composables/use-me';
import { listDirectoryDepartmentsApi } from '@/shared/services/business-service';
import {
  getDashboardApi,
  listDashboardExpiringWarrantiesApi,
  listDashboardReceivablesApi,
  type DashboardMetric,
  type DashboardReceivable,
  type DashboardWarranty,
} from '@/shared/services/dashboard-service';
import { dataColumns } from '@/shared/table/data-table';
import { PROJECT_STATUSES } from '@/shared/types/business';
import { DATA_SCOPE_LABELS } from '@/shared/types/permissions';
import { baseOption, categoryAxis, fullMoney, SERIES, TEXT_PRIMARY, valueAxis } from '@/shared/utils/chart-theme';
import { withBasePath } from '@/shared/utils/base-path';

const { me, canOption } = useMe();
const filters = reactive({ year: undefined as number | undefined, metric: 'accepted' as DashboardMetric, departmentId: undefined as string | undefined, ownerUserId: undefined as string | undefined });
const summary = useQuery({ queryKey: ['dashboard', filters], queryFn: () => getDashboardApi({ ...filters }), placeholderData: keepPreviousData });
const data = computed(() => summary.data.value);

// 兩張清單各自分頁，部門、負責業務篩選變動時回到第一頁。
const receivablePaging = reactive({ page: 1, pageSize: 10 });
const warrantyPaging = reactive({ page: 1, pageSize: 10 });
watch(
  () => [filters.departmentId, filters.ownerUserId],
  () => {
    receivablePaging.page = 1;
    warrantyPaging.page = 1;
  },
);
const listFilter = computed(() => ({ departmentId: filters.departmentId, ownerUserId: filters.ownerUserId }));
const receivables = useQuery({
  queryKey: ['dashboard-receivables', listFilter, receivablePaging],
  queryFn: () => listDashboardReceivablesApi({ ...listFilter.value, ...receivablePaging }),
  enabled: computed(() => !!data.value?.receivables),
  placeholderData: keepPreviousData,
});
const warranties = useQuery({
  queryKey: ['dashboard-warranties', listFilter, warrantyPaging],
  queryFn: () => listDashboardExpiringWarrantiesApi({ ...listFilter.value, ...warrantyPaging }),
  enabled: computed(() => !!data.value?.expiringWarranties),
  placeholderData: keepPreviousData,
});

const showAllStaff = computed(() => canOption('dashboard', 'view_all_staff'));
const canFilterDepartment = computed(() => ['company', 'department_tree'].includes(me.value?.scope.dataScope ?? ''));
const departments = useQuery({ queryKey: ['directory-departments'], queryFn: listDirectoryDepartmentsApi, enabled: canFilterDepartment });
const departmentTree = computed(() => {
  const rows = departments.data.value ?? [];
  const build = (parentId: string | null): unknown[] => rows.filter((row) => row.parentId === parentId).map((row) => ({ value: row.id, title: row.name, children: build(row.id) }));
  return build(null);
});

const metricOptions = [
  { value: 'accepted', label: '報價接受' },
  { value: 'orders', label: '接單' },
  { value: 'invoiced', label: '開票' },
];
const metricLabel = computed(() => metricOptions.find((option) => option.value === (data.value?.metric ?? filters.metric))!.label);
const yearOptions = computed(() => {
  const current = data.value?.isCurrentYear ? data.value.fiscalYear : new Date().getFullYear();
  return [0, 1, 2, 3].map((offset) => ({ value: current - offset, label: `${current - offset} 年度` }));
});
const fiscalNote = computed(() => (data.value && data.value.fiscalYearStartMonth !== 1 ? `（會計年度自 ${data.value.fiscalYearStartMonth} 月起）` : ''));
const monthNames = computed(() => (data.value?.monthLabels ?? []).map((label) => `${Number(label.slice(5))}月`));
const showTable = ref(false);

// ---- 表格檢視與兩張清單：資料都是後端算好、排好的，這裡只負責顯示 ----
interface MonthRow {
  month: string;
  thisYear: number | null;
  lastYear: number | null;
}
type ReceivableRow = DashboardReceivable;
type WarrantyRow = DashboardWarranty;

const monthRows = computed<MonthRow[]>(() => monthNames.value.map((month, index) => ({ month, thisYear: data.value?.trend.thisYear[index] ?? null, lastYear: data.value?.trend.lastYear[index] ?? null })));
const monthCol = dataColumns<MonthRow>();
const monthColumns = computed(() => [
  monthCol.accessor('month', { header: '月份' }),
  monthCol.accessor('thisYear', { header: `${data.value?.fiscalYear} 年度`, meta: { align: 'right' } }),
  monthCol.accessor('lastYear', { header: `${(data.value?.fiscalYear ?? 0) - 1} 年度`, meta: { align: 'right' } }),
]);

const receivableCol = dataColumns<ReceivableRow>();
const receivableColumns = computed(() => [
  receivableCol.accessor('invoiceNo', { header: '發票', meta: { width: 120 } }),
  receivableCol.accessor('projectName', { header: '專案／客戶' }),
  receivableCol.accessor('dueDate', { header: '到期日', meta: { width: 110 } }),
  // 沒有檢視金額的權限時，後端不回傳金額，也不顯示這一欄。
  ...(data.value?.canViewAmount ? [receivableCol.accessor('outstanding', { header: '未收', meta: { width: 110, align: 'right' as const } })] : []),
]);

const warrantyCol = dataColumns<WarrantyRow>();
const warrantyColumns = [
  warrantyCol.accessor('subject', { header: '保固' }),
  warrantyCol.accessor('projectName', { header: '專案／客戶' }),
  warrantyCol.accessor('endDate', { header: '到期日', meta: { width: 110 } }),
];

/** 月趨勢：今年與去年兩條線，同一個 y 軸；hover 顯示十字線與兩年數字。 */
const trendOption = computed(() => {
  const d = data.value;
  if (!d) return {};
  // 今年只畫到目前月份，未來月份留空而不是畫成 0。
  const thisYear = d.trend.thisYear.map((value, index) => (d.currentMonthIndex !== null && index > d.currentMonthIndex ? null : value));
  const line = (name: string, values: (number | null)[], color: string) => ({
    name,
    type: 'line',
    data: values,
    smooth: false,
    showSymbol: true,
    symbol: 'circle',
    symbolSize: 8,
    lineStyle: { width: 2, color },
    itemStyle: { color, borderColor: '#fff', borderWidth: 2 },
    emphasis: { focus: 'series' },
    endLabel: { show: true, formatter: '{a}', color: TEXT_PRIMARY, fontSize: 12 },
  });
  return {
    ...baseOption,
    grid: { ...baseOption.grid, right: 72 },
    tooltip: { ...baseOption.tooltip, trigger: 'axis', axisPointer: { type: 'line' }, valueFormatter: (value: number | null) => fullMoney(value) },
    xAxis: { ...categoryAxis(monthNames.value), boundaryGap: false },
    yAxis: valueAxis,
    series: [line(`${d.fiscalYear} 年度`, thisYear, SERIES[0]), line(`${d.fiscalYear - 1} 年度`, d.trend.lastYear, SERIES[1])],
  };
});

/** 季度：今年與去年並排長條，柱間留 2px 白縫。 */
const quarterOption = computed(() => {
  const d = data.value;
  if (!d) return {};
  const bar = (name: string, values: (number | null)[], color: string) => ({
    name,
    type: 'bar',
    data: values,
    barMaxWidth: 28,
    barGap: '8%',
    itemStyle: { color, borderRadius: [4, 4, 0, 0], borderColor: '#fff', borderWidth: 1 },
  });
  return {
    ...baseOption,
    tooltip: { ...baseOption.tooltip, trigger: 'axis', axisPointer: { type: 'shadow' }, valueFormatter: (value: number | null) => fullMoney(value) },
    xAxis: categoryAxis(['第 1 季', '第 2 季', '第 3 季', '第 4 季']),
    yAxis: valueAxis,
    series: [bar(`${d.fiscalYear} 年度`, d.quarters.thisYear, SERIES[0]), bar(`${d.fiscalYear - 1} 年度`, d.quarters.lastYear, SERIES[1])],
  };
});

/** 橫向單色長條（案件狀態、業務排行）：一個數列不需要圖例，數值直接標在柱尾。 */
function horizontalBars(labels: string[], values: number[], formatter: (value: number) => string) {
  return {
    ...baseOption,
    legend: undefined,
    grid: { ...baseOption.grid, top: 8, right: 72 },
    tooltip: { ...baseOption.tooltip, trigger: 'item', valueFormatter: (value: number) => formatter(value) },
    xAxis: { ...valueAxis, axisLabel: { show: false }, splitLine: { show: false } },
    yAxis: { ...categoryAxis(labels), inverse: true, axisLine: { show: false } },
    series: [
      {
        type: 'bar',
        data: values,
        barMaxWidth: 18,
        itemStyle: { color: SERIES[0], borderRadius: [0, 4, 4, 0] },
        label: { show: true, position: 'right', color: TEXT_PRIMARY, formatter: ({ value }: { value: number }) => formatter(value) },
      },
    ],
  };
}

const statusOption = computed(() => {
  const counts = data.value?.projects.statusCounts ?? {};
  const statuses = PROJECT_STATUSES.filter((status) => counts[status.value]);
  return horizontalBars(statuses.map((status) => status.label), statuses.map((status) => counts[status.value]), (value) => `${value} 件`);
});

const rankingOption = computed(() => {
  const ranking = data.value?.ranking ?? [];
  return horizontalBars(ranking.map((row) => row.name), ranking.map((row) => row.amount ?? 0), fullMoney);
});

const growthText = computed(() => {
  const rate = data.value?.kpi.growthRate;
  if (rate === null || rate === undefined) return null;
  return `${rate >= 0 ? '▲' : '▼'} ${Math.abs(rate)}%`;
});
</script>

<template>
  <a-space direction="vertical" :size="16" style="width: 100%">
    <a-card :bordered="false" :body-style="{ padding: '12px 16px' }">
      <div class="filters">
        <a-space wrap>
          <a-select v-model:value="filters.year" :options="yearOptions" placeholder="本年度" allow-clear style="width: 120px" />
          <a-segmented v-if="data?.canViewAmount !== false" v-model:value="filters.metric" :options="metricOptions" />
          <a-tree-select
            v-if="canFilterDepartment"
            v-model:value="filters.departmentId"
            :tree-data="departmentTree"
            allow-clear
            tree-default-expand-all
            placeholder="全部部門"
            style="width: 180px"
          />
          <div v-if="showAllStaff" style="width: 180px"><UserSelect v-model:value="filters.ownerUserId" allow-clear placeholder="全部負責人" /></div>
        </a-space>
        <span class="scope">資料範圍：{{ DATA_SCOPE_LABELS[me?.scope.dataScope ?? 'own'] }} {{ fiscalNote }}</span>
      </div>
    </a-card>

    <a-skeleton v-if="!data" active />
    <template v-else>
      <a-alert v-if="!data.canViewAmount" type="info" show-icon message="您沒有檢視金額的權限，以下只顯示案件數。" />

      <div class="kpis">
        <template v-if="data.canViewAmount">
          <div class="kpi">
            <div class="kpi-label">{{ data.fiscalYear }} 年度{{ metricLabel }}（未稅）</div>
            <div class="kpi-value">{{ fullMoney(data.kpi.yearToDate) }}</div>
            <div class="kpi-sub">
              去年同期 {{ fullMoney(data.kpi.lastYearToDate) }}
              <span v-if="growthText" :class="(data.kpi.growthRate ?? 0) >= 0 ? 'up' : 'down'">{{ growthText }}</span>
            </div>
          </div>
          <div v-if="data.isCurrentYear" class="kpi">
            <div class="kpi-label">本季（第 {{ data.kpi.currentQuarterNo }} 季）</div>
            <div class="kpi-value">{{ fullMoney(data.kpi.currentQuarter) }}</div>
            <div class="kpi-sub">本月 {{ fullMoney(data.kpi.currentMonth) }}</div>
          </div>
        </template>
        <div class="kpi">
          <div class="kpi-label">進行中案件</div>
          <div class="kpi-value">{{ data.projects.active }}</div>
          <div class="kpi-sub">本年度新增 {{ data.projects.createdThisYear }} 件</div>
        </div>
        <div class="kpi">
          <div class="kpi-label">成交率（本年度）</div>
          <div class="kpi-value">{{ data.projects.winRate === null ? '—' : `${data.projects.winRate}%` }}</div>
          <div class="kpi-sub">得標 {{ data.projects.won }} ／ 未得標 {{ data.projects.lost }}</div>
        </div>
        <div v-if="data.receivables" class="kpi">
          <div class="kpi-label">應收未收</div>
          <div class="kpi-value">{{ data.canViewAmount ? fullMoney(data.receivables.outstanding) : `${data.receivables.count} 張` }}</div>
          <div class="kpi-sub">
            <span v-if="data.receivables.overdueCount" class="down">逾期 {{ data.receivables.overdueCount }} 張{{ data.canViewAmount ? ` · ${fullMoney(data.receivables.overdue)}` : '' }}</span>
            <span v-else>沒有逾期</span>
          </div>
        </div>
      </div>

      <a-row v-if="data.canViewAmount" :gutter="[16, 16]">
        <a-col :xs="24" :xl="16">
          <a-card :bordered="false" :title="`每月${metricLabel}（未稅）`">
            <template #extra><a-switch v-model:checked="showTable" checked-children="表格" un-checked-children="圖表" /></template>
            <EChart v-if="!showTable" :option="trendOption" :height="300" :label="`每月${metricLabel}，今年與去年比較`" />
            <DataTable v-else :data="monthRows" :columns="monthColumns" row-key="month" size="small" :scroll-x="480">
              <template #cell="{ columnId, record }">
                <template v-if="columnId === 'thisYear' || columnId === 'lastYear'">{{ fullMoney(record[columnId]) }}</template>
              </template>
            </DataTable>
          </a-card>
        </a-col>
        <a-col :xs="24" :xl="8">
          <a-card :bordered="false" title="季度比較">
            <EChart :option="quarterOption" :height="300" label="各季業績，今年與去年比較" />
          </a-card>
        </a-col>
      </a-row>

      <a-row :gutter="[16, 16]">
        <a-col :xs="24" :lg="data.ranking && data.canViewAmount ? 12 : 24">
          <a-card :bordered="false" title="案件狀態">
            <EChart v-if="Object.keys(data.projects.statusCounts).length" :option="statusOption" :height="240" label="各狀態案件數" />
            <a-empty v-else description="還沒有專案" />
          </a-card>
        </a-col>
        <a-col v-if="data.ranking && data.canViewAmount" :xs="24" :lg="12">
          <a-card :bordered="false" :title="`業務排行（${metricLabel}）`">
            <EChart v-if="data.ranking.length" :option="rankingOption" :height="240" label="各負責人業績排行" />
            <a-empty v-else description="本年度沒有業績" />
          </a-card>
        </a-col>
      </a-row>

      <a-row :gutter="[16, 16]">
        <a-col v-if="data.receivables" :xs="24" :lg="12">
          <a-card :bordered="false" :title="`應收帳款（${data.receivables.count} 張）`">
            <DataTable
              :data="receivables.data.value?.items ?? []"
              :columns="receivableColumns"
              row-key="id"
              size="small"
              :scroll-x="480"
              :loading="receivables.isFetching.value"
              :pagination="{ page: receivablePaging.page, pageSize: receivablePaging.pageSize, total: receivables.data.value?.total ?? 0 }"
              @change="(state) => Object.assign(receivablePaging, { page: state.page, pageSize: state.pageSize })"
            >
              <template #cell="{ columnId, record }">
                <template v-if="columnId === 'projectName'">
                  <a :href="withBasePath(`/projects/detail?id=${record.projectId}`)">{{ record.projectName }}</a>
                  <div class="muted">{{ record.clientName }}</div>
                </template>
                <template v-else-if="columnId === 'dueDate'">
                  {{ record.dueDate ?? '—' }} <a-tag v-if="record.isOverdue" color="red">逾期</a-tag>
                </template>
                <template v-else-if="columnId === 'outstanding'">{{ fullMoney(record.outstanding) }}</template>
              </template>
              <template #empty><a-empty description="沒有未收款的發票" /></template>
            </DataTable>
          </a-card>
        </a-col>
        <a-col v-if="data.expiringWarranties" :xs="24" :lg="12">
          <a-card :bordered="false" :title="`90 天內到期的保固（${data.expiringWarranties.count} 件）`">
            <DataTable
              :data="warranties.data.value?.items ?? []"
              :columns="warrantyColumns"
              row-key="id"
              size="small"
              :scroll-x="480"
              :loading="warranties.isFetching.value"
              :pagination="{ page: warrantyPaging.page, pageSize: warrantyPaging.pageSize, total: warranties.data.value?.total ?? 0 }"
              @change="(state) => Object.assign(warrantyPaging, { page: state.page, pageSize: state.pageSize })"
            >
              <template #cell="{ columnId, record }">
                <template v-if="columnId === 'subject'">{{ record.subject }}<div class="muted">{{ record.warrantyNo }}</div></template>
                <template v-else-if="columnId === 'projectName'">
                  <a :href="withBasePath(`/projects/detail?id=${record.projectId}`)">{{ record.projectName }}</a>
                  <div class="muted">{{ record.clientName }}</div>
                </template>
              </template>
              <template #empty><a-empty description="近期沒有到期的保固" /></template>
            </DataTable>
          </a-card>
        </a-col>
      </a-row>
    </template>
  </a-space>
</template>

<style scoped>
.filters {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.scope {
  color: #888;
  font-size: 13px;
}
.kpis {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}
.kpi {
  background: #fff;
  border-radius: 8px;
  padding: 16px 20px;
}
.kpi-label {
  color: #595959;
  font-size: 13px;
}
.kpi-value {
  margin: 6px 0 4px;
  font-size: 26px;
  font-weight: 600;
  color: #1f1f1f;
  font-variant-numeric: tabular-nums;
}
.kpi-sub {
  color: #8c8c8c;
  font-size: 12px;
}
.up {
  color: #237804;
  margin-left: 6px;
}
.down {
  color: #cf1322;
  margin-left: 6px;
}
.muted {
  color: #999;
  font-size: 12px;
}
</style>
