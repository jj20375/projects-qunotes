/**
 * @file server.mjs
 * 專案報價管理系統的檔案服務（由美達特 media-server 精簡而來）。
 *
 * 職責：
 * - PUT /files/uploads/:fileId：以 API 簽發的上傳 token 接收檔案（單次串流，上限由 token 的 fileSize 決定），
 *   計算 SHA-256、核對大小與檔頭，存入儲存區後回呼 API 標示完成；回呼失敗會刪除已存的檔案。
 * - GET /files/objects/:fileId?token=…：以 API 簽發的短效下載 token 讀取檔案，支援 Range。
 *
 * 安全邊界：
 * - 不連資料庫；可存取哪個檔案完全由 API 簽入 token 的 fileId 與 storageKey 決定，路徑參數只用來比對。
 * - 與美達特不同，不做分段上傳：專案附件多為數 MB 的掃描檔，單次上傳較簡單；需要時可沿用美達特的 upload-manifest。
 */
import { createHash, randomUUID } from 'node:crypto';
import { createWriteStream } from 'node:fs';
import { mkdir, open, rm } from 'node:fs/promises';
import { createServer } from 'node:http';
import { join } from 'node:path';
import { pipeline } from 'node:stream/promises';
import { Transform } from 'node:stream';
import { isInlineType, matchesSignature } from './file-type.mjs';
import { parseByteRange } from './range.mjs';
import { createStorage } from './storage.mjs';
import { verifyToken } from './token.mjs';

const port = Number(process.env.FILE_PORT || 5190);
const host = process.env.FILE_HOST || '127.0.0.1';
const root = process.env.FILE_ROOT || join(process.cwd(), 'data');
const tokenKey = process.env.FILE_TOKEN_KEY;
const callbackSecret = process.env.FILE_CALLBACK_SECRET;
const callbackUrl = process.env.FILE_CALLBACK_URL || 'http://127.0.0.1:5280/api/internal/files';

if (!tokenKey || tokenKey.length < 32) throw new Error('FILE_TOKEN_KEY 未設定或少於 32 字元。');
if (!callbackSecret || callbackSecret.length < 16) throw new Error('FILE_CALLBACK_SECRET 未設定或少於 16 字元。');

const storage = createStorage({
  driver: process.env.STORAGE_DRIVER || 'filesystem',
  root,
  bucket: process.env.S3_BUCKET || 'quote-files',
  s3: {
    endpoint: process.env.S3_ENDPOINT,
    region: process.env.S3_REGION || 'us-east-1',
    accessKey: process.env.S3_ACCESS_KEY || '',
    secretKey: process.env.S3_SECRET_KEY || '',
  },
});

function json(response, status, body) {
  if (response.headersSent || response.writableEnded) return;
  response.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
  response.end(JSON.stringify(body));
}

/** 上傳用 Authorization 標頭；下載用 query string（讓瀏覽器 <a href> 可直接開啟，token 只有幾分鐘有效）。 */
function readToken(request, url, kind, fileId) {
  const raw = kind === 'upload' ? request.headers.authorization?.replace(/^Bearer\s+/i, '') : url.searchParams.get('token');
  const token = verifyToken(raw, tokenKey);
  return token && token.kind === kind && token.fileId === fileId ? token : null;
}

