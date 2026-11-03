/**
 * @file quote-service.ts
 * 報價單、智慧匯入、舊報價對照範本與品項價目庫 API。
 */
import { apiRequest } from '@/shared/api/http-client';
import type { Paged } from '@/shared/services/business-service';
import { uploadFile } from '@/shared/utils/download';

/**
 * 報價明細列資料結構
 */
export interface QuoteItem {
  /** 明細識別碼 */
  id?: string;
  /** 列型別：item（標準品項）、section（分組標題）、note（附註說明） */
  lineType: 'item' | 'section' | 'note';
  /** 關聯品項主檔 ID（若自建則為 null） */
  productId?: string | null;
  /** 品號代碼 */
  itemCode?: string | null;
  /** 品名 */
  name: string;
  /** 規格說明 */
  spec?: string | null;
  /** 計量單位 */
  unit?: string | null;
  /** 數量 */
  quantity?: number | null;
  /** 單價 */
  unitPrice?: number | null;
  /** 折扣率（百分比） */
  discountRate?: number | null;
  /** 本列合計金額 */
  amount?: number;
  /** 單位成本（僅具成本權限者可見） */
  unitCost?: number | null;
  /** 備註 */
  remark?: string | null;
}

/**
 * 報價表頭輸入與快照欄位
 */
export interface QuoteHeader {
  /** 報價單抬頭標題 */
  title?: string | null;
  /** 客戶 ID（null 代表自由填寫） */
  clientId?: string | null;
  /** 報價日期（YYYY-MM-DD） */
  quoteDate?: string | null;
  /** 報價有效截止日期（YYYY-MM-DD） */
  validUntil?: string | null;
  /** 客戶名稱快照 */
  clientName?: string | null;
  /** 客戶統一編號快照 */
  clientTaxId?: string | null;
  /** 客戶地址快照 */
  clientAddress?: string | null;
  /** 聯絡人姓名快照 */
  contactName?: string | null;
  /** 聯絡人電話快照 */
  contactPhone?: string | null;
  /** 聯絡人電子郵件快照 */
  contactEmail?: string | null;
  /** 幣別（如 'TWD', 'USD'） */
  currency?: string;
  /** 整體折扣方式：none（無）、percent（比例）、amount（固定折讓） */
  discountType?: 'none' | 'percent' | 'amount';
  /** 折扣數值 */
  discountValue?: number | null;
  /** 稅別：taxable（應稅）、zero（零稅率）、exempt（免稅） */
  taxType?: 'taxable' | 'zero' | 'exempt';
  /** 單價是否含稅 */
  taxIncluded?: boolean;
  /** 營業稅率百分比 */
  taxRate?: number | null;
  /** 付款條件說明 */
  paymentTerms?: string | null;
  /** 交貨條件說明 */
  deliveryTerms?: string | null;
  /** 交易備註條款 */
  terms?: string | null;
  /** 內部備註 */
  note?: string | null;
}

/**
 * 報價單完整輸入資料結構
 */
export interface QuoteInput extends QuoteHeader {
  /** 報價品項明細清單 */
  items: QuoteItem[];
}

/** 報價狀態流程操作動作代碼 */
export type QuoteAction = 'save' | 'submit' | 'delete' | 'approve' | 'return' | 'send' | 'accept' | 'reject' | 'revise';

/**
 * 報價單完整詳細主檔（包含版本歷史與金額小計）
 */
export interface QuoteDetail extends Required<Omit<QuoteHeader, 'clientId'>> {
  id: string;
  quoteNo: string;
  revisionNo: number;
  revisionOfId: string | null;
  isLatest: boolean;
  status: string;
  statusReason: string | null;
  isExpired: boolean;
  project: { id: string; projectNo: string; name: string; status: string };
  clientId: string;
  subtotal: number;
  discountAmount: number;
  netAmount: number;
  taxAmount: number;
  total: number;
  costTotal: number | null;
  grossMargin: number | null;
  source: string;
  createdBy: string | null;
  createdAt: string;
  submittedBy: string | null;
  submittedAt: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  sentAt: string | null;
  acceptedAt: string | null;
  rejectedAt: string | null;
  canSeeCost: boolean;
  actions: QuoteAction[];
  revisions: { id: string; revisionNo: number; status: string; total: number; isLatest: boolean }[];
  items: QuoteItem[];
}

/**
 * 報價列表列項目
 */
export interface QuoteRow {
  id: string;
  quoteNo: string;
  revisionNo: number;
  isLatest: boolean;
  status: string;
  title: string | null;
  quoteDate: string;
  validUntil: string | null;
  clientName: string;
  projectId: string;
  projectNo: string;
  projectName: string;
  currency: string;
  netAmount: number;
  total: number;
  createdAt: string;
  isExpired: boolean;
}

