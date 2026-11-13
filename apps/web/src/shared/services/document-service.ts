/**
 * @file document-service.ts
 * 專案的訂單、出貨單、保固單、發票與附件 API。
 *
 * 業務層級：定案報價 → 訂單 → 出貨單／發票／保固單；新增時對應的報價或訂單必填（由後端強制驗證）。
 *
 * 附件上傳流程：向 API 申請上傳（API 檢查權限、簽發短效 token）→ 直接 PUT 到 file-server →
 * file-server 驗證並存檔後回呼 API；完成前附件不會出現在列表。
 */
import { apiRequest } from '@/shared/api/http-client';
import { withBasePath } from '@/shared/utils/base-path';

/**
 * 訂單明細資料結構
 */
export interface Order {
  /** 訂單識別碼 */
  id: string;
  /** 來源報價單 ID */
  quoteId: string | null;
  /** 來源報價單號 */
  quoteNo: string | null;
  /** 訂單編號 */
  orderNo: string;
  /** 訂單成立日期（YYYY-MM-DD） */
  orderDate: string;
  /** 未稅金額 */
  amount: number;
  /** 稅額 */
  taxAmount: number;
  /** 總計金額（含稅） */
  total: number;
  /** 訂單狀態：received（已接單）、in_progress（處理中）、delivered（已出清）、cancelled（已取消） */
  status: 'received' | 'in_progress' | 'delivered' | 'cancelled';
  /** 備註說明 */
  note: string | null;
  /** 關聯附件數量 */
  attachmentCount: number;
}

/**
 * 出貨單明細資料結構
 */
export interface Shipment {
  /** 出貨單識別碼 */
  id: string;
  /** 關聯訂單 ID */
  orderId: string;
  /** 關聯訂單編號 */
  orderNo: string | null;
  /** 出貨單號 */
  shipmentNo: string;
  /** 出貨日期（YYYY-MM-DD） */
  shipDate: string;
  /** 出貨狀態：shipped（已出貨）、delivered（已送達）、cancelled（已取消） */
  status: 'shipped' | 'delivered' | 'cancelled';
  /** 收件人姓名 */
  receiverName: string | null;
  /** 送貨地址 */
  deliveryAddress: string | null;
  /** 物流追蹤碼 / 託運單號 */
  trackingNo: string | null;
  /** 備註說明 */
  note: string | null;
  /** 關聯附件數量 */
  attachmentCount: number;
}

/**
 * 保固單明細資料結構
 */
export interface Warranty {
  /** 保固單識別碼 */
  id: string;
  /** 關聯訂單 ID */
  orderId: string | null;
  /** 關聯訂單編號 */
  orderNo: string | null;
  /** 保固單號 */
  warrantyNo: string;
  /** 保固標的 / 專案主題 */
  subject: string;
  /** 保固起始日（YYYY-MM-DD） */
  startDate: string;
  /** 保固到期日（YYYY-MM-DD） */
  endDate: string;
  /** 保固條款內容 */
  terms: string | null;
  /** 備註說明 */
  note: string | null;
  /** 保固狀態：active（有效中）、expiring（即將到期）、expired（已過期） */
  state: 'active' | 'expiring' | 'expired';
  /** 關聯附件數量 */
  attachmentCount: number;
}

/**
 * 發票明細資料結構
 */
export interface Invoice {
  /** 發票識別碼 */
  id: string;
  /** 關聯訂單 ID */
  orderId: string | null;
  /** 關聯訂單編號 */
  orderNo: string | null;
  /** 發票號碼（如 AB-12345678） */
  invoiceNo: string;
  /** 開立日期（YYYY-MM-DD） */
  invoiceDate: string;
  /** 發票聯式：duplicate（二聯式）、triplicate（三聯式）、e_invoice（電子發票） */
  invoiceType: 'duplicate' | 'triplicate' | 'e_invoice';
  /** 買受人抬頭 */
  buyerName: string | null;
  /** 買受人統一編號 */
  buyerTaxId: string | null;
  /** 銷售金額（未稅） */
  salesAmount: number;
  /** 營業稅額 */
  taxAmount: number;
  /** 發票總額（含稅） */
  totalAmount: number;
  /** 發票開立狀態：issued（正常開立）、void（已作廢） */
  status: 'issued' | 'void';
  /** 款項收訖狀態：unpaid（未收款）、partial（部分收款）、paid（已結清） */
  paymentStatus: 'unpaid' | 'partial' | 'paid';
  /** 已收累計金額 */
  paidAmount: number;
  /** 預計應收日期（YYYY-MM-DD） */
  dueDate: string | null;
  /** 結清收款日期 */
  paidAt: string | null;
  /** 作廢原因說明 */
  voidReason: string | null;
  /** 備註說明 */
  note: string | null;
  /** 是否已超過應收日期（逾期帳款） */
  isOverdue: boolean;
  /** 關聯附件數量 */
  attachmentCount: number;
}

