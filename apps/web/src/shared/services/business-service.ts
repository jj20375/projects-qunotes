/**
 * @file business-service.ts
 * 客戶管理、專案主檔與組織內部通訊錄 API。
 *
 * 安全原則：所有公司範圍與資料範圍（own / department / department_tree / company）均由後端依已驗證之 JWT 決定。
 */
import { apiRequest } from '@/shared/api/http-client';

// ---------- 通訊錄 ----------

/**
 * 簡易通訊錄使用者資訊
 */
export interface DirectoryUser {
  /** 使用者 ID */
  userId: string;
  /** 顯示姓名 */
  displayName: string;
  /** 所屬部門 ID */
  departmentId: string | null;
  /** 所屬部門名稱 */
  departmentName: string | null;
}

/**
 * 通訊錄部門結構
 */
export interface DirectoryDepartment {
  /** 部門 ID */
  id: string;
  /** 上層部門 ID */
  parentId: string | null;
  /** 部門名稱 */
  name: string;
}

/**
 * 人員遠端搜尋（下拉選單用）
 *
 * @param query 查詢條件（關鍵字比對姓名與部門、包含/排除特定 ID、數量上限）
 * @returns 傳回符合條件之使用者清單
 */
export function searchDirectoryUsersApi(query: { keyword?: string; ids?: string[]; excludeIds?: string[]; limit?: number }): Promise<DirectoryUser[]> {
  return apiRequest<DirectoryUser[]>('/api/directory/users', { query });
}

/**
 * 取得通訊錄部門清單（供下拉選單或樹狀結構使用）
 *
 * @returns 傳回部門陣列
 */
export function listDirectoryDepartmentsApi(): Promise<DirectoryDepartment[]> {
  return apiRequest<DirectoryDepartment[]>('/api/directory/departments');
}

/** 選專案成員的名單（表格）：姓名、部門、角色；篩選與分頁都由後端處理。 */
export interface DirectoryMember extends DirectoryUser {
  roleId: string;
  roleName: string;
}

/** 通訊錄成員查詢參數 */
export type DirectoryMemberQuery = {
  keyword?: string;
  departmentId?: string;
  includeSubDepartments?: boolean;
  roleId?: string;
  excludeIds?: string[];
  page?: number;
  pageSize?: number;
};

/**
 * 分頁查詢公司通訊錄成員名單（成員選擇彈窗表格用）
 *
 * @param query 查詢與分頁條件
 * @returns 傳回分頁成員清單
 */
export function listDirectoryMembersApi(query: DirectoryMemberQuery): Promise<{ total: number; page: number; pageSize: number; items: DirectoryMember[] }> {
  return apiRequest('/api/directory/members', { query });
}

/**
 * 取得公司內部角色清單（下拉選單用）
 *
 * @returns 傳回角色 ID 與名稱陣列
 */
export function listDirectoryRolesApi(): Promise<{ id: string; name: string }[]> {
  return apiRequest<{ id: string; name: string }[]>('/api/directory/roles');
}

// ---------- 客戶 ----------

/**
 * 客戶列表列項目
 */
export interface ClientRow {
  /** 客戶 ID */
  id: string;
  /** 客戶代碼 */
  code: string;
  /** 客戶名稱 */
  name: string;
  /** 客戶簡稱 */
  shortName: string | null;
  /** 統一編號 */
  taxId: string | null;
  /** 聯絡電話 */
  phone: string | null;
  /** 客戶狀態：active 或 inactive */
  status: 'active' | 'inactive';
  /** 負責業務 ID */
  ownerUserId: string | null;
  /** 負責業務姓名 */
  ownerName: string | null;
  /** 客戶歸屬部門 ID */
  departmentId: string | null;
  /** 客戶歸屬部門名稱 */
  departmentName: string | null;
  /** 主要聯絡人姓名 */
  primaryContact: string | null;
  /** 關聯專案總數 */
  projectCount: number;
  /** 當前操作者是否具備修改權限 */
  canEdit: boolean;
}

/**
 * 客戶聯絡人明細
 */
export interface ClientContact {
  /** 聯絡人 ID */
  id?: string;
  /** 姓名 */
  name: string;
  /** 職稱 */
  title?: string | null;
  /** 公司電話 */
  phone?: string | null;
  /** 行動電話 */
  mobile?: string | null;
  /** 電子郵件信箱 */
  email?: string | null;
  /** 是否為主要聯絡人 */
  isPrimary: boolean;
  /** 備註說明 */
  note?: string | null;
}

