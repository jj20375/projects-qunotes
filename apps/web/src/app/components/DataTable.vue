<script setup lang="ts" generic="T extends object">
/**
 * @component DataTable
 * 全站共用的資料表格：TanStack Table（v9）管理排序、分頁、展開的狀態，外觀與版面集中在這個元件。
 *
 * 資料的搜尋、篩選、排序與分頁一律由後端 API 處理（AGENTS.md 原則 13）：
 * 表格只顯示後端回傳的這一頁，點表頭、換頁時送出 `change` 事件，由頁面帶著新的條件重新呼叫 API。
 *
 * 用法：
 * ```vue
 * <DataTable
 *   :data="result?.items ?? []" :columns="columns" row-key="id" :loading="isFetching"
 *   :pagination="{ page, pageSize, total: result?.total ?? 0, showSizeChanger: true }"
 *   :sorting="sorting" :scroll-x="900" @change="onChange"
 * >
 *   <template #cell="{ columnId, record }"> …依欄位 id 自訂內容… </template>
 *   <template #expanded="{ record }"> …展開列（詳細內容）… </template>
 *   <template #empty>…沒有資料時的畫面…</template>
 * </DataTable>
 * ```
 * - 欄位用 `dataColumns<T>()` 建立（shared/table/data-table.ts）；`enableSorting: true` 的欄位可點表頭排序。
 * - `#cell`：沒有輸出內容的欄位會自動顯示欄位值（或欄位定義的 `cell` 格式化）。
 * - `#expanded`：有這個 slot 時，每列左邊出現展開鈕，展開後在下方顯示內容。
 * - `get-sub-rows`：樹狀資料（部門、報價版本）；樹狀的展開鈕在第一欄。
 * - `client-page-size`：只給「已完整載入的小型預覽」（匯入檔預覽）在前端切頁；一般列表不可使用。
 * - `v-model:selected-keys`：勾選列。有這個 prop 時左邊出現勾選欄，值是列的 row-key；換頁時由頁面保留已勾選的 key，
 *   所以可以跨頁勾選。`row-selectable` 決定哪些列不能勾（例如已經在專案裡的人）。
 */
import { computed, Comment, defineComponent, Fragment, h, ref, Text, type PropType, type VNode } from 'vue';
import { theme } from 'ant-design-vue';
import { FlexRender, useTable, type Cell, type ExpandedState, type Updater } from '@tanstack/vue-table';
import { dataTableFeatures, sortingToParams, type DataColumn, type DataTableFeatures, type SortingState } from '@/shared/table/data-table';

export interface TablePagination {
  /** 目前頁碼（從 1 開始）。 */
  page: number;
  pageSize: number;
  /** 符合條件的總筆數（後端回傳的 total）。 */
  total: number;
  showSizeChanger?: boolean;
  showTotal?: (total: number) => string;
}

export interface TableChange {
  page: number;
  pageSize: number;
  /** 排序欄位 id；沒有排序時為 null，由頁面套用預設排序。 */
  sort: string | null;
  desc: boolean;
}

const props = withDefaults(
  defineProps<{
    data: readonly T[];
    columns: DataColumn<T>[];
    rowKey?: (keyof T & string) | ((row: T) => string | number);
    loading?: boolean;
    size?: 'middle' | 'small';
    /** 表格最小寬度（px），螢幕更窄時橫向捲動。 */
    scrollX?: number;
    /** 表格最大高度（px），超過時垂直捲動並固定表頭。 */
    scrollY?: number;
    pagination?: TablePagination | false;
    clientPageSize?: number;
    sorting?: SortingState;
    getSubRows?: (row: T) => T[] | undefined;
    defaultExpandAll?: boolean;
    emptyText?: string;
    selectedKeys?: string[];
    rowSelectable?: (row: T) => boolean;
  }>(),
  { rowKey: 'id' as never, size: 'middle', pagination: false, emptyText: '沒有資料' },
);
const emit = defineEmits<{ change: [state: TableChange]; 'update:selectedKeys': [keys: string[]] }>();
const slots = defineSlots<{
  /** 自訂儲存格內容；沒有輸出內容的欄位顯示欄位值。`columnId` 是欄位的 id（accessor 的名稱或 display 欄位的 id）。 */
  cell?(props: { columnId: string; record: T; value: unknown; index: number }): VNode[];
  /** 展開列的詳細內容；有這個 slot 時每列左邊出現展開鈕。 */
  expanded?(props: { record: T }): VNode[];
  /** 沒有資料時顯示的畫面；預設是「沒有資料」。 */
  empty?(): VNode[];
}>();

