/**
 * @file business.ts
 * 專案、報價單與客戶通用業務狀態、常數與格式化工具。
 */

/** 專案狀態顯示名稱與顏色；順序即業務流程順序。 */
export const PROJECT_STATUSES = [
  { value: 'draft', label: '草稿', color: 'default' },
  { value: 'quoting', label: '報價中', color: 'processing' },
  { value: 'won', label: '得標', color: 'success' },
  { value: 'lost', label: '未得標', color: 'error' },
  { value: 'in_progress', label: '執行中', color: 'blue' },
  { value: 'completed', label: '已完工', color: 'cyan' },
  { value: 'closed', label: '已結案', color: 'purple' },
  { value: 'cancelled', label: '已取消', color: 'default' },
] as const;

/** 專案狀態鍵值型別 */
export type ProjectStatus = (typeof PROJECT_STATUSES)[number]['value'];

/**
 * 取得專案狀態的繁體中文標籤
 *
 * @param status 專案狀態字串代碼
 * @returns 繁體中文顯示文字；若無對應則回傳原字串
 */
export function projectStatusLabel(status: string): string {
  return PROJECT_STATUSES.find((item) => item.value === status)?.label ?? status;
}

/**
 * 取得專案狀態對應的 Ant Design Tag 標籤顏色
 *
 * @param status 專案狀態字串代碼
 * @returns 顏色名稱字串
 */
export function projectStatusColor(status: string): string {
  return PROJECT_STATUSES.find((item) => item.value === status)?.color ?? 'default';
}

/** 報價狀態定義清單。 */
export const QUOTE_STATUSES = [
  { value: 'draft', label: '草稿', color: 'default' },
  { value: 'pending_approval', label: '待核准', color: 'orange' },
  { value: 'approved', label: '已核准', color: 'cyan' },
  { value: 'sent', label: '已送出', color: 'processing' },
  { value: 'accepted', label: '客戶接受', color: 'success' },
  { value: 'rejected', label: '客戶拒絕', color: 'error' },
  { value: 'superseded', label: '已作廢', color: 'default' },
] as const;

/**
 * 取得報價單狀態的繁體中文標籤
 *
 * @param status 報價狀態字串代碼
 * @returns 繁體中文顯示文字；若無對應則回傳原字串
 */
export function quoteStatusLabel(status: string): string {
  return QUOTE_STATUSES.find((item) => item.value === status)?.label ?? status;
}

/**
 * 取得報價單狀態對應的 Ant Design Tag 標籤顏色
 *
 * @param status 報價狀態字串代碼
 * @returns 顏色名稱字串
 */
export function quoteStatusColor(status: string): string {
  return QUOTE_STATUSES.find((item) => item.value === status)?.color ?? 'default';
}

/**
 * 產生含版次的格式化報價單號
 *
 * 規則：第一版為 Q202610-0001，改版後則加上 -R1、-R2（如 Q202610-0001-R1）。
 *
 * @param quote 包含單號與版次的物件
 * @returns 格式化後的完整報價單號
 */
export function quoteNumber(quote: { quoteNo: string; revisionNo: number }): string {
  return quote.revisionNo > 0 ? `${quote.quoteNo}-R${quote.revisionNo}` : quote.quoteNo;
}

/** 專案成員角色對應之中文名稱表 */
export const PROJECT_ROLES: Record<string, string> = {
  owner: '負責人',
  manager: '管理',
  member: '成員',
  viewer: '檢視',
};

const money = new Intl.NumberFormat('zh-TW', { maximumFractionDigits: 0 });

/**
 * 將數值格式化為台幣整數金額字串
 *
 * @param value 金額數值
 * @returns 格式化金額字串（千分位、無小數）；若為 null 或 undefined 則傳回破折號 '—'
 */
export function formatMoney(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : money.format(value);
}

/**
 * 將 UTC 日期字串轉換為本地日期時間格式
 *
 * @param value ISO 8601 UTC 日期時間字串
 * @returns 繁體中文本地日期時間字串；若為空則傳回破折號 '—'
 */
export function formatDateTime(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString('zh-TW', { hour12: false }) : '—';
}
