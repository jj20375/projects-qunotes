/**
 * @file permissions.ts
 * 權限型別與判斷工具。
 *
 * 前端判斷只用於隱藏選單與按鈕；API 的權限檢查才是安全邊界。
 * PermissionCode 必須與後端 PermissionCatalog.Definitions 同步。
 */

export type PermissionAction = 'create' | 'read' | 'update' | 'delete';

export type PermissionCode =
  | 'platform.companies'
  | 'platform.system_users'
  | 'platform.roles'
  | 'platform.permissions'
  | 'dashboard'
  | 'clients'
  | 'products'
  | 'projects'
  | 'quotes'
  | 'orders'
  | 'warranties'
  | 'invoices'
  | 'company.departments'
  | 'company.users'
  | 'company.roles'
  | 'company.settings';

/** 後端簽發的有效權限（角色 ∪ 個人加權 − 個人禁止 ∩ 上限）。 */
export interface PermissionGrant {
  code: string;
  actions: PermissionAction[];
  options: string[];
}

export interface PermissionRequirement {
  code: PermissionCode;
  action?: PermissionAction;
  option?: string;
}

export function hasPermission(grants: PermissionGrant[] | undefined, code: PermissionCode, action: PermissionAction = 'read'): boolean {
  return !!grants?.some((grant) => grant.code === code && grant.actions.includes(action));
}

export function hasOption(grants: PermissionGrant[] | undefined, code: PermissionCode, option: string): boolean {
  return !!grants?.some((grant) => grant.code === code && grant.options.includes(option));
}

export function meetsRequirement(grants: PermissionGrant[] | undefined, requirement: PermissionRequirement): boolean {
  return hasPermission(grants, requirement.code, requirement.action ?? 'read') && (!requirement.option || hasOption(grants, requirement.code, requirement.option));
}

/** 權限目錄節點（GET /api/admin/permission-catalog）。 */
export interface CatalogNode {
  code: string;
  name: string;
  isGroup: boolean;
  subFeatures: { key: string; name: string }[];
  children: CatalogNode[];
}

/** 單一資源的 CRUD 與子功能設定；角色權限與個人權限共用。 */
export interface PermissionAllowance {
  code: string;
  perCreate: boolean;
  perRead: boolean;
  perUpdate: boolean;
  perDelete: boolean;
  options: string[];
}

export const DATA_SCOPE_LABELS: Record<string, string> = {
  own: '自己負責／參與的專案',
  department: '本部門',
  department_tree: '本部門與下層部門',
  company: '全公司',
};

const DATA_SCOPE_ORDER = ['own', 'department', 'department_tree', 'company'];

/**
 * 資料範圍下拉選項：比操作者自己的範圍大的選項停用（後端也會拒絕）。
 * 否則有角色管理權限的人可以建立「全公司」角色再指派給自己，繞過資料範圍。
 * actorScope 不給時不限制（平台管理員調整系統角色）。
 */
/** 資料範圍是否超過操作者自己的範圍（不認得的值當成最大）。 */
export function exceedsDataScope(scope: string, actorScope?: string): boolean {
  const rank = (value: string) => (DATA_SCOPE_ORDER.includes(value) ? DATA_SCOPE_ORDER.indexOf(value) : DATA_SCOPE_ORDER.length);
  return actorScope !== undefined && rank(scope) > rank(actorScope);
}

export function dataScopeOptions(actorScope?: string, extra: { value: string; label: string }[] = []) {
  const limit = actorScope === undefined ? DATA_SCOPE_ORDER.length : DATA_SCOPE_ORDER.indexOf(actorScope);
  return [
    ...extra,
    ...DATA_SCOPE_ORDER.map((value, index) => ({ value, label: DATA_SCOPE_LABELS[value], disabled: index > limit })),
  ];
}

export const ACTION_LABELS: Record<PermissionAction, string> = {
  create: '新增',
  read: '讀取',
  update: '修改',
  delete: '刪除',
};
