/**
 * @file dashboard-service.ts
 * 戰情 Dashboard 與公司參數 API。金額欄位在沒有 dashboard:view_amount 時為 null。
 */
import { apiRequest } from '@/shared/api/http-client';
import { putToFileServer, type UploadSession } from '@/shared/services/document-service';

export type DashboardMetric = 'accepted' | 'orders' | 'invoiced';
type Money = number | null;

export interface DashboardSummary {
  fiscalYear: number;
  fiscalYearStartMonth: number;
  isCurrentYear: boolean;
  metric: DashboardMetric;
  canViewAmount: boolean;
  monthLabels: string[];
  currentMonthIndex: number | null;
  trend: { thisYear: Money[]; lastYear: Money[] };
  quarters: { thisYear: Money[]; lastYear: Money[] };
  kpi: { yearTotal: Money; yearToDate: Money; lastYearToDate: Money; growthRate: number | null; currentQuarter: Money; currentQuarterNo: number | null; currentMonth: Money };
  totals: Record<DashboardMetric, Money>;
  projects: { statusCounts: Record<string, number>; active: number; createdThisYear: number; createdMonthly: number[]; won: number; lost: number; winRate: number | null };
  ranking: { ownerUserId: string; name: string; amount: Money; count: number }[] | null;
  expiringWarranties: {
    count: number;
    items: { id: string; warrantyNo: string; subject: string; endDate: string; projectId: string; projectNo: string; projectName: string; clientName: string }[];
  } | null;
  receivables: {
    count: number;
    overdueCount: number;
    outstanding: Money;
    overdue: Money;
    items: { id: string; invoiceNo: string; dueDate: string | null; outstanding: Money; isOverdue: boolean; projectId: string; projectNo: string; projectName: string; clientName: string }[];
  } | null;
}

export const getDashboardApi = (query: { year?: number; metric: DashboardMetric; departmentId?: string; ownerUserId?: string }) =>
  apiRequest<DashboardSummary>('/api/dashboard/summary', { query });

export interface CompanySettings {
  company: { name: string; taxId: string | null; phone: string | null; address: string | null; englishName: string | null; logoFileId: string | null; logoUrl: string | null };
  defaultTaxRate: number;
  defaultCurrency: string;
  quoteValidDays: number;
  defaultTerms: string | null;
  fiscalYearStartMonth: number;
  quoteNumberPattern: string;
  quoteNumberPreview: string;
}

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

export const getCompanySettingsApi = () => apiRequest<CompanySettings>('/api/admin/settings');
export const updateCompanySettingsApi = (body: CompanySettingsInput) => apiRequest('/api/admin/settings', { method: 'PUT', body });

/** 上傳公司 Logo：向 API 申請 → PUT 到 file-server；回傳檔案 Id，再以 updateCompanySettingsApi 的 logoFileId 套用。 */
export async function uploadCompanyLogoApi(file: File): Promise<string> {
  const session = await apiRequest<UploadSession>('/api/admin/settings/logo-upload', { method: 'POST', body: { fileName: file.name, size: file.size } });
  await putToFileServer(session, file);
  return session.fileId;
}

/**
 * 單號規則預覽（與後端 DocumentNumberPattern 相同的代號），只用來即時顯示；是否合法以後端檢查為準。
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
