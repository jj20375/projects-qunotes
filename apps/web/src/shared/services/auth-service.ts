/**
 * @file auth-service.ts
 * 身分驗證、圖形驗證碼、工作階段狀態與租戶工作範圍切換服務。
 */
import { apiRequest, storeAccessToken } from '@/shared/api/http-client';
import type { AuthResponse, Me, MenuNode, ScopeOption } from '@/shared/types/auth';

/**
 * 登入圖形驗證碼回應結構
 */
export interface CaptchaResponse {
  /** 系統是否已啟用驗證碼檢查（連續登入失敗達門檻時啟動） */
  enabled: boolean;
  /** 驗證碼識別 ID */
  captchaId?: string;
  /** 驗證碼圖片 Base64 Data URL */
  image?: string;
}

/**
 * 取得登入驗證碼狀態與圖片
 *
 * @returns 傳回驗證碼狀態及圖片（若啟用）
 */
export function getCaptchaApi(): Promise<CaptchaResponse> {
  return apiRequest('/api/auth/captcha', { handleUnauthorized: false });
}

/**
 * 使用者帳號密碼登入
 *
 * 驗證成功後自動將簽發之 JWT 存入記憶體與工作階段儲存區。
 *
 * @param body 登入資訊（包含帳號、密碼與選填之驗證碼參數）
 * @returns Promise<void>
 */
export async function loginApi(body: { account: string; password: string; captchaId?: string; captchaCode?: string }): Promise<void> {
  const response = await apiRequest<AuthResponse>('/api/auth/login', { method: 'POST', body, handleUnauthorized: false });
  storeAccessToken(response.accessToken);
}

/**
 * 登出當前使用者工作階段
 *
 * 向後端註銷 refresh token，並清空客戶端保存之 access token。
 *
 * @returns Promise<void>
 */
export async function logoutApi(): Promise<void> {
  try {
    await apiRequest('/api/auth/logout', { method: 'POST', handleUnauthorized: false });
  } finally {
    storeAccessToken(null);
  }
}

/**
 * 取得當前已驗證使用者之個人資料、當前工作範圍與有效權限
 *
 * @returns 傳回當前登入者之完整資訊物件
 */
export function getMeApi(): Promise<Me> {
  return apiRequest('/api/auth/me');
}

/**
 * 取得依當前身分與權限篩選後之後台可見動態功能選單樹
 *
 * @returns 傳回選單節點樹陣列
 */
export function getMenuApi(): Promise<MenuNode[]> {
  return apiRequest('/api/auth/menu');
}

/** 公司選單每次向後端要幾筆。 */
export const SCOPE_PAGE_SIZE = 20;

/**
 * 取得當前登入者可切換之公司或平台租戶範圍清單
 *
 * 關鍵字搜尋與分頁（offset／limit）一律由後端完成。
 *
 * @param keyword 搜尋關鍵字（篩選公司名稱）
 * @param offset 偏移筆數，預設為 0
 * @param limit 每頁筆數，預設為 SCOPE_PAGE_SIZE (20)
 * @returns 傳回可切換之工作範圍選項陣列
 */
export function listScopesApi(keyword?: string, offset = 0, limit = SCOPE_PAGE_SIZE): Promise<ScopeOption[]> {
  return apiRequest('/api/auth/scopes', { query: { keyword, offset, limit } });
}

/**
 * 切換至指定之公司或平台工作範圍
 *
 * 後端會依新範圍重新計算權限並簽發新的 JWT access token 與 refresh token。
 *
 * @param scopeKind 目標範圍類型（'platform' 或 'company'）
 * @param companyId 目標公司識別碼（切換至平台時為 null）
 * @returns Promise<void>
 */
export async function selectScopeApi(scopeKind: 'platform' | 'company', companyId: string | null): Promise<void> {
  const response = await apiRequest<AuthResponse>('/api/auth/scopes/select', { method: 'POST', body: { scopeKind, companyId } });
  storeAccessToken(response.accessToken);
}