/**
 * 列表權限資訊
 */
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

/** 分頁資訊 */
interface PagedResult {
  total: number;
  page: number;
  pageSize: number;
}

/**
 * 查詢指定專案之訂單列表
 *
 * @param projectId 專案唯一識別碼
 * @param query 查詢條件（關鍵字、分頁、activeOnly 排除取消訂單）
 * @returns 傳回分頁訂單列表與權限資訊
 */
export function listOrdersApi(
  projectId: string,
  query: ListParams & { activeOnly?: boolean } = {}
): Promise<Permissions & PagedResult & { activeTotal: number; items: Order[] }> {
  return apiRequest(`/api/projects/${projectId}/orders`, { query });
}

/** 單筆訂單內容（預覽用）：含所屬專案、來源報價，以及底下發票／出貨單／保固單的筆數與已開發票金額。 */
export interface OrderDetail extends Omit<Order, 'attachmentCount'> {
  project: { id: string; projectNo: string; name: string };
  shipmentCount: number;
  warrantyCount: number;
  invoiceCount: number;
  invoicedAmount: number;
}

/**
 * 取得單筆訂單詳細資訊（包含關聯單據統計）
 *
 * @param id 訂單識別碼
 * @returns 傳回訂單完整資訊
 */
export function getOrderApi(id: string): Promise<OrderDetail> {
  return apiRequest<OrderDetail>(`/api/orders/${id}`);
}

/**
 * 新增或修改專案訂單
 *
 * @param projectId 專案識別碼
 * @param order 訂單資料（若帶有 id 則執行更新，否則執行建立）
 * @returns Promise<any>
 */
export function saveOrderApi(projectId: string, order: Partial<Order>): Promise<any> {
  return order.id
    ? apiRequest(`/api/orders/${order.id}`, { method: 'PUT', body: order })
    : apiRequest(`/api/projects/${projectId}/orders`, { method: 'POST', body: order });
}

/**
 * 刪除指定訂單
 *
 * @param id 訂單識別碼
 * @returns Promise<void>
 */
export function deleteOrderApi(id: string): Promise<void> {
  return apiRequest(`/api/orders/${id}`, { method: 'DELETE' });
}

/**
 * 查詢指定專案之出貨單列表
 *
 * @param projectId 專案識別碼
 * @param query 查詢分頁條件
 * @returns 傳回出貨單列表
 */
export function listShipmentsApi(
  projectId: string,
  query: ListParams = {}
): Promise<Permissions & PagedResult & { items: Shipment[] }> {
  return apiRequest(`/api/projects/${projectId}/shipments`, { query });
}

/**
 * 新增或修改專案出貨單
 *
 * @param projectId 專案識別碼
 * @param shipment 出貨單資料
 * @returns Promise<any>
 */
export function saveShipmentApi(projectId: string, shipment: Partial<Shipment>): Promise<any> {
  return shipment.id
    ? apiRequest(`/api/shipments/${shipment.id}`, { method: 'PUT', body: shipment })
    : apiRequest(`/api/projects/${projectId}/shipments`, { method: 'POST', body: shipment });
}

/**
 * 刪除指定出貨單
 *
 * @param id 出貨單識別碼
 * @returns Promise<void>
 */
export function deleteShipmentApi(id: string): Promise<void> {
  return apiRequest(`/api/shipments/${id}`, { method: 'DELETE' });
}