/** 報價搜尋查詢條件 */
export interface QuoteSearchQuery {
  keyword?: string;
  status?: string[];
  projectId?: string;
  clientId?: string;
  from?: string;
  to?: string;
  amountMin?: number | null;
  amountMax?: number | null;
  latestOnly?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * 跨專案條件搜尋與分頁查詢報價單清單
 *
 * @param query 查詢條件物件
 * @returns 傳回分頁報價單列表
 */
export function searchQuotesApi(query: QuoteSearchQuery): Promise<Paged<QuoteRow>> {
  const params = new URLSearchParams();
  Object.entries(query).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return;
    if (Array.isArray(value)) value.forEach((item) => params.append(key, item));
    else params.set(key, String(value));
  });
  return apiRequest(`/api/quotes?${params}`);
}

/**
 * 把貼上的條列文字自動正規化拆分為條款陣列
 *
 * @param text 使用者貼上之條款文字
 * @returns 傳回拆分後之字串陣列
 */
export function normalizeTermsApi(text: string): Promise<{ items: string[] }> {
  return apiRequest<{ items: string[] }>('/api/quote-terms/normalize', { method: 'POST', body: { text } });
}

/**
 * 查詢指定專案所屬之全部報價單（包含歷史改版紀錄，依單號與版次排序）
 *
 * @param projectId 專案識別碼
 * @param query 分頁與關鍵字條件
 * @returns 傳回分頁報價單列表
 */
export function listProjectQuotesApi(
  projectId: string,
  query: { keyword?: string; page?: number; pageSize?: number } = {}
): Promise<Paged<QuoteRow>> {
  return apiRequest<Paged<QuoteRow>>(`/api/projects/${projectId}/quotes`, { query });
}

/**
 * 取得指定報價單完整明細與品項結構
 *
 * @param id 報價單唯一識別碼
 * @returns 傳回報價單詳細資料
 */
export function getQuoteApi(id: string): Promise<QuoteDetail> {
  return apiRequest<QuoteDetail>(`/api/quotes/${id}`);
}

/**
 * 在指定專案底下建立新報價單（初始為草稿狀態）
 *
 * @param projectId 專案識別碼
 * @param body 報價單輸入資料
 * @returns 傳回新建立之 ID 與報價單號
 */
export function createQuoteApi(projectId: string, body: QuoteInput): Promise<{ id: string; quoteNo: string }> {
  return apiRequest<{ id: string; quoteNo: string }>(`/api/projects/${projectId}/quotes`, { method: 'POST', body });
}

/**
 * 更新草稿狀態之報價單內容
 *
 * @param id 報價單識別碼
 * @param body 欲儲存之報價單資料
 * @returns Promise<void>
 */
export function updateQuoteApi(id: string, body: QuoteInput): Promise<void> {
  return apiRequest(`/api/quotes/${id}`, { method: 'PUT', body });
}

/**
 * 刪除草稿報價單
 *
 * @param id 報價單識別碼
 * @returns Promise<void>
 */
export function deleteQuoteApi(id: string): Promise<void> {
  return apiRequest(`/api/quotes/${id}`, { method: 'DELETE' });
}

/**
 * 執行報價單狀態流轉動作（送審、核准、退回、發送客戶、客戶接受、客戶拒絕）
 *
 * @param id 報價單識別碼
 * @param action 動作名稱
 * @param reason 退回或拒絕之原因備註
 * @returns Promise<void>
 */
export function transitionQuoteApi(id: string, action: string, reason?: string): Promise<void> {
  return apiRequest(`/api/quotes/${id}/${action}`, { method: 'POST', body: { reason } });
}

/**
 * 建立報價單的新修訂版本（Revision + 1）
 *
 * 凍結前一版本快照，並產生同單號之新版次草稿。
 *
 * @param id 欲改版之報價單識別碼
 * @returns 傳回新版報價單 ID 與新版次編號
 */
export function reviseQuoteApi(id: string): Promise<{ id: string; revisionNo: number }> {
  return apiRequest<{ id: string; revisionNo: number }>(`/api/quotes/${id}/revise`, { method: 'POST' });
}

// ---------- 匯入 ----------

/**
 * 解析後之匯入品項明細列
 */
export interface ParsedQuoteItem {
  row: number;
  lineType: 'item' | 'section';
  itemCode: string | null;
  name: string;
  spec: string | null;
  unit: string | null;
  quantity: number | null;
  unitPrice: number | null;
  discountRate: number | null;
  unitCost: number | null;
  remark: string | null;
  sheetAmount: number | null;
}

/**
 * 報價匯入解析工作階段與預覽資料
 */
export interface ImportJob {
  id: string;
  kind: ImportKind;
  fileName: string;
  status: string;
  createdQuoteId: string | null;
  preview: {
    header: Record<string, string | null>;
    items: ParsedQuoteItem[];
    warnings: string[];
    detectedColumns: Record<string, string>;
    sheetName: string;
    headerRow: number;
  };
}

/** 匯入檔案類型：excel_template（標準範本）、legacy_excel（自訂對照）、ocr（圖文辨識） */
export type ImportKind = 'excel_template' | 'legacy_excel' | 'ocr';

/**
 * 取得後端 OCR 圖文識別組態設定（檢查是否啟用及供應商）
 *
 * @returns 傳回 OCR 支援狀態
 */
