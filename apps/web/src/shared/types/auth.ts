/**
 * @file auth.ts
 * 使用者身分認證、切換範圍與工作工作區型別定義。
 */
import type { PermissionGrant } from '@/shared/types/permissions';

/**
 * 登入使用者基本資料
 */
export interface AuthUser {
  /** 使用者唯一識別碼（UUID） */
  id: string;
  /** 登入帳號 */
  account: string;
  /** 顯示名稱 / 本名 */
  displayName: string;
  /** 電子郵件信箱 */
  email: string | null;
  /** 是否具備平台系統管理員身分 */
  isSystemUser: boolean;
}

/**
 * 目前已選定之工作範圍（租戶身分）
 */
export interface AuthScope {
  /** 範圍類型：platform（平台總管）或 company（公司租戶） */
  kind: 'platform' | 'company';
  /** 公司 ID（若是 platform scope 則為 null） */
  companyId: string | null;
  /** 公司名稱 */
  companyName: string | null;
  /** 在該公司的成員身分 ID */
  membershipId: string | null;
  /** 所屬部門 ID */
  departmentId: string | null;
  /** 所屬部門名稱 */
  departmentName: string | null;
  /** 有效資料可見範圍（own / department / department_tree / company） */
  dataScope: string;
  /** 是否正在模擬/代登入其他公司 */
  isImpersonating: boolean;
  /** 是否為該公司的初代最高管理員（不可被停用或剝奪角色） */
  isRootAdmin: boolean;
}

/**
 * 當前使用者完整登入工作階段資訊（GET /api/auth/me 回傳結構）
 */
export interface Me {
  /** 使用者基本資訊 */
  user: AuthUser;
  /** 當前生效的工作範圍與部門層級 */
  scope: AuthScope;
  /** 經計算後當前生效之功能權限授權清單 */
  grants: PermissionGrant[];
}

/**
 * 導覽選單節點樹結構
 */
export interface MenuNode {
  /** 模組或選單代碼 */
  code: string;
  /** 顯示名稱 */
  name: string;
  /** 路由路徑（若是群組節點則為 null） */
  routePath: string | null;
  /** 子選單節點清單 */
  children: MenuNode[];
}

/**
 * 可切換之公司或平台範圍選項
 */
export interface ScopeOption {
  /** 範圍類型：platform 或 company */
  scopeKind: 'platform' | 'company';
  /** 公司識別碼（platform 時為 null） */
  companyId: string | null;
  /** 公司或工作區顯示名稱 */
  name: string;
  /** 是否為平台管理員透過模擬進入 */
  isImpersonation: boolean;
}

/**
 * 登入或權杖交換成功後回傳之權杖封包
 */
export interface AuthResponse {
  /** JWT Access Token 緊湊字串 */
  accessToken: string;
  /** 權杖過期時間（ISO 8601 UTC 字串） */
  expiresAt: string;
}
