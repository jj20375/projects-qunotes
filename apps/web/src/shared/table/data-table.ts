/**
 * @file data-table.ts
 * 共用表格（app/components/DataTable.vue）的 TanStack Table 設定與型別。
 *
 * 表格狀態（排序、分頁、展開）交給 TanStack Table 管理；資料的篩選、排序與分頁一律由後端 API 完成
 * （見 AGENTS.md 原則 13），所以這裡只註冊「狀態」功能，不註冊前端的排序／篩選 row model。
 */
import {
  createColumnHelper,
  createExpandedRowModel,
  createPaginatedRowModel,
  metaHelper,
  rowExpandingFeature,
  rowPaginationFeature,
  rowSortingFeature,
  tableFeatures,
  type ColumnDef,
  type RowData,
  type SortingState,
} from '@tanstack/vue-table';

/** 欄位的版面設定；不影響資料，只決定這一欄怎麼呈現。 */
export interface DataColumnMeta {
  /** 欄寬（px）；沒指定的欄位平分剩下的寬度。 */
  width?: number;
  /** 對齊方式，金額與數量用 `right`。 */
  align?: 'left' | 'center' | 'right';
  /** 橫向捲動時固定在右側（例如操作欄）。 */
  fixed?: 'right';
}

export const dataTableFeatures = tableFeatures({
  rowSortingFeature,
  rowPaginationFeature,
  rowExpandingFeature,
  // 只用在「已完整載入的小型預覽」的前端切頁（匯入檔預覽）；伺服器分頁時被 manualPagination 略過。
  paginatedRowModel: createPaginatedRowModel(),
  // 樹狀資料（部門、報價版本）的展開。
  expandedRowModel: createExpandedRowModel(),
  columnMeta: metaHelper<DataColumnMeta>(),
});

export type DataTableFeatures = typeof dataTableFeatures;
export type DataColumn<T extends RowData> = ColumnDef<DataTableFeatures, T, any>;

/**
 * 建立某種資料列的欄位。用法：
 * ```ts
 * const col = dataColumns<ClientRow>();
 * const columns = [
 *   col.accessor('name', { header: '客戶名稱', enableSorting: true }),
 *   col.accessor('amount', { header: '金額', meta: { width: 120, align: 'right' } }),
 *   col.display({ id: 'actions', header: '', meta: { width: 150 } }),
 * ];
 * ```
 * 沒有 `enableSorting: true` 的欄位不能點表頭排序。自訂儲存格內容放在 `<DataTable>` 的 `#cell` slot。
 */
export function dataColumns<T extends RowData>() {
  return createColumnHelper<DataTableFeatures, T>();
}

/** 目前的排序狀態轉成 API 參數（沒有排序時回傳 null）。 */
export function sortingToParams(sorting: SortingState): { sort: string | null; desc: boolean } {
  const first = sorting[0];
  return { sort: first?.id ?? null, desc: first?.desc ?? false };
}

/** 排序參數轉成表格的排序狀態；`fallback` 是「沒有點任何欄位時」API 的預設排序，不顯示為已排序。 */
export function paramsToSorting(sort: string | null | undefined, desc: boolean, fallback?: string): SortingState {
  return sort && sort !== fallback ? [{ id: sort, desc }] : [];
}

export type { SortingState };
