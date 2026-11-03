/**
 * @file permissions.ts
 * 權限型別與判斷工具。
 *
 * 前端判斷只用於隱藏選單與按鈕；API 的權限檢查才是安全邊界。
 * PermissionCode 必須與後端 PermissionCatalog.Definitions 同步。
 */

/** 基礎 CRUD 操作動作類型 */
export type PermissionAction = 'create' | 'read' | 'update' | 'delete';

/** 系統定義之權限資源代碼 */
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

/**
 * 後端簽發的有效權限條目（角色權限 ∪ 個人加權 − 個人禁止 ∩ 角色上限）
 */
export interface PermissionGrant {
  /** 權限資源代碼 */
  code: string;
  /** 授予之 CRUD 動作清單 */
  actions: PermissionAction[];
  /** 授予之子功能或細部權限選項鍵值清單 */
  options: string[];
}

/**
 * 權限檢查條件需求
 */
export interface PermissionRequirement {
  /** 欲檢查的權限資源代碼 */
  code: PermissionCode;
  /** 欲檢查的操作動作，預設為 'read' */
  action?: PermissionAction;
  /** 欲檢查的特定子功能選項代碼 */
  option?: string;
}

/**
 * 檢查授權清單中是否具備指定資源的操作權限
 *
 * @param grants 有效權限授權清單
 * @param code 權限資源代碼
 * @param action 操作動作，預設為 'read'
 * @returns 若具備授權傳回 true，否則傳回 false
 */
export function hasPermission(grants: PermissionGrant[] | undefined, code: PermissionCode, action: PermissionAction = 'read'): boolean {
  return !!grants?.some((grant) => grant.code === code && grant.actions.includes(action));
}

/**
 * 檢查授權清單中是否具備指定資源的特定子功能選項
 *
 * @param grants 有效權限授權清單
 * @param code 權限資源代碼
 * @param option 子功能選項鍵值
 * @returns 若具備該子功能選項傳回 true，否則傳回 false
 */
export function hasOption(grants: PermissionGrant[] | undefined, code: PermissionCode, option: string): boolean {
  return !!grants?.some((grant) => grant.code === code && grant.options.includes(option));
}

/**
 * 檢查使用者是否同時滿足特定權限需求（動作與子功能）
 *
 * @param grants 有效權限授權清單
 * @param requirement 權限需求物件
 * @returns 若滿足需求傳回 true，否則傳回 false
 */
export function meetsRequirement(grants: PermissionGrant[] | undefined, requirement: PermissionRequirement): boolean {
  return hasPermission(grants, requirement.code, requirement.action ?? 'read') && (!requirement.option || hasOption(grants, requirement.code, requirement.option));
}

/**
 * 權限目錄節點結構（GET /api/admin/permission-catalog）
 */
export interface CatalogNode {
  /** 權限項目代碼 */
  code: string;
  /** 顯示名稱 */
  name: string;
  /** 是否為純分組節點（不可直接授予權限） */
  isGroup: boolean;
  /** 該資源下可配置之子功能項目清單 */
  subFeatures: { key: string; name: string }[];
  /** 下層子節點清單 */
  children: CatalogNode[];
}

/**
 * 單一資源的 CRUD 與子功能設定項目；角色權限與個人權限配置共用
 */
export interface PermissionAllowance {
  /** 權限項目代碼 */
  code: string;
  /** 是否具備新增權限 */
  perCreate: boolean;
  /** 是否具備檢視權限 */
  perRead: boolean;
  /** 是否具備修改權限 */
  perUpdate: boolean;
  /** 是否具備刪除權限 */
  perDelete: boolean;
  /** 已勾選之子功能鍵值清單 */
  options: string[];
}

/** 資料範圍中文說明對映表 */
export const DATA_SCOPE_LABELS: Record<string, string> = {
  own: '自己負責／參與的專案',
  department: '本部門',
  department_tree: '本部門與下層部門',
  company: '全公司',
};

const DATA_SCOPE_ORDER = ['own', 'department', 'department_tree', 'company'];

/**
 * 檢查目標資料範圍是否超過操作者自身具備的資料範圍階層
 *
 * 說明：資料範圍階層由小至大為 own < department < department_tree < company。
 *
 * @param scope 欲賦予或選擇的目標資料範圍
 * @param actorScope 操作者自身的資料範圍；若未提供則不限制
 * @returns 若目標範圍超越操作者範圍則傳回 true，否則傳回 false
 */
export function exceedsDataScope(scope: string, actorScope?: string): boolean {
  const rank = (value: string) => (DATA_SCOPE_ORDER.includes(value) ? DATA_SCOPE_ORDER.indexOf(value) : DATA_SCOPE_ORDER.length);
  return actorScope !== undefined && rank(scope) > rank(actorScope);
}

/**
 * 產生資料範圍下拉選單選項，並將超越操作者權限之選項設為停用
 *
 * 防止具角色管理權限但僅有「部門」範圍的使用者建立「全公司」角色並指派給自己，藉此提權。
 *
 * @param actorScope 操作者當前自身的資料範圍
 * @param extra 額外的固定前置選項（例如預設值）
 * @returns 下拉選單物件陣列
 */
export function dataScopeOptions(actorScope?: string, extra: { value: string; label: string }[] = []) {
  const limit = actorScope === undefined ? DATA_SCOPE_ORDER.length : DATA_SCOPE_ORDER.indexOf(actorScope);
  return [
    ...extra,
    ...DATA_SCOPE_ORDER.map((value, index) => ({ value, label: DATA_SCOPE_LABELS[value], disabled: index > limit })),
  ];
}

/** CRUD 動作中文名稱對映表 */
export const ACTION_LABELS: Record<PermissionAction, string> = {
  create: '新增',
  read: '讀取',
  update: '修改',
  delete: '刪除',
};