const hasDetail = computed(() => !!slots.expanded);
const isTree = computed(() => !!props.getSubRows);

const expanded = ref<ExpandedState>(props.defaultExpandAll ? true : {});
const clientPagination = ref({ pageIndex: 0, pageSize: props.clientPageSize ?? 20 });
const resolve = <V,>(updater: Updater<V>, current: V): V => (typeof updater === 'function' ? (updater as (old: V) => V)(current) : updater);

function emitChange(next: { page?: number; pageSize?: number; sorting?: SortingState }) {
  const pagination = props.pagination || undefined;
  emit('change', {
    page: next.page ?? pagination?.page ?? 1,
    pageSize: next.pageSize ?? pagination?.pageSize ?? 20,
    ...sortingToParams(next.sorting ?? props.sorting ?? []),
  });
}

const table = useTable<DataTableFeatures, T>({
  features: dataTableFeatures,
  columns: computed(() => props.columns),
  // 展開成新陣列：呼叫端若原地修改陣列（splice、push），TanStack 才會偵測到資料變了。
  data: computed(() => [...props.data]),
  getRowId: (row: T) => String(typeof props.rowKey === 'function' ? props.rowKey(row) : (row as Record<string, unknown>)[props.rowKey as string]),
  getSubRows: (row: T) => props.getSubRows?.(row),
  getRowCanExpand: (row) => (hasDetail.value ? true : row.subRows.length > 0),
  defaultColumn: { enableSorting: false },
  // 排序與分頁由後端處理：表格只記錄狀態並通知頁面，不自己重排或切資料。
  manualSorting: true,
  // 沒有伺服器分頁、也沒指定 clientPageSize 時不分頁；否則 TanStack 預設的每頁 20 筆會在沒有分頁器的情況下悄悄截掉後面的列。
  manualPagination: computed(() => !!props.pagination || !props.clientPageSize),
  rowCount: computed(() => (props.pagination ? props.pagination.total : undefined)),
  enableSortingRemoval: true,
  sortDescFirst: false,
  autoResetPageIndex: false,
  autoResetExpanded: false,
  state: computed(() => ({
    sorting: props.sorting ?? [],
    pagination: props.pagination ? { pageIndex: props.pagination.page - 1, pageSize: props.pagination.pageSize } : clientPagination.value,
    expanded: expanded.value,
  })),
  onSortingChange: (updater: Updater<SortingState>) => emitChange({ sorting: resolve(updater, props.sorting ?? []), page: 1 }),
  onPaginationChange: (updater) => {
    const current = props.pagination ? { pageIndex: props.pagination.page - 1, pageSize: props.pagination.pageSize } : clientPagination.value;
    const next = resolve(updater, current);
    if (props.pagination) emitChange({ page: next.pageIndex + 1, pageSize: next.pageSize });
    else clientPagination.value = next;
  },
  onExpandedChange: (updater) => (expanded.value = resolve(updater, expanded.value)),
});

const rows = computed(() => table.getRowModel().rows);
const hasSelection = computed(() => props.selectedKeys !== undefined);
const leafColumnCount = computed(() => table.getAllLeafColumns().length + (hasDetail.value ? 1 : 0) + (hasSelection.value ? 1 : 0));

// ---------- 勾選 ----------
const selectedSet = computed(() => new Set(props.selectedKeys ?? []));
const canSelect = (row: T) => props.rowSelectable?.(row) ?? true;
/** 這一頁可以勾選的列的 key。 */
const pageKeys = computed(() => rows.value.filter((row) => canSelect(row.original as T)).map((row) => row.id));
const pageAllSelected = computed(() => pageKeys.value.length > 0 && pageKeys.value.every((key) => selectedSet.value.has(key)));
const pageSomeSelected = computed(() => !pageAllSelected.value && pageKeys.value.some((key) => selectedSet.value.has(key)));
function toggleRow(key: string, checked: boolean) {
  const next = new Set(selectedSet.value);
  if (checked) next.add(key);
  else next.delete(key);
  emit('update:selectedKeys', [...next]);
}
function togglePage(checked: boolean) {
  const next = new Set(selectedSet.value);
  for (const key of pageKeys.value) {
    if (checked) next.add(key);
    else next.delete(key);
  }
  emit('update:selectedKeys', [...next]);
}

