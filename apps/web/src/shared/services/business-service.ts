/**
 * @file business-service.ts
 * 客戶、專案與公司通訊錄 API。公司範圍與資料範圍都由後端依 JWT 決定。
 */
import { apiRequest } from '@/shared/api/http-client';

// ---------- 通訊錄 ----------

export interface DirectoryUser {
  userId: string;
  displayName: string;
  departmentId: string | null;
  departmentName: string | null;
}

export interface DirectoryDepartment {
  id: string;
  parentId: string | null;
  name: string;
}

/** 人員遠端搜尋：keyword 比對姓名與部門；ids 取回指定人員（顯示已選值）；excludeIds 排除；最多 limit 筆。 */
export const searchDirectoryUsersApi = (query: { keyword?: string; ids?: string[]; excludeIds?: string[]; limit?: number }) =>
  apiRequest<DirectoryUser[]>('/api/directory/users', { query });
export const listDirectoryDepartmentsApi = () => apiRequest<DirectoryDepartment[]>('/api/directory/departments');

/** 選專案成員的名單（表格）：姓名、部門、角色；篩選與分頁都由後端處理。 */
export interface DirectoryMember extends DirectoryUser {
  roleId: string;
  roleName: string;
}
export type DirectoryMemberQuery = {
  keyword?: string;
  departmentId?: string;
  includeSubDepartments?: boolean;
  roleId?: string;
  excludeIds?: string[];
  page?: number;
  pageSize?: number;
};
export const listDirectoryMembersApi = (query: DirectoryMemberQuery) =>
  apiRequest<{ total: number; page: number; pageSize: number; items: DirectoryMember[] }>('/api/directory/members', { query });
export const listDirectoryRolesApi = () => apiRequest<{ id: string; name: string }[]>('/api/directory/roles');

// ---------- 客戶 ----------

export interface ClientRow {
  id: string;
  code: string;
  name: string;
  shortName: string | null;
  taxId: string | null;
  phone: string | null;
  status: 'active' | 'inactive';
  ownerUserId: string | null;
  ownerName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  primaryContact: string | null;
  projectCount: number;
  canEdit: boolean;
}

export interface ClientContact {
  id?: string;
  name: string;
  title?: string | null;
  phone?: string | null;
  mobile?: string | null;
  email?: string | null;
  isPrimary: boolean;
  note?: string | null;
}

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
  /** 只有全公司範圍的管理者可以移動客戶所屬部門。 */
  canChangeDepartment: boolean;
  status: 'active' | 'inactive';
  note: string | null;
  canEdit: boolean;
  canDelete: boolean;
  contacts: ClientContact[];
}

export type ClientInput = Omit<ClientDetail, 'id' | 'parentName' | 'ownerName' | 'departmentName' | 'canChangeDepartment' | 'canEdit' | 'canDelete'>;

export interface Paged<T> {
  total: number;
  page: number;
  pageSize: number;
  items: T[];
}

export const searchClientsApi = (query: { keyword?: string; status?: string; ownerUserId?: string; page?: number; pageSize?: number }) =>
  apiRequest<Paged<ClientRow>>('/api/clients', { query });
export const getClientApi = (id: string) => apiRequest<ClientDetail>(`/api/clients/${id}`);
export const createClientApi = (body: Partial<ClientInput>) => apiRequest<{ id: string; code: string }>('/api/clients', { method: 'POST', body });
export const updateClientApi = (id: string, body: Partial<ClientInput>) => apiRequest(`/api/clients/${id}`, { method: 'PUT', body });
export const deleteClientApi = (id: string) => apiRequest(`/api/clients/${id}`, { method: 'DELETE' });

// ---------- 專案 ----------

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

export interface ProjectSearchResult extends Paged<ProjectRow> {
  statusCounts: Record<string, number>;
  sumExpectedAmount: number;
  sumContractAmount: number;
}

export interface ProjectMember {
  userId: string;
  displayName?: string;
  departmentName?: string | null;
  projectRole: 'manager' | 'member' | 'viewer';
}

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
  /** 沒有讀取權的資源為 null。 */
  summary: { orderTotal: number | null; invoicedTotal: number | null; paidTotal: number | null; activeWarranties: number | null };
}

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

/** status 多選要展開成重複的 query 參數（status=a&status=b）。 */
export function searchProjectsApi(query: ProjectSearchQuery): Promise<ProjectSearchResult> {
  const params = new URLSearchParams();
  Object.entries(query).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return;
    if (Array.isArray(value)) value.forEach((item) => params.append(key, item));
    else params.set(key, String(value));
  });
  return apiRequest(`/api/projects?${params}`);
}

export const getProjectApi = (id: string) => apiRequest<ProjectDetail>(`/api/projects/${id}`);
export const createProjectApi = (body: ProjectInput) => apiRequest<{ id: string; projectNo: string }>('/api/projects', { method: 'POST', body });
export const updateProjectApi = (id: string, body: ProjectInput) => apiRequest(`/api/projects/${id}`, { method: 'PUT', body });
export const changeProjectStatusApi = (id: string, status: string, reason?: string) =>
  apiRequest(`/api/projects/${id}/status`, { method: 'POST', body: { status, reason } });
export const deleteProjectApi = (id: string) => apiRequest(`/api/projects/${id}`, { method: 'DELETE' });
export const replaceProjectMembersApi = (id: string, members: ProjectMember[]) =>
  apiRequest(`/api/projects/${id}/members`, { method: 'PUT', body: members.map(({ userId, projectRole }) => ({ userId, projectRole })) });
