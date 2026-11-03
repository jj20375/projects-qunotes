/**
 * @file admin-service.ts
 * 公司管理、組織部門、成員帳號、角色權限、平台系統帳號與權限目錄 API。
 *
 * 安全原則：所有公司範圍 API 都由後端依 JWT 決定公司，前端嚴禁傳入 companyId。
 */
import { apiRequest } from '@/shared/api/http-client';
import type { Paged } from '@/shared/services/business-service';
import type { CatalogNode, PermissionAllowance, PermissionGrant } from '@/shared/types/permissions';

// ---------- 公司（platform） ----------

/**
 * 平台管理 - 公司列表列項目
 */
export interface CompanyRow {
  /** 公司識別碼 */
  id: string;
  /** 公司名稱 */
  name: string;
  /** 租戶唯一代碼（Slug） */
  slug: string;
  /** 統一編號 */
  taxId: string | null;
  /** 代表號電話 */
  phone: string | null;
  /** 登記地址 */
  address: string | null;
  /** 營運狀態：active（正常啟用）、suspended（已停權） */
  status: 'active' | 'suspended';
  /** 是否允許自訂角色 CRUD 配置 */
  isRoleCrudConfigurationEnabled: boolean;
  /** 是否允許角色細項選項配置 */
  isRoleOptionConfigurationEnabled: boolean;
  /** 建立時間 */
  createdAt: string;
  /** 成員總數 */
  memberCount: number;
  /** 初始最高管理員帳號 */
  rootAdminAccount: string | null;
  /** 初始最高管理員姓名 */
  rootAdminName: string | null;
}

/** 列表查詢通用分頁條件；關鍵字與分頁一律交給後端處理。 */
export interface ListQuery {
  keyword?: string;
  page?: number;
  pageSize?: number;
}

/**
 * 查詢平台註冊之公司清單（平台管理員專用）
 *
 * @param query 列表分頁與關鍵字查詢條件
 * @returns 傳回分頁公司資料
 */
export function listCompaniesApi(query: ListQuery): Promise<Paged<CompanyRow>> {
  return apiRequest<Paged<CompanyRow>>('/api/platform/companies', { query: { ...query } });
}

/**
 * 建立新租戶公司及其初始管理員帳號
 *
 * @param body 包含公司資料與初始管理員帳號之輸入物件
 * @returns 傳回新建立公司之 ID 物件
 */
export function createCompanyApi(body: Record<string, unknown>): Promise<{ id: string }> {
  return apiRequest<{ id: string }>('/api/platform/companies', { method: 'POST', body });
}

/**
 * 更新指定公司基本資料與租戶功能旗標
 *
 * @param id 公司識別碼
 * @param body 欲更新之欄位物件
 * @returns Promise<void>
 */
export function updateCompanyApi(id: string, body: Record<string, unknown>): Promise<void> {
  return apiRequest(`/api/platform/companies/${id}`, { method: 'PATCH', body });
}

// ---------- 系統帳號（platform） ----------

/**
 * 平台系統管理員帳號列資料
 */
export interface SystemUserRow {
  /** 使用者識別碼 */
  id: string;
  /** 登入帳號 */
  account: string;
  /** 顯示姓名 */
  displayName: string;
  /** 電子郵件信箱 */
  email: string | null;
  /** 帳號狀態：active 或 disabled */
  status: 'active' | 'disabled';
  /** 指派之系統角色 ID */
  systemRoleId: string;
  /** 角色名稱 */
  roleName: string;
  /** 最後登入時間 */
  lastLoginAt: string | null;
  /** 鎖定解除時間（密碼猜錯多次鎖定時非 null） */
  lockedUntil: string | null;
}

/**
 * 查詢平台系統帳號清單（平台專用）
 *
 * @param query 查詢分頁條件
 * @returns 傳回分頁系統使用者列表
 */
export function listSystemUsersApi(query: ListQuery): Promise<Paged<SystemUserRow>> {
  return apiRequest<Paged<SystemUserRow>>('/api/platform/system-users', { query: { ...query } });
}

/**
 * 建立平台系統管理員帳號
 *
 * @param body 系統使用者帳號與角色資料
 * @returns Promise<void>
 */