// ---------- 分頁 ----------
const pager = computed(() => {
  if (props.pagination) return props.pagination;
  if (props.clientPageSize) {
    return { page: clientPagination.value.pageIndex + 1, pageSize: clientPagination.value.pageSize, total: props.data.length, showSizeChanger: false };
  }
  return null;
});

function onPage(page: number, pageSize: number) {
  if (props.pagination) emitChange({ page, pageSize });
  else clientPagination.value = { pageIndex: page - 1, pageSize };
}

// ---------- 儲存格 ----------
/** slot 有輸出實際內容（不是只有註解或空片段）。 */
function hasContent(nodes: VNode[] | undefined): boolean {
  return !!nodes?.some((node) => {
    if (node.type === Comment) return false;
    if (node.type === Text) return String(node.children ?? '').trim().length > 0;
    if (node.type === Fragment) return hasContent(node.children as VNode[]);
    return true;
  });
}

/** 先交給 `#cell` slot；slot 沒輸出內容時，顯示欄位定義的格式化結果。 */
const CellView = defineComponent({
  props: { cell: { type: Object as PropType<Cell<DataTableFeatures, T, unknown>>, required: true } },
  setup(cellProps) {
    return () => {
      const cell = cellProps.cell;
      const custom = slots.cell?.({ columnId: cell.column.id, record: cell.row.original as T, value: cell.getValue(), index: cell.row.index });
      return hasContent(custom) ? custom : h(FlexRender, { cell });
    };
  },
});

// ---------- 版面 ----------
const { token } = theme.useToken();
const styleVars = computed(() => ({
  '--dt-border': token.value.colorBorderSecondary,
  // 表頭、懸停列、固定欄的底色必須不透明：Ant Design 的 colorFillAlter 是半透明（2%），
  // 固定在右側的欄位與固定表頭會透出底下被捲動的內容。這裡把它疊在不透明的容器底色上。
  '--dt-head-bg': `color-mix(in srgb, #000 3%, ${token.value.colorBgContainer})`,
  '--dt-hover-bg': `color-mix(in srgb, #000 3%, ${token.value.colorBgContainer})`,
  '--dt-text': token.value.colorText,
  '--dt-muted': token.value.colorTextTertiary,
  '--dt-primary': token.value.colorPrimary,
  '--dt-bg': token.value.colorBgContainer,
  '--dt-radius': `${token.value.borderRadiusLG}px`,
}));

const meta = (column: { columnDef: { meta?: unknown } }) => (column.columnDef.meta ?? {}) as { width?: number; align?: 'left' | 'center' | 'right'; fixed?: 'right' };
const cellStyle = (column: { columnDef: { meta?: unknown } }) => {
  const { width, align } = meta(column);
  return { width: width ? `${width}px` : undefined, minWidth: width ? `${width}px` : undefined, textAlign: align };
};
const sortLabel = (order: false | 'asc' | 'desc') => (order === 'asc' ? 'ascending' : order === 'desc' ? 'descending' : 'none');
</script>

