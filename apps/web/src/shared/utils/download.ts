/**
 * @file download.ts
 * 處理需要受權限保護之檔案下載（PDF、Excel）與檔案上傳。
 *
 * 瀏覽器直接以 <a href> 開啟連結不會夾帶 JWT Authorization 標頭，
 * 因此此模組以 fetch 取回 Blob 二進位物件後建立暫時性 Object URL 觸發瀏覽器下載。
 * 支援 RFC 5987 之 Content-Disposition 中文檔名解析。
 */
import { ApiError, readAccessToken, refreshAccessToken } from '@/shared/api/http-client';
import { withBasePath } from '@/shared/utils/base-path';

/**
 * 從 HTTP Content-Disposition 回應標頭解析檔案名稱
 *
 * @param disposition Content-Disposition 標頭字串
 * @param fallback 解析失敗時使用之預設檔名
 * @returns 檔名字串
 */
function fileNameFrom(disposition: string | null, fallback: string): string {
  const encoded = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  if (encoded) return decodeURIComponent(encoded);
  return disposition?.match(/filename="?([^";]+)"?/i)?.[1] ?? fallback;
}

/**
 * 以帶有 JWT 驗證之 fetch 方式下載檔案並在瀏覽器觸發存檔對話框
 *
 * @param path 後端 API 下載端點路徑（例如 /api/quotes/{id}/pdf）
 * @param fallbackName 若伺服器未提供檔名時之預設下載檔名
 * @returns Promise<void>
 */
export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const send = () => fetch(withBasePath(path), { headers: { authorization: `Bearer ${readAccessToken() ?? ''}` }, credentials: 'same-origin' });
  let response = await send();
  if (response.status === 401 && (await refreshAccessToken())) response = await send();
  if (!response.ok) {
    const data = response.headers.get('content-type')?.includes('json') ? await response.json() : undefined;
    throw new ApiError(response.status, (data as { message?: string } | undefined)?.message ?? `下載失敗（${response.status}）`, data);
  }
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = fileNameFrom(response.headers.get('content-disposition'), fallbackName);
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/**
 * 以 multipart/form-data 格式上傳檔案至指定端點
 *
 * @param path 後端 API 上傳端點路徑
 * @param form 封裝之 FormData 表單物件
 * @returns 傳回伺服器回傳之 JSON 反序列化物件
 */
export async function uploadFile<T>(path: string, form: FormData): Promise<T> {
  const send = () => fetch(withBasePath(path), { method: 'POST', body: form, headers: { authorization: `Bearer ${readAccessToken() ?? ''}` }, credentials: 'same-origin' });
  let response = await send();
  if (response.status === 401 && (await refreshAccessToken())) response = await send();
  const data = response.headers.get('content-type')?.includes('json') ? await response.json() : undefined;
  if (!response.ok) throw new ApiError(response.status, (data as { message?: string } | undefined)?.message ?? `上傳失敗（${response.status}）`, data);
  return data as T;
}