/** 以 Content-Disposition 帶入原始檔名；ASCII fallback 與 RFC 5987 值都避免控制字元注入。 */
function disposition(token) {
  const type = token.inline && isInlineType(token.contentType) ? 'inline' : 'attachment';
  const fileName = String(token.fileName || token.fileId);
  const fallback = fileName.replace(/[^\x20-\x7e]|["\\;]/g, '_');
  const encoded = encodeURIComponent(fileName).replace(/[!'()*]/g, (character) => `%${character.charCodeAt(0).toString(16).toUpperCase()}`);
  return `${type}; filename="${fallback}"; filename*=UTF-8''${encoded}`;
}

async function readHead(path) {
  const handle = await open(path, 'r');
  try {
    const buffer = Buffer.alloc(16);
    const { bytesRead } = await handle.read(buffer, 0, 16, 0);
    return buffer.subarray(0, bytesRead);
  } finally {
    await handle.close();
  }
}

async function notifyApi(token, size, sha256) {
  const response = await fetch(`${callbackUrl}/${token.fileId}/completed`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-file-callback-secret': callbackSecret },
    body: JSON.stringify({ size, sha256 }),
    signal: AbortSignal.timeout(10_000),
  });
  if (!response.ok) throw new Error(`API 回呼失敗（${response.status}）`);
}

async function handleUpload(request, response, token) {
  const tempDirectory = join(root, 'tmp');
  await mkdir(tempDirectory, { recursive: true });
  const tempPath = join(tempDirectory, `${token.fileId}-${randomUUID()}`);
  const hash = createHash('sha256');
  let size = 0;
  // 邊收邊算雜湊並限制大小，超過 token 宣告的大小立即中斷，避免灌爆磁碟。
  const meter = new Transform({
    transform(chunk, _encoding, callback) {
      size += chunk.length;
      if (size > token.fileSize) return callback(new RangeError('檔案大小超過申請時的大小。'));
      hash.update(chunk);
      callback(null, chunk);
    },
  });

  try {
    await pipeline(request, meter, createWriteStream(tempPath));
    if (size !== token.fileSize) return json(response, 400, { message: `檔案大小不符（收到 ${size}，預期 ${token.fileSize} bytes）。` });
    if (!matchesSignature(token.fileName, await readHead(tempPath))) return json(response, 415, { message: '檔案內容與副檔名不符。' });

    const sha256 = hash.digest('hex');
    await storage.put(token.storageKey, tempPath, token.contentType);
    try {
      await notifyApi(token, size, sha256);
    } catch (error) {
      // API 沒有確認就不能留下無 metadata 可追蹤的檔案。
      await storage.remove(token.storageKey).catch(() => {});
      console.error(error);
      return json(response, 502, { message: '檔案已收到，但無法完成登記，請重新上傳。' });
    }
    return json(response, 200, { fileId: token.fileId, size, sha256 });
  } catch (error) {
    if (error instanceof RangeError) return json(response, 413, { message: error.message });
    throw error;
  } finally {
    await rm(tempPath, { force: true });
  }
}

async function handleDownload(request, response, token) {
  const object = await storage.open(token.storageKey);
  if (!object) return json(response, 404, { message: '找不到檔案。' });
  const range = parseByteRange(request.headers.range, object.size);
  if (range === undefined) return json(response, 416, { message: '讀取範圍無效。' });
  response.writeHead(range ? 206 : 200, {
    'content-type': token.contentType || 'application/octet-stream',
    'content-disposition': disposition(token),
    'content-length': String(range?.length ?? object.size),
    'accept-ranges': 'bytes',
    'cache-control': 'private, no-store',
    'x-content-type-options': 'nosniff',
    ...(range ? { 'content-range': range.contentRange } : {}),
  });
  await pipeline(object.stream(range ? { start: range.start, end: range.end } : undefined), response);
}

const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url || '/', 'http://file-server');
    // nginx 與 Vite 以 /files 前綴轉發，兩種路徑都接受。
    const path = url.pathname.replace(/^\/files(?=\/)/, '');
    if (path === '/health') return json(response, 200, { status: 'ok', storage: storage.driver });

    const upload = /^\/uploads\/([0-9a-f-]{36})$/.exec(path);
    if (upload && request.method === 'PUT') {
      const token = readToken(request, url, 'upload', upload[1]);
      return token ? await handleUpload(request, response, token) : json(response, 401, { message: '上傳憑證無效或已過期。' });
    }

    const object = /^\/objects\/([0-9a-f-]{36})$/.exec(path);
    if (object && (request.method === 'GET' || request.method === 'HEAD')) {
      const token = readToken(request, url, 'download', object[1]);
      return token ? await handleDownload(request, response, token) : json(response, 401, { message: '下載連結無效或已過期，請重新開啟。' });
    }

    return json(response, 404, { message: '找不到路徑。' });
  } catch (error) {
    console.error(error);
    if (!response.headersSent) json(response, 500, { message: '檔案服務發生錯誤。' });
    else response.destroy();
  }
});

server.listen(port, host, () => console.log(`file-server 執行於 http://${host}:${port}（儲存：${storage.driver}）`));