<template>
  <div class="dt" :class="`dt-${size}`" :style="styleVars">
    <a-spin :spinning="!!loading">
      <div class="dt-scroll" :style="{ maxHeight: scrollY ? `${scrollY}px` : undefined }">
        <table class="dt-table" :style="{ minWidth: scrollX ? `${scrollX}px` : undefined }">
          <thead>
            <tr v-for="group in table.getHeaderGroups()" :key="group.id">
              <th v-if="hasSelection" class="dt-select-col" :class="{ 'dt-sticky-head': !!scrollY }">
                <a-checkbox :checked="pageAllSelected" :indeterminate="pageSomeSelected" :disabled="!pageKeys.length" aria-label="勾選這一頁" @change="(event: { target: { checked: boolean } }) => togglePage(event.target.checked)" />
              </th>
              <th v-if="hasDetail" class="dt-expand-col" :class="{ 'dt-sticky-head': !!scrollY }" aria-label="展開" />
              <th
                v-for="header in group.headers"
                :key="header.id"
                :class="[{ 'dt-sticky-head': !!scrollY, 'dt-fixed-right': meta(header.column).fixed === 'right', 'dt-sortable': header.column.getCanSort() }]"
                :style="cellStyle(header.column)"
                :aria-sort="header.column.getCanSort() ? sortLabel(header.column.getIsSorted()) : undefined"
                @click="header.column.getToggleSortingHandler()?.($event)"
              >
                <!-- 整個表頭儲存格都能點；裡面的按鈕讓鍵盤（Tab、Enter）也能排序，點擊事件會冒泡到儲存格處理，所以按鈕本身不再綁事件。 -->
                <button v-if="header.column.getCanSort()" type="button" class="dt-sort">
                  <FlexRender :header="header" />
                  <span class="dt-sort-icon" :data-order="header.column.getIsSorted() || ''" aria-hidden="true" />
                </button>
                <FlexRender v-else-if="!header.isPlaceholder" :header="header" />
              </th>
            </tr>
          </thead>
          <tbody>
            <template v-for="row in rows" :key="row.id">
              <tr class="dt-row" :class="{ 'dt-row-expanded': row.getIsExpanded(), 'dt-row-selected': selectedSet.has(row.id) }">
                <td v-if="hasSelection" class="dt-select-col">
                  <a-checkbox
                    :checked="selectedSet.has(row.id)"
                    :disabled="!canSelect(row.original as T)"
                    aria-label="勾選"
                    @change="(event: { target: { checked: boolean } }) => toggleRow(row.id, event.target.checked)"
                  />
                </td>
                <td v-if="hasDetail" class="dt-expand-col">
                  <button
                    type="button"
                    class="dt-toggle"
                    :class="{ 'is-open': row.getIsExpanded() }"
                    :aria-expanded="row.getIsExpanded()"
                    :aria-label="row.getIsExpanded() ? '收合' : '展開'"
                    @click="row.toggleExpanded()"
                  />
                </td>
                <td
                  v-for="(cell, cellIndex) in row.getAllCells()"
                  :key="cell.id"
                  :class="{ 'dt-fixed-right': meta(cell.column).fixed === 'right' }"
                  :style="cellStyle(cell.column)"
                >
                  <div v-if="isTree && cellIndex === 0" class="dt-tree-cell" :style="{ paddingLeft: `${row.depth * 24}px` }">
                    <button
                      v-if="row.getCanExpand()"
                      type="button"
                      class="dt-toggle"
                      :class="{ 'is-open': row.getIsExpanded() }"
                      :aria-expanded="row.getIsExpanded()"
                      :aria-label="row.getIsExpanded() ? '收合' : '展開'"
                      @click="row.toggleExpanded()"
                    />
                    <span v-else class="dt-toggle-spacer" />
                    <CellView :cell="cell" />
                  </div>
                  <CellView v-else :cell="cell" />
                </td>
              </tr>
              <tr v-if="hasDetail && row.getIsExpanded()" class="dt-detail">
                <td :colspan="leafColumnCount"><slot name="expanded" :record="row.original" /></td>
              </tr>
            </template>
            <tr v-if="!rows.length && !loading" class="dt-empty">
              <td :colspan="leafColumnCount">
                <slot name="empty"><a-empty :image="Empty.PRESENTED_IMAGE_SIMPLE" :description="emptyText" /></slot>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </a-spin>
    <div v-if="pager" class="dt-pager">
      <a-pagination
        :current="pager.page"
        :page-size="pager.pageSize"
        :total="pager.total"
        :show-size-changer="pager.showSizeChanger"
        :show-total="pager.showTotal"
        :hide-on-single-page="!!clientPageSize"
        :size="size === 'small' ? 'small' : 'default'"
        @change="onPage"
      />
    </div>
  </div>
</template>