/**
 * 客戶主檔完整詳細資料
 */
export interface ClientDetail {
  id: string;
  code: string;
  name: string;
  shortName: string | null;
  taxId: string | null;
  parentClientId: string | null;
  parentName: string | null;
  industry: string | null;
  phone: string | null;
  fax: string | null;
  email: string | null;
  address: string | null;
  invoiceTitle: string | null;
  invoiceAddress: string | null;
  paymentTerms: string | null;
  ownerUserId: string | null;
  ownerName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  /** 只有具備全公司範圍權限之管理者可變更客戶所屬部門 */
  canChangeDepartment: boolean;
  status: 'active' | 'inactive';
  note: string | null;
  canEdit: boolean;
  canDelete: boolean;
  contacts: ClientContact[];
}

/** 客戶新增或修改輸入資料型別 */
export type ClientInput = Omit<ClientDetail, 'id' | 'parentName' | 'ownerName' | 'departmentName' | 'canChangeDepartment' | 'canEdit' | 'canDelete'>;

/** 通用分頁容器介面 */
export interface Paged<T> {
  total: number;
  page: number;
  pageSize: number;
  items: T[];
}

/**
 * 查詢客戶列表
 *
 * @param query 查詢條件（關鍵字、狀態、業務負責人、分頁）
 * @returns 傳回分頁客戶清單
 */
export function searchClientsApi(query: { keyword?: string; status?: string; ownerUserId?: string; page?: number; pageSize?: number }): Promise<Paged<ClientRow>> {
  return apiRequest<Paged<ClientRow>>('/api/clients', { query });
}

/**
 * 取得指定客戶完整明細主檔（含聯絡人清單）
 *
 * @param id 客戶唯一識別碼
 * @returns 傳回客戶詳細資訊
 */
export function getClientApi(id: string): Promise<ClientDetail> {
  return apiRequest<ClientDetail>(`/api/clients/${id}`);
}

/**
 * 新增客戶主檔
 *
 * @param body 客戶輸入資料
 * @returns 傳回新建立客戶 ID 與代碼
 */
export function createClientApi(body: Partial<ClientInput>): Promise<{ id: string; code: string }> {
  return apiRequest<{ id: string; code: string }>('/api/clients', { method: 'POST', body });
}

/**
 * 修改客戶主檔
 *
 * @param id 客戶識別碼
 * @param body 欲更新之客戶資料
 * @returns Promise<void>
 */
export function updateClientApi(id: string, body: Partial<ClientInput>): Promise<void> {
  return apiRequest(`/api/clients/${id}`, { method: 'PUT', body });
}

/**
 * 刪除客戶（若已有關聯專案或報價，後端將阻擋刪除）
 *
 * @param id 客戶識別碼
 * @returns Promise<void>
 */
export function deleteClientApi(id: string): Promise<void> {
  return apiRequest(`/api/clients/${id}`, { method: 'DELETE' });
}

// ---------- 專案 ----------

/** 專案搜尋條件 */
export interface ProjectSearchQuery {
  keyword?: string;
  status?: string[];
  dateField?: 'created' | 'start' | 'end';
  from?: string;
  to?: string;
  amountField?: 'expected' | 'contract';
  amountMin?: number | null;
  amountMax?: number | null;
  clientId?: string;
  ownerUserId?: string;
  departmentId?: string;
  mine?: boolean;
  page?: number;
  pageSize?: number;
  sort?: string;
  desc?: boolean;
}

/** 專案列表列項目 */
export interface ProjectRow {
  id: string;
  projectNo: string;
  name: string;
  status: string;
  client: { id: string; code: string; name: string };
  departmentId: string | null;
  departmentName: string | null;
  owner: { id: string; displayName: string };
  startDate: string | null;
  endDate: string | null;
  expectedAmount: number | null;
  contractAmount: number | null;
  contractTotal: number | null;
  currency: string;
  createdAt: string;
  memberCount: number;
}

/** 專案查詢結果（含統計合計） */
export interface ProjectSearchResult extends Paged<ProjectRow> {
  statusCounts: Record<string, number>;
  sumExpectedAmount: number;
  sumContractAmount: number;
}

