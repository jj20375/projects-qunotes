/**
 * @file document-service.ts
 * 專案的訂單、出貨單、保固單、發票與附件 API。
 * 層級：定案報價 → 訂單 → 出貨單／發票／保固單；新增時對應的報價或訂單必填（後端檢查）。
 *
 * 附件上傳流程：向 API 申請上傳（API 檢查權限、簽發短效 token）→ 直接 PUT 到 file-server →
 * file-server 驗證並存檔後回呼 API；完成前附件不會出現在列表。
 */
import { apiRequest } from '@/shared/api/http-client';

export interface Order {
  id: string;
  quoteId: string | null;
  quoteNo: string | null;
  orderNo: string;
  orderDate: string;
  amount: number;
  taxAmount: number;
  total: number;
  status: 'received' | 'in_progress' | 'delivered' | 'cancelled';
  note: string | null;
  attachmentCount: number;
}

export interface Shipment {
  id: string;
  orderId: string;
  orderNo: string | null;
  shipmentNo: string;
  shipDate: string;
  status: 'shipped' | 'delivered' | 'cancelled';
  receiverName: string | null;
  deliveryAddress: string | null;
  trackingNo: string | null;
  note: string | null;
  attachmentCount: number;
}

export interface Warranty {
  id: string;
  orderId: string | null;
  orderNo: string | null;
  warrantyNo: string;
  subject: string;
  startDate: string;
  endDate: string;
  terms: string | null;
  note: string | null;
  state: 'active' | 'expiring' | 'expired';
  attachmentCount: number;
}

export interface Invoice {
  id: string;
  orderId: string | null;
  orderNo: string | null;
  invoiceNo: string;
  invoiceDate: string;
  invoiceType: 'duplicate' | 'triplicate' | 'e_invoice';
  buyerName: string | null;
  buyerTaxId: string | null;
  salesAmount: number;
  taxAmount: number;
  totalAmount: number;
  status: 'issued' | 'void';
  paymentStatus: 'unpaid' | 'partial' | 'paid';
  paidAmount: number;
  dueDate: string | null;
  paidAt: string | null;
  voidReason: string | null;
  note: string | null;
  isOverdue: boolean;
  attachmentCount: number;
}

interface Permissions {
  canCreate: boolean;
  canUpdate: boolean;
  canDelete: boolean;
}

/** 專案子資料列表的查詢條件；搜尋與分頁都由後端處理。 */
export type ListParams = {
  keyword?: string;
  page?: number;
  pageSize?: number;
};

interface PagedResult {
  total: number;
  page: number;
  pageSize: number;
}

/** activeOnly：排除已取消的訂單（由後端過濾）；activeTotal 是整個專案（不含已取消）的訂單合計，不受分頁影響。 */
export const listOrdersApi = (projectId: string, query: ListParams & { activeOnly?: boolean } = {}) =>
  apiRequest<Permissions & PagedResult & { activeTotal: number; items: Order[] }>(`/api/projects/${projectId}/orders`, { query });
export const saveOrderApi = (projectId: string, order: Partial<Order>) =>
  order.id ? apiRequest(`/api/orders/${order.id}`, { method: 'PUT', body: order }) : apiRequest(`/api/projects/${projectId}/orders`, { method: 'POST', body: order });
export const deleteOrderApi = (id: string) => apiRequest(`/api/orders/${id}`, { method: 'DELETE' });

export const listShipmentsApi = (projectId: string, query: ListParams = {}) =>
  apiRequest<Permissions & PagedResult & { items: Shipment[] }>(`/api/projects/${projectId}/shipments`, { query });
export const saveShipmentApi = (projectId: string, shipment: Partial<Shipment>) =>
  shipment.id ? apiRequest(`/api/shipments/${shipment.id}`, { method: 'PUT', body: shipment }) : apiRequest(`/api/projects/${projectId}/shipments`, { method: 'POST', body: shipment });
export const deleteShipmentApi = (id: string) => apiRequest(`/api/shipments/${id}`, { method: 'DELETE' });

export const listWarrantiesApi = (projectId: string, query: ListParams = {}) =>
  apiRequest<Permissions & PagedResult & { items: Warranty[] }>(`/api/projects/${projectId}/warranties`, { query });
export const saveWarrantyApi = (projectId: string, warranty: Partial<Warranty>) =>
  warranty.id ? apiRequest(`/api/warranties/${warranty.id}`, { method: 'PUT', body: warranty }) : apiRequest(`/api/projects/${projectId}/warranties`, { method: 'POST', body: warranty });
export const deleteWarrantyApi = (id: string) => apiRequest(`/api/warranties/${id}`, { method: 'DELETE' });

