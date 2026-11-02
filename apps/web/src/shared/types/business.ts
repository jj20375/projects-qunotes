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

export type ProjectStatus = (typeof PROJECT_STATUSES)[number]['value'];

export function projectStatusLabel(status: string): string {
  return PROJECT_STATUSES.find((item) => item.value === status)?.label ?? status;
}

export function projectStatusColor(status: string): string {
  return PROJECT_STATUSES.find((item) => item.value === status)?.color ?? 'default';
}

/** 報價狀態。 */
export const QUOTE_STATUSES = [
  { value: 'draft', label: '草稿', color: 'default' },
  { value: 'pending_approval', label: '待核准', color: 'orange' },
  { value: 'approved', label: '已核准', color: 'cyan' },
  { value: 'sent', label: '已送出', color: 'processing' },
  { value: 'accepted', label: '客戶接受', color: 'success' },
  { value: 'rejected', label: '客戶拒絕', color: 'error' },
  { value: 'superseded', label: '已作廢', color: 'default' },
] as const;

export function quoteStatusLabel(status: string): string {
  return QUOTE_STATUSES.find((item) => item.value === status)?.label ?? status;
}

export function quoteStatusColor(status: string): string {
  return QUOTE_STATUSES.find((item) => item.value === status)?.color ?? 'default';
}

/** 報價單號＋版次：Q202610-0001、Q202610-0001-R1。 */
export function quoteNumber(quote: { quoteNo: string; revisionNo: number }): string {
  return quote.revisionNo > 0 ? `${quote.quoteNo}-R${quote.revisionNo}` : quote.quoteNo;
}

export const PROJECT_ROLES: Record<string, string> = {
  owner: '負責人',
  manager: '管理',
  member: '成員',
  viewer: '檢視',
};

const money = new Intl.NumberFormat('zh-TW', { maximumFractionDigits: 0 });

/** 金額以整數元顯示（新台幣）；null 顯示破折號。 */
export function formatMoney(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : money.format(value);
}

export function formatDateTime(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString('zh-TW', { hour12: false }) : '—';
}
