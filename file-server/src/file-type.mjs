/**
 * @file file-type.mjs
 * 允許上傳的檔案類型與檔頭（magic bytes）檢查。
 *
 * 副檔名與 Content-Type 都可以被竄改，所以合併完檔案後再以實際檔頭確認；
 * 清單必須與 API 的 FileRules.AllowedExtensions 一致。
 */

/** 副檔名 → 允許的檔頭判斷。Office Open XML（xlsx／docx）是 zip。 */
const signatures = {
  pdf: (head) => head.subarray(0, 4).toString('latin1') === '%PDF',
  png: (head) => head.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])),
  jpg: (head) => head[0] === 0xff && head[1] === 0xd8 && head[2] === 0xff,
  jpeg: (head) => head[0] === 0xff && head[1] === 0xd8 && head[2] === 0xff,
  webp: (head) => head.subarray(0, 4).toString('latin1') === 'RIFF' && head.subarray(8, 12).toString('latin1') === 'WEBP',
  heic: (head) => head.subarray(4, 8).toString('latin1') === 'ftyp',
  xlsx: (head) => head[0] === 0x50 && head[1] === 0x4b && head[2] === 0x03 && head[3] === 0x04,
  docx: (head) => head[0] === 0x50 && head[1] === 0x4b && head[2] === 0x03 && head[3] === 0x04,
};

/** 取小寫副檔名（不含點）。 */
export function extensionOf(fileName) {
  const match = /\.([a-z0-9]+)$/i.exec(String(fileName || ''));
  return match ? match[1].toLowerCase() : '';
}

/**
 * 檢查檔頭是否符合副檔名。
 *
 * @param {string} fileName - API 簽入 token 的原始檔名
 * @param {Buffer} head - 檔案開頭至少 16 bytes
 * @returns {boolean}
 */
export function matchesSignature(fileName, head) {
  const check = signatures[extensionOf(fileName)];
  return Boolean(check && head.length >= 12 && check(head));
}

/** 瀏覽器可直接預覽的類型以 inline 回應，其他一律下載。 */
export function isInlineType(contentType) {
  return contentType === 'application/pdf' || /^image\/(png|jpeg|webp)$/.test(contentType || '');
}
