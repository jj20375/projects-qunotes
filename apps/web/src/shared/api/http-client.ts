/**
 * @file http-client.ts
 * 所有 API 呼叫的唯一入口：附上 access token、401 時以 refresh token cookie 換新一次再重送。
 *
 * - access token 存在 localStorage（多分頁共用）；refresh token 是 HttpOnly cookie，前端讀不到。
 * - 同時多個請求遇到 401 只會送出一次 refresh（single-flight），避免 refresh token 輪替後互相作廢。
 * - 送出前先看 access token 是否已過期（或快過期）；是的話先換發再送，避免分頁開了一陣子後
 *   第一批請求全部先收到 401 才重試（功能沒問題，但 Network 與 Console 會出現一串 401）。
 */
import type { AuthResponse } from '@/shared/types/auth';

const ACCESS_TOKEN_KEY = 'qp_access_token';

export class ApiError extends Error {
  constructor(public status: number, message: string, public data?: unknown) {
    super(message);
    this.name = 'ApiError';
  }
}

export interface ApiRequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  /** 陣列值會展開成重複的參數（ids=a&ids=b），對應後端的 List 參數。 */
  query?: Record<string, string | number | boolean | null | undefined | string[]>;
  /** 401 時是否嘗試 refresh 並導向登入頁；登入、refresh 本身要關閉。 */
  handleUnauthorized?: boolean;
}

export function readAccessToken(): string | null {
  try {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  } catch {
    return null;
  }
}

export function storeAccessToken(token: string | null): void {
  try {
    if (token) localStorage.setItem(ACCESS_TOKEN_KEY, token);
    else localStorage.removeItem(ACCESS_TOKEN_KEY);
  } catch {
    // 私密模式或封鎖儲存時，token 只存在本次頁面生命週期內的請求裡。
  }
}

let refreshing: Promise<boolean> | null = null;

/** 以 refresh token cookie 換新 access token；同時間只會有一個 refresh 請求。 */
export function refreshAccessToken(): Promise<boolean> {
  refreshing ??= (async () => {
    try {
      const response = await fetch('/api/auth/refresh', { method: 'POST', credentials: 'same-origin' });
      if (!response.ok) {
        storeAccessToken(null);
        return false;
      }
      storeAccessToken(((await response.json()) as AuthResponse).accessToken);
      return true;
    } catch {
      return false;
    } finally {
      setTimeout(() => (refreshing = null), 0);
    }
  })();
  return refreshing;
}

export function redirectToLogin(): void {
  const here = `${window.location.pathname}${window.location.search}`;
  window.location.assign(here === '/' || here.startsWith('/login') ? '/login' : `/login?redirect=${encodeURIComponent(here)}`);
}

function buildUrl(path: string, query?: ApiRequestOptions['query']): string {
  const params = new URLSearchParams();
  Object.entries(query ?? {}).forEach(([key, value]) => {
    if (Array.isArray(value)) value.forEach((item) => params.append(key, item));
    else if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  });
  const text = params.toString();
  return text ? `${path}?${text}` : path;
}

/** JWT 是否已過期或 15 秒內就會過期；讀不出內容的 token 也視為無效。 */
function isExpiringSoon(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))) as { exp?: number };
    return typeof payload.exp !== 'number' || payload.exp * 1000 - Date.now() < 15_000;
  } catch {
    return true;
  }
}

async function send(path: string, options: ApiRequestOptions): Promise<Response> {
  let token = readAccessToken();
  if (token && (options.handleUnauthorized ?? true) && isExpiringSoon(token) && (await refreshAccessToken())) token = readAccessToken();
  return fetch(buildUrl(path, options.query), {
    method: options.method ?? 'GET',
    credentials: 'same-origin',
    headers: {
      ...(options.body !== undefined ? { 'content-type': 'application/json' } : {}),
      ...(token ? { authorization: `Bearer ${token}` } : {}),
    },
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
  });
}

export async function apiRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const handleUnauthorized = options.handleUnauthorized ?? true;
  let response = await send(path, options);
  if (response.status === 401 && handleUnauthorized) {
    if (await refreshAccessToken()) {
      response = await send(path, options);
    }
    if (response.status === 401) {
      storeAccessToken(null);
      redirectToLogin();
      throw new ApiError(401, '登入已逾期，請重新登入。');
    }
  }

  const text = await response.text();
  const data = text ? (JSON.parse(text) as unknown) : undefined;
  if (!response.ok) {
    const message = (data as { message?: string } | undefined)?.message ?? `請求失敗（${response.status}）`;
    throw new ApiError(response.status, message, data);
  }
  return data as T;
}