export function getQuoteImportOptionsApi(): Promise<{ ocrEnabled: boolean; ocrProvider: 'claude' | 'gemini' | null }> {
  return apiRequest<{ ocrEnabled: boolean; ocrProvider: 'claude' | 'gemini' | null }>('/api/quote-imports/options');
}

/**
 * 上傳待解析之報價試算表或掃描圖片檔
 *
 * @param file 檔案物件
 * @param kind 匯入類型
 * @param mappingProfileId 自訂對照範本 ID（legacy_excel 時使用）
 * @returns 傳回匯入解析工作物件
 */
export function uploadQuoteImportApi(file: File, kind: string, mappingProfileId?: string): Promise<ImportJob> {
  const form = new FormData();
  form.append('file', file);
  form.append('kind', kind);
  if (mappingProfileId) form.append('mappingProfileId', mappingProfileId);
  return uploadFile('/api/quote-imports', form);
}

/**
 * 確認匯入結果並正式產生報價單
 *
 * @param jobId 匯入工作 ID
 * @param projectId 目標專案識別碼
 * @param quote 確認後之報價資料結構
 * @returns 傳回建立之報價單 ID 與單號
 */
export function confirmQuoteImportApi(jobId: string, projectId: string, quote: QuoteInput): Promise<{ id: string; quoteNo: string }> {
  return apiRequest<{ id: string; quoteNo: string }>(`/api/quote-imports/${jobId}/confirm`, { method: 'POST', body: { projectId, quote } });
}

/**
 * 試算表欄位對照範本結構
 */
export interface MappingProfile {
  id?: string;
  name: string;
  sheetName?: string | null;
  headerRow: number;
  dataStartRow: number;
  columns: Record<string, string>;
  cells: Record<string, string>;
}

/**
 * 取得使用者自訂之 Excel 對照範本清單
 *
 * @returns 傳回對照範本陣列
 */
export function listMappingProfilesApi(): Promise<MappingProfile[]> {
  return apiRequest<MappingProfile[]>('/api/import-mapping-profiles');
}

/**
 * 儲存（新增或更新）自訂 Excel 對照範本
 *
 * @param profile 範本設定內容
 * @returns Promise<void>
 */
export function saveMappingProfileApi(profile: MappingProfile): Promise<void> {
  return profile.id
    ? apiRequest(`/api/import-mapping-profiles/${profile.id}`, { method: 'PUT', body: profile })
    : apiRequest('/api/import-mapping-profiles', { method: 'POST', body: profile });
}

/**
 * 刪除自訂 Excel 對照範本
 *
 * @param id 範本識別碼
 * @returns Promise<void>
 */
export function deleteMappingProfileApi(id: string): Promise<void> {
  return apiRequest(`/api/import-mapping-profiles/${id}`, { method: 'DELETE' });
}

// ---------- 品項主檔 ----------

/**
 * 品項主檔列項目
 */
export interface ProductRow {
  id: string;
  code: string;
  name: string;
  spec: string | null;
  unit: string | null;
  category: string | null;
  defaultPrice: number;
  defaultCost: number | null;
  isActive: boolean;
}

/**
 * 條件搜尋與分頁查詢品項主檔清單
 *
 * @param query 查詢條件
 * @returns 傳回分頁品項資料
 */
export function searchProductsApi(query: { keyword?: string; activeOnly?: boolean; page?: number; pageSize?: number }): Promise<Paged<ProductRow>> {
  return apiRequest<Paged<ProductRow>>('/api/products', { query });
}

/**
 * 新增或更新品項主檔
 *
 * @param product 品項資料
 * @returns Promise<void>
 */
export function saveProductApi(product: Partial<ProductRow>): Promise<void> {
  return product.id
    ? apiRequest(`/api/products/${product.id}`, { method: 'PUT', body: product })
    : apiRequest('/api/products', { method: 'POST', body: product });
}

/**
 * 刪除品項（若已有被報價單引用則自動轉為停用）
 *
 * @param id 品項識別碼
 * @returns 傳回是否實體刪除或轉為停用之狀態
 */
export function deleteProductApi(id: string): Promise<{ deleted: boolean; deactivated: boolean }> {
  return apiRequest<{ deleted: boolean; deactivated: boolean }>(`/api/products/${id}`, { method: 'DELETE' });
}

// ---------- 客戶 Excel 批次匯入 ----------

/** 客戶匯入解析比對結果 */
export interface ClientImportResult {
  dryRun: boolean;
  summary: { created: number; updated: number; errors: number };
  rows: { row: number; action: 'create' | 'update' | 'error'; code: string | null; name: string | null; message: string | null }[];
}

/**
 * 上傳客戶資料 Excel 進行驗證或批次匯入
 *
 * @param file Excel 檔案物件
 * @param dryRun 是否僅為試算預覽（true 不寫入 DB）
 * @returns 傳回比對結果與統計摘要
 */
export function importClientsApi(file: File, dryRun: boolean): Promise<ClientImportResult> {
  const form = new FormData();
  form.append('file', file);
  return uploadFile(`/api/clients/import?dryRun=${dryRun}`, form);
}