export const listInvoicesApi = (projectId: string, query: ListParams = {}) =>
  apiRequest<Permissions & PagedResult & { canMarkPaid: boolean; canVoid: boolean; totalAmount: number; paidAmount: number; items: Invoice[] }>(`/api/projects/${projectId}/invoices`, { query });
export const saveInvoiceApi = (projectId: string, invoice: Partial<Invoice>) =>
  invoice.id ? apiRequest(`/api/invoices/${invoice.id}`, { method: 'PUT', body: invoice }) : apiRequest(`/api/projects/${projectId}/invoices`, { method: 'POST', body: invoice });
/** 發票號碼即時查詢：已被使用時帶出那張發票與它的訂單、專案；屬於看不到的專案時只回已被使用（visible=false）。 */
export interface InvoiceLookup {
  exists: boolean;
  visible?: boolean;
  invoiceNo: string;
  invoice?: Omit<Invoice, 'orderId' | 'orderNo' | 'isOverdue' | 'attachmentCount'> & { id: string };
  order?: { id: string; orderNo: string; orderDate: string; amount: number; taxAmount: number; total: number; status: Order['status']; quoteId: string | null; quoteNo: string | null } | null;
  project?: { id: string; projectNo: string; name: string };
}
export const lookupInvoiceNoApi = (invoiceNo: string, excludeId?: string) => apiRequest<InvoiceLookup>('/api/invoices/lookup', { query: { invoiceNo, excludeId } });
export const recordPaymentApi = (id: string, paidAmount: number, paidAt?: string) => apiRequest(`/api/invoices/${id}/payment`, { method: 'POST', body: { paidAmount, paidAt } });
export const voidInvoiceApi = (id: string, reason: string) => apiRequest(`/api/invoices/${id}/void`, { method: 'POST', body: { reason } });
export const deleteInvoiceApi = (id: string) => apiRequest(`/api/invoices/${id}`, { method: 'DELETE' });

// ---------- 附件 ----------

export type AttachmentOwner = 'project' | 'quote' | 'order' | 'shipment' | 'warranty' | 'invoice';

export interface AttachmentRow {
  id: string;
  category: string | null;
  note: string | null;
  createdAt: string;
  createdBy: string | null;
  fileName: string;
  contentType: string;
  size: number;
}

export const ACCEPTED_FILE_TYPES = '.pdf,.png,.jpg,.jpeg,.webp,.heic,.xlsx,.docx';
export const MAX_FILE_BYTES = 20 * 1024 * 1024;

export const listAttachmentsApi = (ownerType: AttachmentOwner, ownerId: string) =>
  apiRequest<{ canUpload: boolean; canDownload: boolean; canDelete: boolean; items: AttachmentRow[] }>('/api/attachments', { query: { ownerType, ownerId } });
export const getDownloadUrlApi = (id: string, inline: boolean) => apiRequest<{ url: string }>(`/api/attachments/${id}/download`, { query: { inline } });
export const deleteAttachmentApi = (id: string) => apiRequest(`/api/attachments/${id}`, { method: 'DELETE' });

/** 申請上傳後直接傳到 file-server；用 XHR 才能回報上傳進度。 */
export async function uploadAttachment(ownerType: AttachmentOwner, ownerId: string, file: File, category: string | undefined, onProgress: (percent: number) => void): Promise<void> {
  const session = await apiRequest<UploadSession>('/api/files/upload-sessions', {
    method: 'POST',
    body: { ownerType, ownerId, fileName: file.name, size: file.size, category },
  });
  await putToFileServer(session, file, onProgress);
}

export interface UploadSession {
  fileId: string;
  uploadUrl: string;
  token: string;
}

/** 依 API 簽發的上傳 token 把檔案 PUT 到 file-server；file-server 會等 API 確認完成才回應，所以結束時檔案已可使用。 */
export function putToFileServer(session: UploadSession, file: File, onProgress: (percent: number) => void = () => {}): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', session.uploadUrl);
    xhr.setRequestHeader('authorization', `Bearer ${session.token}`);
    xhr.upload.onprogress = (event) => event.lengthComputable && onProgress(Math.round((event.loaded / event.total) * 100));
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) return resolve();
      let message = `上傳失敗（${xhr.status}）`;
      try {
        message = (JSON.parse(xhr.responseText) as { message?: string }).message ?? message;
      } catch {
        // 非 JSON 回應時沿用預設訊息。
      }
      reject(new Error(message));
    };
    xhr.onerror = () => reject(new Error('網路中斷，上傳失敗。'));
    xhr.send(file);
  });
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
