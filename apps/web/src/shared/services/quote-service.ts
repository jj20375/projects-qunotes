/**
 * @file quote-service.ts
 * 報價單、匯入、舊報價對照設定與品項價目 API。
 */
import { apiRequest } from '@/shared/api/http-client';
import type { Paged } from '@/shared/services/business-service';
import { uploadFile } from '@/shared/utils/download';

export interface QuoteItem {
  id?: string;
  lineType: 'item' | 'section' | 'note';
  productId?: string | null;
  itemCode?: string | null;
  name: string;
  spec?: string | null;
  unit?: string | null;
  quantity?: number | null;
  unitPrice?: number | null;
  discountRate?: number | null;
  amount?: number;
  unitCost?: number | null;
  remark?: string | null;
}

export interface QuoteHeader {
  title?: string | null;
  clientId?: string | null;
  quoteDate?: string | null;
  validUntil?: string | null;
  clientName?: string | null;
  clientTaxId?: string | null;
  clientAddress?: string | null;
  contactName?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
  currency?: string;
  discountType?: 'none' | 'percent' | 'amount';
  discountValue?: number | null;
  taxType?: 'taxable' | 'zero' | 'exempt';
  taxIncluded?: boolean;
  taxRate?: number | null;
  paymentTerms?: string | null;
  deliveryTerms?: string | null;
  terms?: string | null;
  note?: string | null;
}

export interface QuoteInput extends QuoteHeader {
  items: QuoteItem[];
}

export type QuoteAction = 'save' | 'submit' | 'delete' | 'approve' | 'return' | 'send' | 'accept' | 'reject' | 'revise';

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

export function searchQuotesApi(query: QuoteSearchQuery): Promise<Paged<QuoteRow>> {
  const params = new URLSearchParams();
  Object.entries(query).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return;
    if (Array.isArray(value)) value.forEach((item) => params.append(key, item));
    else params.set(key, String(value));
  });
  return apiRequest(`/api/quotes?${params}`);
}

/** 把貼上的條列文字拆成條款清單（與後端儲存時的規則相同）。 */
export const normalizeTermsApi = (text: string) => apiRequest<{ items: string[] }>('/api/quote-terms/normalize', { method: 'POST', body: { text } });

/** 專案底下的報價（含舊版本，依單號、版次排序）；關鍵字與分頁由後端處理。 */
export const listProjectQuotesApi = (projectId: string, query: { keyword?: string; page?: number; pageSize?: number } = {}) =>
  apiRequest<Paged<QuoteRow>>(`/api/projects/${projectId}/quotes`, { query });
export const getQuoteApi = (id: string) => apiRequest<QuoteDetail>(`/api/quotes/${id}`);
export const createQuoteApi = (projectId: string, body: QuoteInput) => apiRequest<{ id: string; quoteNo: string }>(`/api/projects/${projectId}/quotes`, { method: 'POST', body });
export const updateQuoteApi = (id: string, body: QuoteInput) => apiRequest(`/api/quotes/${id}`, { method: 'PUT', body });
export const deleteQuoteApi = (id: string) => apiRequest(`/api/quotes/${id}`, { method: 'DELETE' });
export const transitionQuoteApi = (id: string, action: string, reason?: string) => apiRequest(`/api/quotes/${id}/${action}`, { method: 'POST', body: { reason } });
export const reviseQuoteApi = (id: string) => apiRequest<{ id: string; revisionNo: number }>(`/api/quotes/${id}/revise`, { method: 'POST' });

// ---------- 匯入 ----------

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

export interface ImportJob {
  id: string;
  kind: ImportKind;
  fileName: string;
  status: string;
  createdQuoteId: string | null;
  preview: {
    /** 表頭欄位以 PascalCase 為 key（與後端 QuoteInput 屬性同名）。 */
    header: Record<string, string | null>;
    items: ParsedQuoteItem[];
    warnings: string[];
    detectedColumns: Record<string, string>;
    sheetName: string;
    headerRow: number;
  };
}

export type ImportKind = 'excel_template' | 'legacy_excel' | 'ocr';

/** 伺服器是否已設定圖像識別的金鑰，以及使用哪一家（claude／gemini）。 */
export const getQuoteImportOptionsApi = () => apiRequest<{ ocrEnabled: boolean; ocrProvider: 'claude' | 'gemini' | null }>('/api/quote-imports/options');

export function uploadQuoteImportApi(file: File, kind: string, mappingProfileId?: string): Promise<ImportJob> {
  const form = new FormData();
  form.append('file', file);
  form.append('kind', kind);
  if (mappingProfileId) form.append('mappingProfileId', mappingProfileId);
  return uploadFile('/api/quote-imports', form);
}

export const confirmQuoteImportApi = (jobId: string, projectId: string, quote: QuoteInput) =>
  apiRequest<{ id: string; quoteNo: string }>(`/api/quote-imports/${jobId}/confirm`, { method: 'POST', body: { projectId, quote } });

export interface MappingProfile {
  id?: string;
  name: string;
  sheetName?: string | null;
  headerRow: number;
  dataStartRow: number;
  columns: Record<string, string>;
  cells: Record<string, string>;
}

export const listMappingProfilesApi = () => apiRequest<MappingProfile[]>('/api/import-mapping-profiles');
export const saveMappingProfileApi = (profile: MappingProfile) =>
  profile.id ? apiRequest(`/api/import-mapping-profiles/${profile.id}`, { method: 'PUT', body: profile }) : apiRequest('/api/import-mapping-profiles', { method: 'POST', body: profile });
export const deleteMappingProfileApi = (id: string) => apiRequest(`/api/import-mapping-profiles/${id}`, { method: 'DELETE' });

// ---------- 品項 ----------

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

export const searchProductsApi = (query: { keyword?: string; activeOnly?: boolean; page?: number; pageSize?: number }) => apiRequest<Paged<ProductRow>>('/api/products', { query });
export const saveProductApi = (product: Partial<ProductRow>) =>
  product.id ? apiRequest(`/api/products/${product.id}`, { method: 'PUT', body: product }) : apiRequest('/api/products', { method: 'POST', body: product });
export const deleteProductApi = (id: string) => apiRequest<{ deleted: boolean; deactivated: boolean }>(`/api/products/${id}`, { method: 'DELETE' });

// ---------- 客戶 Excel ----------

export interface ClientImportResult {
  dryRun: boolean;
  summary: { created: number; updated: number; errors: number };
  rows: { row: number; action: 'create' | 'update' | 'error'; code: string | null; name: string | null; message: string | null }[];
}

export function importClientsApi(file: File, dryRun: boolean): Promise<ClientImportResult> {
  const form = new FormData();
  form.append('file', file);
  return uploadFile(`/api/clients/import?dryRun=${dryRun}`, form);
}
