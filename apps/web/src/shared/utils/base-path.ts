/**
 * @file base-path.ts
 * 部署路徑前綴：後台掛在其他網站的子路徑下（例如 IIS Application `https://host/quote-platform/app`）時，
 * 瀏覽器端的頁面連結、API、檔案上傳下載與 WebSocket 網址都要帶上這段前綴。
 *
 * - 前綴只有一個來源：建置時的 `QUOTE_BASE_PATH`（astro.config 的 `base`），Astro 會注入成 `import.meta.env.BASE_URL`。
 * - 沒有設定時前綴是空字串，所有函式都原樣回傳，行為與沒有這項功能時完全相同（本機開發、正式機）。
 * - 前置代理（IIS、dev-proxy）會先剝掉前綴再轉給 nginx，所以 nginx、API、file-server 收到的路徑永遠不含前綴；
 *   程式裡的路徑一律寫邏輯路徑（`/api/...`、`/projects/detail`），只在交給瀏覽器的那一刻才加前綴。
 */

/** 正規化後的部署前綴：沒有前綴時是 `''`，否則是 `/quote-platform/app` 這種開頭有斜線、結尾沒有的形狀。 */
export const BASE_PATH = normalizeBasePath(import.meta.env.BASE_URL ?? '/');

/**
 * 把任意寫法的前綴整理成 `''` 或 `/a/b`。
 * @param value 原始前綴（`/`、`/a/b/`、`a/b` 等）
 * @returns 正規化後的前綴
 */
export function normalizeBasePath(value: string): string {
  const trimmed = value.trim().replace(/^\/+|\/+$/g, '');
  return trimmed ? `/${trimmed}` : '';
}

/** 路徑是否已經以部署前綴開頭（前綴後面必須接 `/`、`?`、`#` 或結束，避免 `/quote-platform/application` 被誤判）。 */
function hasBasePath(path: string): boolean {
  if (!BASE_PATH || !path.startsWith(BASE_PATH)) return false;
  const rest = path.slice(BASE_PATH.length);
  return rest === '' || /^[/?#]/.test(rest);
}

/**
 * 把站內根路徑加上部署前綴，供 `href`、`location.assign`、`fetch`、`XMLHttpRequest`、SignalR 使用。
 * 外部網址、`//` 開頭、`blob:`／`data:` 與相對路徑原樣回傳；已經帶前綴的路徑也原樣回傳，所以重複呼叫是安全的。
 * @param path 站內根路徑，例如 `/api/quotes`、`/projects/detail?id=1`，或 API 回傳的 `/files/objects/...`
 * @returns 瀏覽器實際要使用的路徑；沒有前綴時就是原本的 `path`
 */
export function withBasePath(path: string): string {
  if (!BASE_PATH || !path.startsWith('/') || path.startsWith('//') || hasBasePath(path)) return path;
  return `${BASE_PATH}${path}`;
}

/**
 * 從瀏覽器的實際路徑去掉部署前綴，還原成程式使用的邏輯路徑；與 {@link withBasePath} 對稱。
 * 任何拿 `window.location.pathname` 跟邏輯路徑比對的地方都要先經過這裡，否則有前綴時會永遠比對不到。
 * @param pathname 瀏覽器路徑，例如 `window.location.pathname`
 * @returns 邏輯路徑，例如 `/projects`；不含前綴時原樣回傳
 */
export function stripBasePath(pathname: string): string {
  if (!hasBasePath(pathname)) return pathname;
  return pathname.slice(BASE_PATH.length) || '/';
}
