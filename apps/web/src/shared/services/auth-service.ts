import { apiRequest, storeAccessToken } from '@/shared/api/http-client';
import type { AuthResponse, Me, MenuNode, ScopeOption } from '@/shared/types/auth';

export interface CaptchaResponse {
  enabled: boolean;
  captchaId?: string;
  image?: string;
}

export function getCaptchaApi(): Promise<CaptchaResponse> {
  return apiRequest('/api/auth/captcha', { handleUnauthorized: false });
}

export async function loginApi(body: { account: string; password: string; captchaId?: string; captchaCode?: string }): Promise<void> {
  const response = await apiRequest<AuthResponse>('/api/auth/login', { method: 'POST', body, handleUnauthorized: false });
  storeAccessToken(response.accessToken);
}

export async function logoutApi(): Promise<void> {
  try {
    await apiRequest('/api/auth/logout', { method: 'POST', handleUnauthorized: false });
  } finally {
    storeAccessToken(null);
  }
}

export function getMeApi(): Promise<Me> {
  return apiRequest('/api/auth/me');
}

export function getMenuApi(): Promise<MenuNode[]> {
  return apiRequest('/api/auth/menu');
}

/** 公司選單每次向後端要幾筆。 */
export const SCOPE_PAGE_SIZE = 20;

/** 可切換的工作範圍；公司名稱的搜尋與分頁（offset／limit）由後端處理。 */
export function listScopesApi(keyword?: string, offset = 0, limit = SCOPE_PAGE_SIZE): Promise<ScopeOption[]> {
  return apiRequest('/api/auth/scopes', { query: { keyword, offset, limit } });
}

/** 切換工作範圍會重新簽發 access token 與 refresh token。 */
export async function selectScopeApi(scopeKind: 'platform' | 'company', companyId: string | null): Promise<void> {
  const response = await apiRequest<AuthResponse>('/api/auth/scopes/select', { method: 'POST', body: { scopeKind, companyId } });
  storeAccessToken(response.accessToken);
}