export function createSystemUserApi(body: Record<string, unknown>): Promise<void> {
  return apiRequest('/api/platform/system-users', { method: 'POST', body });
}

/**
 * 更新平台系統管理員帳號
 *
 * @param id 使用者識別碼
 * @param body 欲更新資料
 * @returns Promise<void>
 */
export function updateSystemUserApi(id: string, body: Record<string, unknown>): Promise<void> {
  return apiRequest(`/api/platform/system-users/${id}`, { method: 'PATCH', body });
}

/**
 * 重設平台系統管理員之密碼
 *
 * 重設後將自動遞增 AuthVersion 強制使現存 Session 登出失效。
 *
 * @param id 使用者識別碼
 * @param password 新密碼
 * @returns Promise<void>
 */
export function resetSystemUserPasswordApi(id: string, password: string): Promise<void> {
  return apiRequest(`/api/platform/system-users/${id}/reset-password`, { method: 'POST', body: { password } });
}

// ---------- 部門 ----------

/**
 * 公司部門組織節點項目
 */
export interface DepartmentRow {
  /** 部門識別碼 */
  id: string;
  /** 上層部門 ID（根部門為 null） */
  parentId: string | null;
  /** 部門代碼 */
  code: string | null;
  /** 部門名稱 */
  name: string;
  /** 同層排列順序 */
  sortOrder: number;
  /** 是否啟用 */
  isActive: boolean;
  /** 階層深度（根層為 0） */
  depth: number;
  /** 隸屬成員數量 */
  memberCount: number;
}

/**
 * 取得當前公司完整組織部門清單
 *
 * @returns 傳回部門節點陣列
 */
export function listDepartmentsApi(): Promise<DepartmentRow[]> {
  return apiRequest<DepartmentRow[]>('/api/admin/departments');
}

/**
 * 建立組織部門
 *
 * @param body 部門名稱、代碼與上層部門設定
 * @returns Promise<void>
 */
export function createDepartmentApi(body: Record<string, unknown>): Promise<void> {
  return apiRequest('/api/admin/departments', { method: 'POST', body });
}

/**
 * 更新組織部門名稱與上層從屬關係
 *
 * @param id 部門識別碼
 * @param body 欲更新之部門欄位
 * @returns Promise<void>
 */
export function updateDepartmentApi(id: string, body: Record<string, unknown>): Promise<void> {
  return apiRequest(`/api/admin/departments/${id}`, { method: 'PATCH', body });
}

/**
 * 刪除組織部門
 *
 * 若部門下仍有成員或子部門，後端將阻擋刪除。
 *
 * @param id 部門識別碼
 * @returns Promise<void>
 */
export function deleteDepartmentApi(id: string): Promise<void> {
  return apiRequest(`/api/admin/departments/${id}`, { method: 'DELETE' });
}

// ---------- 成員 ----------

/**
 * 公司內部成員資料列項目
 */
export interface MemberRow {
  /** 該成員在當前公司的 Membership ID */
  membershipId: string;
  /** 使用者帳號 ID */
  userId: string;
  /** 登入帳號 */
  account: string;
  /** 成員姓名 */
  displayName: string;
  /** 電子郵件信箱 */
  email: string | null;
  /** 帳號總體狀態 */
  userStatus: string;
  /** 最後登入時間 */
  lastLoginAt: string | null;
  /** 所屬部門 ID */
  departmentId: string | null;
  /** 所屬部門名稱 */
  departmentName: string | null;
  /** 指派之角色 ID */
  roleId: string;
  /** 角色名稱 */
  roleName: string;
  /** 角色定義之基礎資料範圍 */
  roleDataScope: string;
  /** 個人覆寫之資料範圍（若無覆寫則為 null） */
  dataScopeOverride: string | null;
  /** 經計算後最終生效之資料範圍 */
  effectiveDataScope: string;
  /** 在此公司的成員資格是否生效中 */
  isActive: boolean;
  /** 是否為本公司創始最高管理員 */
  isRootAdmin: boolean;
  /** 是否為操作者自己的成員帳號 */
  isSelf: boolean;
  /** 個人自訂特殊權限覆寫條目數 */
  personalPermissionCount: number;
}