/** 專案參與成員 */
export interface ProjectMember {
  userId: string;
  displayName?: string;
  departmentName?: string | null;
  projectRole: 'manager' | 'member' | 'viewer';
}

/** 專案完整詳細主檔 */
export interface ProjectDetail {
  id: string;
  projectNo: string;
  name: string;
  status: string;
  client: { id: string; code: string; name: string; isDeleted: boolean };
  departmentId: string | null;
  departmentName: string | null;
  owner: { id: string; displayName: string };
  startDate: string | null;
  endDate: string | null;
  expectedAmount: number | null;
  contractAmount: number | null;
  contractTotal: number | null;
  currency: string;
  description: string | null;
  wonAt: string | null;
  closedAt: string | null;
  createdAt: string;
  updatedAt: string;
  myRole: string | null;
  canWrite: boolean;
  canManageMembers: boolean;
  canDelete: boolean;
  nextStatuses: string[];
  members: (ProjectMember & { displayName: string; addedAt: string })[];
  history: { fromStatus: string | null; toStatus: string; reason: string | null; changedAt: string; changedBy: string | null }[];
  summary: { orderTotal: number | null; invoicedTotal: number | null; paidTotal: number | null; activeWarranties: number | null };
}

/** 專案表單輸入結構 */
export interface ProjectInput {
  name: string;
  clientId: string;
  departmentId?: string | null;
  ownerUserId?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  expectedAmount?: number | null;
  contractAmount?: number | null;
  contractTotal?: number | null;
  description?: string | null;
  members?: ProjectMember[];
}

/**
 * 條件搜尋與分頁查詢專案列表
 *
 * @param query 查詢條件物件
 * @returns 傳回分頁列表與狀態統計
 */
export function searchProjectsApi(query: ProjectSearchQuery): Promise<ProjectSearchResult> {
  const params = new URLSearchParams();
  Object.entries(query).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return;
    if (Array.isArray(value)) value.forEach((item) => params.append(key, item));
    else params.set(key, String(value));
  });
  return apiRequest(`/api/projects?${params}`);
}

/**
 * 取得指定專案完整詳細資料（包含階段統計與成員清單）
 *
 * @param id 專案識別碼
 * @returns 傳回專案詳細資訊
 */
export function getProjectApi(id: string): Promise<ProjectDetail> {
  return apiRequest<ProjectDetail>(`/api/projects/${id}`);
}

/**
 * 建立新專案
 *
 * @param body 專案輸入資料
 * @returns 傳回新建立專案 ID 與單號
 */
export function createProjectApi(body: ProjectInput): Promise<{ id: string; projectNo: string }> {
  return apiRequest<{ id: string; projectNo: string }>('/api/projects', { method: 'POST', body });
}

/**
 * 更新專案基本資料
 *
 * @param id 專案識別碼
 * @param body 欲更新之欄位資料
 * @returns Promise<void>
 */
export function updateProjectApi(id: string, body: ProjectInput): Promise<void> {
  return apiRequest(`/api/projects/${id}`, { method: 'PUT', body });
}

/**
 * 變更專案業務流程狀態（例如草稿轉報價中、得標等）
 *
 * @param id 專案識別碼
 * @param status 目標狀態代碼
 * @param reason 變更原因（未得標或取消時建議填寫）
 * @returns Promise<void>
 */
export function changeProjectStatusApi(id: string, status: string, reason?: string): Promise<void> {
  return apiRequest(`/api/projects/${id}/status`, { method: 'POST', body: { status, reason } });
}

/**
 * 刪除專案（若有報價或下游單據則不允許刪除）
 *
 * @param id 專案識別碼
 * @returns Promise<void>
 */
export function deleteProjectApi(id: string): Promise<void> {
  return apiRequest(`/api/projects/${id}`, { method: 'DELETE' });
}

/**
 * 整批更新專案參與成員清單與指派角色
 *
 * @param id 專案識別碼
 * @param members 成員清單與角色
 * @returns Promise<void>
 */
export function replaceProjectMembersApi(id: string, members: ProjectMember[]): Promise<void> {
  return apiRequest(`/api/projects/${id}/members`, { method: 'PUT', body: members.map(({ userId, projectRole }) => ({ userId, projectRole })) });
}
