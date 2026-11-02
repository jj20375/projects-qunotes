/**
 * @file token.mjs
 * 檔案服務的存取 Token 驗證（沿用美達特 media-server）
 *
 * 職責說明：
 * 提供 verifyToken 函式用於檢驗與解密由 API 核發的 HMAC 簽章 Token（Infrastructure/FileTokenService.cs）。
 * 提供 parseContentRange 函式用於解析 HTTP 上傳請求中的 Content-Range 標頭。
 * 
 * 注意事項：
 * - verifyToken 採用 Node.js 的 timingSafeEqual 防止時序攻擊 (Timing Attack)。
 * - Token 簽章以 Base64 payload + '.' + Hex HMAC 構成，有效期限驗證使用 Unix 時間戳。
 */
import { createHmac, timingSafeEqual } from 'node:crypto';

/**
 * 驗證並解析 JWT-like 媒體存取 Token
 *
 * 使用 HMAC-SHA256 驗證 Token 簽章完整性，防止客戶端竄改。
 * 同時驗證 Token 是否已過期。
 *
 * @param {string} token - 待驗證的 Token 字串（格式為 payloadBase64.signatureHex）
 * @param {string} secret - 用於簽章校驗的 HMAC 金鑰
 * @returns {Object|null} 解析後的 Payload 物件，若驗證失敗或已過期則回傳 null
 */
export function verifyToken(token, secret) {
  const [body, signature] = String(token || '').split('.');
  if (!body || !signature) return null;
  
  /** 使用指定的金鑰重新計算 HMAC 簽章 */
  const expected = createHmac('sha256', secret).update(body).digest('hex');
  
  /** 採用防時序攻擊的安全比對 */
  if (expected.length !== signature.length || !timingSafeEqual(Buffer.from(expected), Buffer.from(signature))) return null;
  
  try {
    const payload = JSON.parse(Buffer.from(body, 'base64').toString('utf8'));
    /** 檢查 token 的 Unix 過期時間 */
    return Number(payload.expiresAtUnix) > Math.floor(Date.now() / 1000) ? payload : null;
  } catch {
    return null;
  }
}

/**
 * 解析 HTTP Content-Range 標頭
 *
 * 適用於分片上傳 (Chunked Upload)，提取該分片的 start、end 位元組索引與總檔案大小。
 * 範例格式: "bytes 0-1048575/5242880"
 *
 * @param {string} value - Content-Range 標頭值
 * @returns {Object|null} 解析後的結果 { start, end, total }，若格式無效則回傳 null
 */
export function parseContentRange(value) {
  const match = /^bytes (\d+)-(\d+)\/(\d+)$/.exec(String(value || ''));
  if (!match) return null;
  const [, start, end, total] = match.map(Number);
  /** 確保區間邏輯正確：起始小於等於結束，且結束索引小於總長度 */
  return start <= end && end < total ? { start, end, total } : null;
}