/**
 * 成員個人特殊權限覆寫設定項目
 */
export interface UserPermissionOverride extends PermissionAllowance {
  /** 覆寫效果：grant（加權允許）或 deny（個別禁止） */
  effect: 'grant' | 'deny';
  /** 特殊授權原因說明 */
  reason: string | null;
  /** 授權過期時間（可指定臨時權限） */
  expiresAt: string | null;
  /** 是否已逾期失效 */
  isExpired?: boolean;
}

/**
 * 成員完整權限配置分析結果
 */
export interface MemberPermissions {
  /** 角色所賦予之權限清單 */
  roleGrants: PermissionGrant[];
  /** 個人特殊覆寫清單 */
  overrides: UserPermissionOverride[];
  /** 經合併運算後最終生效之權限清單 */
  effective: PermissionGrant[];
}

/**
 * 查詢公司成員列表
 *
 * @param query 關鍵字與分頁查詢參數
 * @returns 傳回分頁成員清單
 */
export function listMembersApi(query: ListQuery): Promise<Paged<MemberRow>> {
  return apiRequest<Paged<MemberRow>>('/api/admin/users', { query: { ...query } });
}

/**
 * 建立新公司成員帳號並指派角色與部門
 *
 * @param body 成員帳號、姓名、密碼與部門設定
 * @returns Promise<void>
 */
export function createMemberApi(body: Record<string, unknown>): Promise<void> {
  return apiRequest('/api/admin/users', { method: 'POST', body });
}

/**
 * 更新成員職務資訊（部門、角色、啟用狀態與資料範圍）
 *
 * @param membershipId 成員資格識別碼
 * @param body 欲更新之欄位資料
 * @returns Promise<void>
 */
export function updateMemberApi(membershipId: string, body: Record<string, unknown>): Promise<void> {
  return apiRequest(`/api/admin/users/${membershipId}`, { method: 'PATCH', body });
}

/**
 * 重設成員登入密碼
 *
 * @param membershipId 成員資格識別碼
 * @param password 新密碼字串
 * @returns Promise<void>
 */
export function resetMemberPasswordApi(membershipId: string, password: string): Promise<void> {
  return apiRequest(`/api/admin/users/${membershipId}/reset-password`, { method: 'POST', body: { password } });
}

/**
 * 取得指定成員之權限配置分析（含角色繼承、個人覆寫與最終生效結果）
 *
 * @param membershipId 成員資格識別碼
 * @returns 傳回成員完整權限詳細物件
 */
export function getMemberPermissionsApi(membershipId: string): Promise<MemberPermissions> {
  return apiRequest<MemberPermissions>(`/api/admin/users/${membershipId}/permissions`);
}

/**
 * 整批覆寫指定成員之個人特殊授權清單
 *
 * @param membershipId 成員資格識別碼
 * @param items 個人權限覆寫項目清單
 * @returns Promise<void>
 */
export function replaceMemberPermissionsApi(membershipId: string, items: UserPermissionOverride[]): Promise<void> {
  return apiRequest(`/api/admin/users/${membershipId}/permissions`, { method: 'PUT', body: { items } });
}

// ---------- 角色 ----------

/**
 * 角色資料列項目
 */
export interface RoleRow {
  /** 角色識別碼 */
  id: string;
  /** 角色唯一代碼 */
  code: string;
  /** 角色名稱 */
  name: string;
  /** 適用範圍：company 或 platform */
  scope?: 'platform' | 'company';
  /** 角色賦予之資料可見範圍（own / department / department_tree / company） */
  dataScope: string;
  /** 是否為系統內建預設角色（不可更名與刪除） */
  isSystem: boolean;
  /** 是否具備全功能無限存取權（系統管理員） */
  isFullAccess: boolean;
  /** 當前操作者是否具備編輯此角色的權限 */
  isEditable: boolean;
  /** 指派此角色的成員數量 */
  memberCount: number;
}

/**
 * 取得角色基礎路徑
 * @param mode 'company' 或 'platform'
 */
