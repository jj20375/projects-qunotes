/**
 * @file range.mjs
 * HTTP 單一位元組範圍解析工具，供影片播放與拖曳讀取使用。
 */

/**
 * 解析單一 `Range: bytes=start-end` 標頭。
 *
 * @param {string|undefined} value - 瀏覽器送出的 Range 標頭
 * @param {number} size - 物件總大小
 * @returns {{ start: number, end: number, length: number, contentRange: string }|null|undefined} 無 Range 時回傳 null；格式或範圍無效時回傳 undefined
 */
export function parseByteRange(value, size) {
  if (!value) return null;
  const match = /^bytes=(\d*)-(\d*)$/.exec(String(value));
  if (!match || (!match[1] && !match[2]) || size <= 0) return undefined;
  const [, rawStart, rawEnd] = match;
  const start = rawStart ? Number(rawStart) : Math.max(0, size - Number(rawEnd));
  const end = rawEnd && rawStart ? Math.min(Number(rawEnd), size - 1) : size - 1;
  if (!Number.isInteger(start) || !Number.isInteger(end) || start < 0 || start >= size || end < start) return undefined;
  return { start, end, length: end - start + 1, contentRange: `bytes ${start}-${end}/${size}` };
}