/**
 * 查詢指定專案之保固單列表
 *
 * @param projectId 專案識別碼
 * @param query 查詢分頁條件
 * @returns 傳回保固單清單
 */
export function listWarrantiesApi(
  projectId: string,
  query: ListParams = {}
): Promise<Permissions & PagedResult & { items: Warranty[] }> {
  return apiRequest(`/api/projects/${projectId}/warranties`, { query });
}

/**
 * 新增或修改專案保固單
 *
 * @param projectId 專案識別碼
 * @param warranty 保固單資料
 * @returns Promise<any>
 */
export function saveWarrantyApi(projectId: string, warranty: Partial<Warranty>): Promise<any> {
  return warranty.id
    ? apiRequest(`/api/warranties/${warranty.id}`, { method: 'PUT', body: warranty })
    : apiRequest(`/api/projects/${projectId}/warranties`, { method: 'POST', body: warranty });
}

/**
 * 刪除指定保固單
 *
 * @param id 保固單識別碼
 * @returns Promise<void>
 */
export function deleteWarrantyApi(id: string): Promise<void> {
  return apiRequest(`/api/warranties/${id}`, { method: 'DELETE' });
}

/**
 * 查詢指定專案之發票列表
 *
 * @param projectId 專案識別碼
 * @param query 查詢分頁條件
 * @returns 傳回發票列表及開立/結案統計金額
 */
export function listInvoicesApi(
  projectId: string,
  query: ListParams = {}
): Promise<Permissions & PagedResult & { canMarkPaid: boolean; canVoid: boolean; totalAmount: number; paidAmount: number; items: Invoice[] }> {
  return apiRequest(`/api/projects/${projectId}/invoices`, { query });
}

/**
 * 新增或修改專案發票
 *
 * @param projectId 專案識別碼
 * @param invoice 發票資料
 * @returns Promise<any>
 */
export function saveInvoiceApi(projectId: string, invoice: Partial<Invoice>): Promise<any> {
  return invoice.id
    ? apiRequest(`/api/invoices/${invoice.id}`, { method: 'PUT', body: invoice })
    : apiRequest(`/api/projects/${projectId}/invoices`, { method: 'POST', body: invoice });
}

/** 發票號碼即時查詢結果 */
export interface InvoiceLookup {
  exists: boolean;
  visible?: boolean;
  invoiceNo: string;
  invoice?: Omit<Invoice, 'orderId' | 'orderNo' | 'isOverdue' | 'attachmentCount'> & { id: string };
  order?: { id: string; orderNo: string; orderDate: string; amount: number; taxAmount: number; total: number; status: Order['status']; quoteId: string | null; quoteNo: string | null } | null;
  project?: { id: string; projectNo: string; name: string };
}

/**
 * 即時檢查發票號碼是否重複
 *
 * @param invoiceNo 發票號碼
 * @param excludeId 排除檢查之發票 ID（修改時傳入本筆 ID）
 * @returns 傳回發票查詢查重結果
 */
export function lookupInvoiceNoApi(invoiceNo: string, excludeId?: string): Promise<InvoiceLookup> {
  return apiRequest<InvoiceLookup>('/api/invoices/lookup', { query: { invoiceNo, excludeId } });
}

/**
 * 登記發票收款紀錄
 *
 * @param id 發票識別碼
 * @param paidAmount 本次實收金額
 * @param paidAt 收款入帳日期時間（選填）
 * @returns Promise<void>
 */
export function recordPaymentApi(id: string, paidAmount: number, paidAt?: string): Promise<void> {
  return apiRequest(`/api/invoices/${id}/payment`, { method: 'POST', body: { paidAmount, paidAt } });
}

/**
 * 將發票標記為作廢
 *
 * @param id 發票識別碼
 * @param reason 作廢原因說明
 * @returns Promise<void>
 */
export function voidInvoiceApi(id: string, reason: string): Promise<void> {
  return apiRequest(`/api/invoices/${id}/void`, { method: 'POST', body: { reason } });
}

/**
 * 刪除指定發票
 *
 * @param id 發票識別碼
 * @returns Promise<void>
 */
export function deleteInvoiceApi(id: string): Promise<void> {
  return apiRequest(`/api/invoices/${id}`, { method: 'DELETE' });
}

