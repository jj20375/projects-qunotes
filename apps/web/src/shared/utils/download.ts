/**
 * @file download.ts
 * 下載需要授權的檔案（PDF、Excel）。瀏覽器直接開連結不會帶 Authorization 標頭，
 * 所以以 fetch 取回 blob 再觸發下載；檔名取自 Content-Disposition（支援 RFC 5987 的中文檔名）。
 */
import { ApiError, readAccessToken, refreshAccessToken } from '@/shared/api/http-client';

function fileNameFrom(disposition: string | null, fallback: string): string {
  const encoded = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  if (encoded) return decodeURIComponent(encoded);
  return disposition?.match(/filename="?([^";]+)"?/i)?.[1] ?? fallback;
}

export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const send = () => fetch(path, { headers: { authorization: `Bearer ${readAccessToken() ?? ''}` }, credentials: 'same-origin' });
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

/** 上傳 multipart；回傳 JSON。 */
export async function uploadFile<T>(path: string, form: FormData): Promise<T> {
  const send = () => fetch(path, { method: 'POST', body: form, headers: { authorization: `Bearer ${readAccessToken() ?? ''}` }, credentials: 'same-origin' });
  let response = await send();
  if (response.status === 401 && (await refreshAccessToken())) response = await send();
  const data = response.headers.get('content-type')?.includes('json') ? await response.json() : undefined;
  if (!response.ok) throw new ApiError(response.status, (data as { message?: string } | undefined)?.message ?? `上傳失敗（${response.status}）`, data);
  return data as T;
}
