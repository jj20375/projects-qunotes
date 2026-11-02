/**
 * @file admin-service.ts
 * 公司管理、部門、帳號、角色、系統帳號與權限目錄 API。
 * 所有公司範圍 API 都由後端依 JWT 決定公司，前端不傳 companyId。
 */
import { apiRequest } from '@/shared/api/http-client';
import type { Paged } from '@/shared/services/business-service';
import type { CatalogNode, PermissionAllowance, PermissionGrant } from '@/shared/types/permissions';

// ---------- 公司（platform） ----------

export interface CompanyRow {
  id: string;
  name: string;
  slug: string;
  taxId: string | null;
  phone: string | null;
  address: string | null;
  status: 'active' | 'suspended';
  isRoleCrudConfigurationEnabled: boolean;
  isRoleOptionConfigurationEnabled: boolean;
  createdAt: string;
  memberCount: number;
  rootAdminAccount: string | null;
  rootAdminName: string | null;
}

/** 列表查詢：關鍵字與分頁一律交給後端。 */
export interface ListQuery {
  keyword?: string;
  page?: number;
  pageSize?: number;
}

export const listCompaniesApi = (query: ListQuery) => apiRequest<Paged<CompanyRow>>('/api/platform/companies', { query: { ...query } });
export const createCompanyApi = (body: Record<string, unknown>) => apiRequest<{ id: string }>('/api/platform/companies', { method: 'POST', body });
export const updateCompanyApi = (id: string, body: Record<string, unknown>) => apiRequest(`/api/platform/companies/${id}`, { method: 'PATCH', body });

// ---------- 系統帳號（platform） ----------

export interface SystemUserRow {
  id: string;
  account: string;
  displayName: string;
  email: string | null;
  status: 'active' | 'disabled';
  systemRoleId: string;
  roleName: string;
  lastLoginAt: string | null;
  lockedUntil: string | null;
}

export const listSystemUsersApi = (query: ListQuery) => apiRequest<Paged<SystemUserRow>>('/api/platform/system-users', { query: { ...query } });
export const createSystemUserApi = (body: Record<string, unknown>) => apiRequest('/api/platform/system-users', { method: 'POST', body });
export const updateSystemUserApi = (id: string, body: Record<string, unknown>) => apiRequest(`/api/platform/system-users/${id}`, { method: 'PATCH', body });
export const resetSystemUserPasswordApi = (id: string, password: string) =>
  apiRequest(`/api/platform/system-users/${id}/reset-password`, { method: 'POST', body: { password } });

// ---------- 部門 ----------

export interface DepartmentRow {
  id: string;
  parentId: string | null;
  code: string | null;
  name: string;
  sortOrder: number;
  isActive: boolean;
  depth: number;
  memberCount: number;
}

export const listDepartmentsApi = () => apiRequest<DepartmentRow[]>('/api/admin/departments');
export const createDepartmentApi = (body: Record<string, unknown>) => apiRequest('/api/admin/departments', { method: 'POST', body });
export const updateDepartmentApi = (id: string, body: Record<string, unknown>) => apiRequest(`/api/admin/departments/${id}`, { method: 'PATCH', body });
export const deleteDepartmentApi = (id: string) => apiRequest(`/api/admin/departments/${id}`, { method: 'DELETE' });

// ---------- 成員 ----------

export interface MemberRow {
  membershipId: string;
  userId: string;
  account: string;
  displayName: string;
  email: string | null;
  userStatus: string;
  lastLoginAt: string | null;
  departmentId: string | null;
  departmentName: string | null;
  roleId: string;
  roleName: string;
  roleDataScope: string;
  dataScopeOverride: string | null;
  effectiveDataScope: string;
  isActive: boolean;
  isRootAdmin: boolean;
  isSelf: boolean;
  personalPermissionCount: number;
}

export interface UserPermissionOverride extends PermissionAllowance {
  effect: 'grant' | 'deny';
  reason: string | null;
  expiresAt: string | null;
  isExpired?: boolean;
}

export interface MemberPermissions {
  roleGrants: PermissionGrant[];
  overrides: UserPermissionOverride[];
  effective: PermissionGrant[];
}

export const listMembersApi = (query: ListQuery) => apiRequest<Paged<MemberRow>>('/api/admin/users', { query: { ...query } });
export const createMemberApi = (body: Record<string, unknown>) => apiRequest('/api/admin/users', { method: 'POST', body });
export const updateMemberApi = (membershipId: string, body: Record<string, unknown>) => apiRequest(`/api/admin/users/${membershipId}`, { method: 'PATCH', body });
export const resetMemberPasswordApi = (membershipId: string, password: string) =>
  apiRequest(`/api/admin/users/${membershipId}/reset-password`, { method: 'POST', body: { password } });
export const getMemberPermissionsApi = (membershipId: string) => apiRequest<MemberPermissions>(`/api/admin/users/${membershipId}/permissions`);
export const replaceMemberPermissionsApi = (membershipId: string, items: UserPermissionOverride[]) =>
  apiRequest(`/api/admin/users/${membershipId}/permissions`, { method: 'PUT', body: { items } });

// ---------- 角色 ----------

export interface RoleRow {
  id: string;
  code: string;
  name: string;
  scope?: 'platform' | 'company';
  dataScope: string;
  isSystem: boolean;
  isFullAccess: boolean;
  isEditable: boolean;
  memberCount: number;
}

/** company：本公司可見角色；platform：系統角色。 */
const roleBase = (mode: 'company' | 'platform') => (mode === 'platform' ? '/api/platform/roles' : '/api/admin/roles');

/** forAssignment：給成員表單選角色用，自訂角色只列出自己建立的；角色管理頁不帶，列出全部。 */
export const listRolesApi = (mode: 'company' | 'platform', options: { forAssignment?: boolean } = {}) =>
  apiRequest<RoleRow[]>(roleBase(mode), { query: options.forAssignment ? { forAssignment: true } : undefined });
export const getRolePermissionsApi = (mode: 'company' | 'platform', roleId: string) => apiRequest<PermissionAllowance[]>(`${roleBase(mode)}/${roleId}/permissions`);
export const createRoleApi = (body: Record<string, unknown>) => apiRequest<{ id: string }>('/api/admin/roles', { method: 'POST', body });
export const updateRoleApi = (mode: 'company' | 'platform', roleId: string, body: Record<string, unknown>) =>
  apiRequest(`${roleBase(mode)}/${roleId}`, { method: 'PATCH', body });
export const replaceRolePermissionsApi = (mode: 'company' | 'platform', roleId: string, permissions: PermissionAllowance[]) =>
  apiRequest(`${roleBase(mode)}/${roleId}/permissions`, { method: 'PUT', body: { permissions } });
export const deleteRoleApi = (roleId: string) => apiRequest(`/api/admin/roles/${roleId}`, { method: 'DELETE' });

// ---------- 權限目錄 ----------

export const getPermissionCatalogApi = (scope: 'company' | 'platform') => apiRequest<CatalogNode[]>('/api/admin/permission-catalog', { query: { scope } });

/** 陣列轉成以代碼為 key 的物件，供 PermissionMatrix 使用。 */
export function toAllowanceMap(items: PermissionAllowance[]): Record<string, PermissionAllowance> {
  return Object.fromEntries(items.map((item) => [item.code, { ...item, options: [...item.options] }]));
}

/** 去掉完全沒勾的資源。 */
export function fromAllowanceMap(map: Record<string, PermissionAllowance>): PermissionAllowance[] {
  return Object.values(map).filter((item) => item.perCreate || item.perRead || item.perUpdate || item.perDelete || item.options.length > 0);
}
