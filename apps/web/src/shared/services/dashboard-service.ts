/**
 * @file dashboard-service.ts
 * 營運戰情 Dashboard 與公司系統參數 API。
 *
 * 金額指標具有權限保護：若無 dashboard:view_amount 權限，後端將回傳 null。
 */
import { apiRequest } from '@/shared/api/http-client';
import { putToFileServer, type UploadSession } from '@/shared/services/document-service';

/** 營運指標衡量口徑：accepted（報價成交）、orders（訂單）、invoiced（已開發票） */
export type DashboardMetric = 'accepted' | 'orders' | 'invoiced';
type Money = number | null;

/**
 * 戰情儀表板彙整數據
 */
export interface DashboardSummary {
  /** 統計會計年度 */
  fiscalYear: number;
  /** 會計年度起始月份（1~12） */
  fiscalYearStartMonth: number;
  /** 是否為當前即時年度 */
  isCurrentYear: boolean;
  /** 當前選取的統計口徑 */
  metric: DashboardMetric;
  /** 當前操作者是否具備檢視金額權限 */
  canViewAmount: boolean;
  /** 各月份標籤陣列 */
  monthLabels: string[];
  /** 當前月份索引 */
  currentMonthIndex: number | null;
  /** 12 個月趨勢圖數據（今年與去年同期） */
  trend: { thisYear: Money[]; lastYear: Money[] };
  /** 季度比較數據（今年與去年） */
  quarters: { thisYear: Money[]; lastYear: Money[] };
  /** KPI 指標彙總 */
  kpi: { yearTotal: Money; yearToDate: Money; lastYearToDate: Money; growthRate: number | null; currentQuarter: Money; currentQuarterNo: number | null; currentMonth: Money };
  /** 各衡量口徑之全年度總計 */
  totals: Record<DashboardMetric, Money>;
  /** 專案漏斗分析與贏率統計 */
  projects: { statusCounts: Record<string, number>; active: number; createdThisYear: number; createdMonthly: number[]; won: number; lost: number; winRate: number | null };
  /** 業務人員業績排行（無權限時為 null） */
  ranking: { ownerUserId: string; name: string; amount: Money; count: number }[] | null;
  /** 即將到期保固提醒 */
  expiringWarranties: {
    count: number;
    items: { id: string; warrantyNo: string; subject: string; endDate: string; projectId: string; projectNo: string; projectName: string; clientName: string }[];
  } | null;
  /** 應收未收與逾期發票提醒 */
  receivables: {
    count: number;
    overdueCount: number;
    outstanding: Money;
    overdue: Money;
    items: { id: string; invoiceNo: string; dueDate: string | null; outstanding: Money; isOverdue: boolean; projectId: string; projectNo: string; projectName: string; clientName: string }[];
  } | null;
}

/**
 * 取得營運戰情 Dashboard 彙整數據
 *
 * @param query 查詢條件（年度、口徑、部門與負責業務篩選）
 * @returns 傳回儀表板彙整資訊
 */
export function getDashboardApi(query: { year?: number; metric: DashboardMetric; departmentId?: string; ownerUserId?: string }): Promise<DashboardSummary> {
  return apiRequest<DashboardSummary>('/api/dashboard/summary', { query });
}

/**
 * 公司系統參數與格式配置
 */
export interface CompanySettings {
  /** 公司基本資料 */
  company: { name: string; taxId: string | null; phone: string | null; address: string | null; englishName: string | null; logoFileId: string | null; logoUrl: string | null };
  /** 預設營業稅率 */
  defaultTaxRate: number;
  /** 預設報價幣別 */
  defaultCurrency: string;
  /** 報價單有效天數 */
  quoteValidDays: number;
  /** 預設報價條款 */
  defaultTerms: string | null;
  /** 會計年度起始月份 */
  fiscalYearStartMonth: number;
  /** 報價單號命名模式規則 */
  quoteNumberPattern: string;
  /** 報價單號規則即時預覽字串 */
  quoteNumberPreview: string;
}

/**
 * 公司參數儲存輸入結構
 */
export interface CompanySettingsInput {
  defaultTaxRate: number;
  defaultCurrency: string;
  quoteValidDays: number;
  defaultTerms: string | null;
  fiscalYearStartMonth: number;
  quoteNumberPattern: string;
  englishName: string;
  phone: string;
  address: string;
  logoFileId?: string;
  removeLogo?: boolean;
}

/**
 * 取得當前公司系統參數設定
 *
 * @returns 傳回公司設定物件
 */
export function getCompanySettingsApi(): Promise<CompanySettings> {
  return apiRequest<CompanySettings>('/api/admin/settings');
}

/**
 * 更新當前公司系統參數設定
 *
 * @param body 公司設定輸入資料
 * @returns Promise<void>
 */
export function updateCompanySettingsApi(body: CompanySettingsInput): Promise<void> {
  return apiRequest('/api/admin/settings', { method: 'PUT', body });
}

/**
 * 上傳公司 Logo 檔案
 *
 * 流程：向 API 申請上傳工作階段 → 直接 PUT 檔案至 file-server → 回傳 fileId 供公司設定關聯。
 *
 * @param file 圖片檔案物件
 * @returns 傳回新建立之檔案識別碼 fileId
 */
export async function uploadCompanyLogoApi(file: File): Promise<string> {
  const session = await apiRequest<UploadSession>('/api/admin/settings/logo-upload', { method: 'POST', body: { fileName: file.name, size: file.size } });
  await putToFileServer(session, file);
  return session.fileId;
}

/**
 * 即時預覽單號命名規則產生之編號樣式
 *
 * @param pattern 單號範本字串（例如 'Q{yyyy}{MM}-{seq:4}'）
 * @param date 參考基準日期，預設為當前時間
 * @param sequence 序號，預設為 1
 * @returns 預覽產出之單號字串
 */
export function previewNumberPattern(pattern: string, date = new Date(), sequence = 1): string {
  const pad = (value: number, size: number) => String(value).padStart(size, '0');
  return pattern.replace(/\{(yyyy|yy|MM|dd|seq(?::([1-6]))?)\}/g, (_, token: string, digits?: string) => {
    if (token === 'yyyy') return String(date.getFullYear());
    if (token === 'yy') return pad(date.getFullYear() % 100, 2);
    if (token === 'MM') return pad(date.getMonth() + 1, 2);
    if (token === 'dd') return pad(date.getDate(), 2);
    return pad(sequence, digits ? Number(digits) : 1);
  });
}