// ---------- 附件 ----------

/** 附件關聯擁有者業務實體類型 */
export type AttachmentOwner = 'project' | 'quote' | 'order' | 'shipment' | 'warranty' | 'invoice';

/**
 * 附件列表列項目
 */
export interface AttachmentRow {
  /** 附件識別碼 */
  id: string;
  /** 檔案自訂分類標籤 */
  category: string | null;
  /** 備註說明 */
  note: string | null;
  /** 上傳時間 */
  createdAt: string;
  /** 上傳人員名稱 */
  createdBy: string | null;
  /** 檔案名稱 */
  fileName: string;
  /** MIME 內容類型 */
  contentType: string;
  /** 檔案大小（位元組） */
  size: number;
}

/** 系統允許上傳之檔案副檔名清單 */
export const ACCEPTED_FILE_TYPES = '.pdf,.png,.jpg,.jpeg,.webp,.heic,.xlsx,.docx';
/** 單一檔案上傳大小上限（20 MB） */
export const MAX_FILE_BYTES = 20 * 1024 * 1024;

/**
 * 查詢指定業務單據所屬之全部附件清單
 *
 * @param ownerType 擁有者實體類型（如 'project', 'quote'）
 * @param ownerId 擁有者實體 ID
 * @returns 傳回附件清單與操作權限
 */
export function listAttachmentsApi(
  ownerType: AttachmentOwner,
  ownerId: string
): Promise<{ canUpload: boolean; canDownload: boolean; canDelete: boolean; items: AttachmentRow[] }> {
  return apiRequest('/api/attachments', { query: { ownerType, ownerId } });
}

/**
 * 向後端取得附件下載或在線預覽的授權短效 URL
 *
 * @param id 附件識別碼
 * @param inline 是否於瀏覽器分頁直接預覽開檔（true）或強制下載（false）
 * @returns 傳回帶簽名權杖之下載網址
 */
export function getDownloadUrlApi(id: string, inline: boolean): Promise<{ url: string }> {
  return apiRequest<{ url: string }>(`/api/attachments/${id}/download`, { query: { inline } });
}

/**
 * 刪除指定附件（標記軟刪除並保留供稽核）
 *
 * @param id 附件識別碼
 * @returns Promise<void>
 */
export function deleteAttachmentApi(id: string): Promise<void> {
  return apiRequest(`/api/attachments/${id}`, { method: 'DELETE' });
}

/**
 * 申請上傳工作階段並直接將檔案 PUT 至 file-server
 *
 * @param ownerType 擁有者實體類型
 * @param ownerId 擁有者識別碼
 * @param file 欲上傳之檔案物件
 * @param category 自訂檔案分類標籤
 * @param onProgress 上傳進度回報回呼函式（0~100）
 * @returns Promise<void>
 */
export async function uploadAttachment(
  ownerType: AttachmentOwner,
  ownerId: string,
  file: File,
  category: string | undefined,
  onProgress: (percent: number) => void
): Promise<void> {
  const session = await apiRequest<UploadSession>('/api/files/upload-sessions', {
    method: 'POST',
    body: { ownerType, ownerId, fileName: file.name, size: file.size, category },
  });
  await putToFileServer(session, file, onProgress);
}

/** 上傳工作階段設定資訊 */
export interface UploadSession {
  fileId: string;
  uploadUrl: string;
  token: string;
}

/**
 * 依 API 簽發之上傳 Session 與短效 Token 把檔案 PUT 到 file-server
 *
 * @param session 上傳工作階段資訊
 * @param file 檔案物件
 * @param onProgress 上傳進度回呼
 * @returns Promise<void>
 */
export function putToFileServer(session: UploadSession, file: File, onProgress: (percent: number) => void = () => {}): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    // uploadUrl 由 API 產生，是不含部署前綴的 /files/uploads/...（API 不知道外層掛在哪個路徑）。
    xhr.open('PUT', withBasePath(session.uploadUrl));
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

/**
 * 將檔案位元組大小格式化為易讀之單位（B / KB / MB）
 *
 * @param bytes 位元組整數數值
 * @returns 格式化字串
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