<script lang="ts">
import { Empty } from 'ant-design-vue';
export default { inheritAttrs: true };
</script>

<style scoped>
.dt {
  color: var(--dt-text);
}
.dt-scroll {
  overflow: auto;
  border: 1px solid var(--dt-border);
  border-radius: var(--dt-radius);
  background: var(--dt-bg);
}
.dt-table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  text-align: left;
}
.dt-table th,
.dt-table td {
  padding: 12px 16px;
  border-bottom: 1px solid var(--dt-border);
  vertical-align: middle;
  overflow-wrap: break-word;
}
.dt-small .dt-table th,
.dt-small .dt-table td {
  padding: 8px;
}
.dt-table th {
  background: var(--dt-head-bg);
  font-weight: 600;
  white-space: nowrap;
}
.dt-table tbody tr:last-child > td {
  border-bottom: none;
}
.dt-row:hover > td {
  background: var(--dt-hover-bg);
}
.dt-select-col {
  width: 48px;
  text-align: center;
}
.dt-row-selected > td {
  background: color-mix(in srgb, var(--dt-primary) 6%, var(--dt-bg));
}
.dt-detail > td {
  background: var(--dt-head-bg);
  padding: 12px 16px 12px 56px;
}
.dt-empty > td {
  padding: 32px 16px;
  text-align: center;
  color: var(--dt-muted);
}
.dt-sticky-head {
  position: sticky;
  top: 0;
  z-index: 2;
}
.dt-fixed-right {
  position: sticky;
  right: 0;
  z-index: 1;
  background: var(--dt-bg);
  box-shadow: -6px 0 6px -6px rgba(0, 0, 0, 0.15);
}
th.dt-fixed-right {
  background: var(--dt-head-bg);
}
.dt-row:hover > td.dt-fixed-right {
  background: var(--dt-hover-bg);
}
.dt-expand-col {
  width: 44px;
  min-width: 44px;
  padding-left: 12px !important;
  padding-right: 4px !important;
}

.dt-sortable {
  cursor: pointer;
  user-select: none;
}
.dt-sortable:hover {
  background: color-mix(in srgb, var(--dt-head-bg) 80%, var(--dt-primary) 8%);
}

/* 排序鈕：沿用文字樣式，右側顯示排序方向 */
.dt-sort {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 0;
  border: none;
  background: none;
  font: inherit;
  color: inherit;
  cursor: pointer;
}
.dt-sort:hover,
.dt-sort:focus-visible {
  color: var(--dt-primary);
}
.dt-sort-icon {
  width: 0;
  height: 0;
  border-left: 4px solid transparent;
  border-right: 4px solid transparent;
  border-bottom: 5px solid #bfbfbf;
  position: relative;
  top: -3px;
  opacity: 0.6;
}
.dt-sort-icon::after {
  content: '';
  position: absolute;
  left: -4px;
  top: 8px;
  border-left: 4px solid transparent;
  border-right: 4px solid transparent;
  border-top: 5px solid #bfbfbf;
}
.dt-sort-icon[data-order='asc'] {
  border-bottom-color: var(--dt-primary);
  opacity: 1;
}
.dt-sort-icon[data-order='desc'] {
  opacity: 1;
}
.dt-sort-icon[data-order='desc']::after {
  border-top-color: var(--dt-primary);
}

/* 展開鈕 */
.dt-toggle {
  width: 20px;
  height: 20px;
  padding: 0;
  border: 1px solid var(--dt-border);
  border-radius: 4px;
  background: var(--dt-bg);
  cursor: pointer;
  position: relative;
  flex: none;
}
.dt-toggle::before {
  content: '';
  position: absolute;
  left: 6px;
  top: 5px;
  border-top: 4px solid transparent;
  border-bottom: 4px solid transparent;
  border-left: 5px solid #595959;
  transition: transform 0.15s;
}
.dt-toggle.is-open::before {
  transform: rotate(90deg);
  left: 6px;
  top: 6px;
}
.dt-toggle:hover,
.dt-toggle:focus-visible {
  border-color: var(--dt-primary);
}
.dt-tree-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}
.dt-toggle-spacer {
  width: 20px;
  flex: none;
}
.dt-pager {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}
</style>