const roleBase = (mode: 'company' | 'platform') => (mode === 'platform' ? '/api/platform/roles' : '/api/admin/roles');

/**
 * 查詢角色清單
 *
 * @param mode 範圍模式：'company'（公司角色）或 'platform'（系統角色）
 * @param options 選項（forAssignment 為 true 時僅列出可用於表單指派的角色）
 * @returns 傳回角色列表
 */
export function listRolesApi(mode: 'company' | 'platform', options: { forAssignment?: boolean } = {}): Promise<RoleRow[]> {
  return apiRequest<RoleRow[]>(roleBase(mode), { query: options.forAssignment ? { forAssignment: true } : undefined });
}

/**
 * 取得指定角色的功能權限配置清單
 *
 * @param mode 範圍模式
 * @param roleId 角色識別碼
 * @returns 傳回權限授權明細陣列
 */
export function getRolePermissionsApi(mode: 'company' | 'platform', roleId: string): Promise<PermissionAllowance[]> {
  return apiRequest<PermissionAllowance[]>(`${roleBase(mode)}/${roleId}/permissions`);
}

/**
 * 建立自訂公司角色
 *
 * @param body 角色名稱、代碼與資料範圍
 * @returns 傳回新建立角色之 ID 物件
 */
export function createRoleApi(body: Record<string, unknown>): Promise<{ id: string }> {
  return apiRequest<{ id: string }>('/api/admin/roles', { method: 'POST', body });
}

/**
 * 更新角色基本資料與資料範圍
 *
 * @param mode 範圍模式
 * @param roleId 角色識別碼
 * @param body 欲更新之資料
 * @returns Promise<void>
 */
export function updateRoleApi(mode: 'company' | 'platform', roleId: string, body: Record<string, unknown>): Promise<void> {
  return apiRequest(`${roleBase(mode)}/${roleId}`, { method: 'PATCH', body });
}

/**
 * 整批更新指定角色的功能權限配置項目
 *
 * @param mode 範圍模式
 * @param roleId 角色識別碼
 * @param permissions 權限授權配置項目清單
 * @returns Promise<void>
 */
export function replaceRolePermissionsApi(mode: 'company' | 'platform', roleId: string, permissions: PermissionAllowance[]): Promise<void> {
  return apiRequest(`${roleBase(mode)}/${roleId}/permissions`, { method: 'PUT', body: { permissions } });
}

/**
 * 刪除自訂角色
 *
 * 若仍有成員被指派為該角色，後端將拒絕刪除。
 *
 * @param roleId 角色識別碼
 * @returns Promise<void>
 */
export function deleteRoleApi(roleId: string): Promise<void> {
  return apiRequest(`/api/admin/roles/${roleId}`, { method: 'DELETE' });
}

// ---------- 權限目錄 ----------

/**
 * 取得系統可用權限樹狀目錄結構（畫勾選矩陣用）
 *
 * @param scope 適用範圍：'company' 或 'platform'
 * @returns 傳回權限目錄樹節點陣列
 */
export function getPermissionCatalogApi(scope: 'company' | 'platform'): Promise<CatalogNode[]> {
  return apiRequest<CatalogNode[]>('/api/admin/permission-catalog', { query: { scope } });
}

/**
 * 將權限授權陣列轉成以 code 為鍵值的 Map 物件，供 PermissionMatrix 編輯使用
 *
 * @param items 權限項目陣列
 * @returns 字典物件
 */
export function toAllowanceMap(items: PermissionAllowance[]): Record<string, PermissionAllowance> {
  return Object.fromEntries(items.map((item) => [item.code, { ...item, options: [...item.options] }]));
}

/**
 * 將 PermissionMatrix 的 Map 物件還原為陣列，並過濾掉未勾選任何動作與子功能的空白項目
 *
 * @param map 權限字典物件
 * @returns 有效權限項目陣列
 */
export function fromAllowanceMap(map: Record<string, PermissionAllowance>): PermissionAllowance[] {
  return Object.values(map).filter((item) => item.perCreate || item.perRead || item.perUpdate || item.perDelete || item.options.length > 0);
}
