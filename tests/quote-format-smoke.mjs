/**
 * 報價單格式端對端驗證：條款條列化（貼上整段、儲存、Excel 往返）、報價單號規則、報價單抬頭（英文名稱、Logo）與 PDF。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動、已執行 Seeder（SeedDemoData=true），file-server 已啟動。
 * 測試會暫時修改 demo 公司的公司參數，結束時（含失敗）還原。
 * 用法：node tests/quote-format-smoke.mjs [baseUrl] [fileServerUrl]
 */
import { readFile } from 'node:fs/promises';
import { deflateSync } from 'node:zlib';
import assert from 'node:assert/strict';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
const fileUrl = process.argv[3] ?? 'http://127.0.0.1:5190';
const envText = await readFile(new URL('../.env.development-api.local', import.meta.url), 'utf8');
const seedPassword = envText.match(/^Quote__BootstrapAdminPassword=(.+)$/m)?.[1]?.trim();
assert.ok(seedPassword, '找不到 Quote__BootstrapAdminPassword');

const suffix = Date.now().toString(36);
let passed = 0;

async function call(method, path, { token, body, form } = {}) {
  const headers = {};
  if (token) headers.authorization = `Bearer ${token}`;
  if (body !== undefined) headers['content-type'] = 'application/json';
  const response = await fetch(`${baseUrl}${path}`, { method, headers, body: form ?? (body === undefined ? undefined : JSON.stringify(body)) });
  const type = response.headers.get('content-type') ?? '';
  if (type.includes('json')) return { status: response.status, data: await response.json() };
  return { status: response.status, bytes: new Uint8Array(await response.arrayBuffer()), type };
}

async function login(account) {
  const result = await call('POST', '/api/auth/login', { body: { account, password: seedPassword } });
  assert.equal(result.status, 200, `${account} 登入失敗：${JSON.stringify(result.data)}`);
  return result.data.accessToken;
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

/** 產生一張 4×4 的 PNG（不依賴外部套件）。 */
function tinyPng() {
  const crcTable = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    return c >>> 0;
  });
  const crc = (bytes) => {
    let c = 0xffffffff;
    for (const b of bytes) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
  };
  const chunk = (type, data) => {
    const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const out = Buffer.alloc(12 + data.length);
    out.writeUInt32BE(data.length, 0);
    body.copy(out, 4);
    out.writeUInt32BE(crc(body), 8 + data.length);
    return out;
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(4, 0);
  ihdr.writeUInt32BE(4, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 2; // RGB
  const raw = Buffer.concat(Array.from({ length: 4 }, () => Buffer.from([0, ...Array(4).fill([0x1f, 0x6f, 0xeb]).flat()])));
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

const hasImage = (bytes) => /\/Subtype\s*\/Image/.test(Buffer.from(bytes).toString('latin1'));

const pastedTerms = `備註條款
1. 建置官網屬於 RWD 響應式設計
2. 官網架構如附件所示。若製作內容超過規範書
   ，本公司會針對實際狀況另行報價。
3.報價有效期限:60天`;
const expectedTerms = ['建置官網屬於 RWD 響應式設計', '官網架構如附件所示。若製作內容超過規範書，本公司會針對實際狀況另行報價。', '報價有效期限:60天'];

const admin = await login('demo-admin');
const sales = await login('demo-sales');
const original = (await call('GET', '/api/admin/settings', { token: admin })).data;
let projectId;
let quoteId;

try {
  await step('準備：建立客戶與專案', async () => {
    const client = await call('POST', '/api/clients', { token: admin, body: { name: `${suffix} 格式客戶` } });
    assert.equal(client.status, 201, JSON.stringify(client.data));
    const project = await call('POST', '/api/projects', { token: admin, body: { name: `${suffix} 格式專案`, clientId: client.data.id } });
    projectId = project.data.id;
  });

  await step('條款整理 API：移除編號、略過標題、續行併回上一條', async () => {
    const result = await call('POST', '/api/quote-terms/normalize', { token: sales, body: { text: pastedTerms } });
    assert.equal(result.status, 200, JSON.stringify(result.data));
    assert.deepEqual(result.data.items, expectedTerms);
    assert.equal((await call('POST', '/api/quote-terms/normalize', { body: { text: 'a' } })).status, 401);
  });

  await step('直接把整段條列存進報價：儲存後是一條一行、無編號', async () => {
    const created = await call('POST', `/api/projects/${projectId}/quotes`, {
      token: admin,
      body: { title: '條款測試', terms: pastedTerms, items: [{ name: '網站建置', unit: '式', quantity: 1, unitPrice: 100000 }] },
    });
    assert.equal(created.status, 201, JSON.stringify(created.data));
    quoteId = created.data.id;
    const quote = (await call('GET', `/api/quotes/${quoteId}`, { token: admin })).data;
    assert.equal(quote.terms, expectedTerms.join('\n'));
  });

  await step('Excel 往返：匯出時加上編號，再匯入時還原成相同條款', async () => {
    const excel = await call('GET', `/api/quotes/${quoteId}/excel`, { token: admin });
    assert.equal(excel.status, 200);
    const form = new FormData();
    form.append('file', new Blob([excel.bytes]), 'quote.xlsx');
    form.append('kind', 'excel_template');
    const uploaded = await call('POST', '/api/quote-imports', { token: admin, form });
    assert.equal(uploaded.status, 201, JSON.stringify(uploaded.data));
    assert.equal(uploaded.data.preview.header.Terms, expectedTerms.join('\n'));
  });

  await step('公司預設條款：貼上的條列存成一條一行，新報價自動帶入', async () => {
    const saved = await call('PUT', '/api/admin/settings', { token: admin, body: { defaultTerms: '1. 甲條款\n2. 乙條款' } });
    assert.equal(saved.status, 204, JSON.stringify(saved.data));
    assert.equal((await call('GET', '/api/admin/settings', { token: admin })).data.defaultTerms, '甲條款\n乙條款');
    const created = await call('POST', `/api/projects/${projectId}/quotes`, { token: admin, body: { title: '預設條款', items: [] } });
    assert.equal((await call('GET', `/api/quotes/${created.data.id}`, { token: admin })).data.terms, '甲條款\n乙條款');
  });

  await step('報價單號規則：自訂格式、預覽、依日期重新編號；不合法的規則被拒', async () => {
    for (const bad of ['Q{yyyy}', 'Q{seq}{seq}', 'Q 報價{seq}', `Q{yyyy}${'x'.repeat(40)}{seq}`]) {
      const rejected = await call('PUT', '/api/admin/settings', { token: admin, body: { quoteNumberPattern: bad } });
      assert.equal(rejected.status, 400, `應拒絕 ${bad}`);
    }
    const saved = await call('PUT', '/api/admin/settings', { token: admin, body: { quoteNumberPattern: 'DB-{yyyy}{MM}{dd}-{seq:2}' } });
    assert.equal(saved.status, 204, JSON.stringify(saved.data));
    const settings = (await call('GET', '/api/admin/settings', { token: admin })).data;
    assert.match(settings.quoteNumberPreview, /^DB-\d{8}-01$/);
    const first = await call('POST', `/api/projects/${projectId}/quotes`, { token: admin, body: { title: '單號一', quoteDate: '2026-03-04', items: [] } });
    const second = await call('POST', `/api/projects/${projectId}/quotes`, { token: admin, body: { title: '單號二', quoteDate: '2026-03-04', items: [] } });
    assert.match(first.data.quoteNo, /^DB-20260304-\d{2}$/);
    assert.equal(Number(second.data.quoteNo.slice(-2)), Number(first.data.quoteNo.slice(-2)) + 1);
    assert.equal((await call('PUT', '/api/admin/settings', { token: sales, body: { quoteNumberPattern: 'X{seq}' } })).status, 403, '業務不能改公司參數');
  });

  await step('報價單抬頭：英文名稱、Logo 上傳與預覽，PDF 正常產生', async () => {
    const png = tinyPng();
    assert.equal((await call('POST', '/api/admin/settings/logo-upload', { token: admin, body: { fileName: 'logo.gif', size: 10 } })).status, 400, '只接受 PNG、JPG、WebP');
    assert.equal((await call('POST', '/api/admin/settings/logo-upload', { token: admin, body: { fileName: 'logo.png', size: 3 * 1024 * 1024 } })).status, 400, 'Logo 不可超過 2 MB');
    const session = await call('POST', '/api/admin/settings/logo-upload', { token: admin, body: { fileName: 'logo.png', size: png.length } });
    assert.equal(session.status, 201, JSON.stringify(session.data));
    const put = await fetch(`${fileUrl}${session.data.uploadUrl}`, { method: 'PUT', headers: { authorization: `Bearer ${session.data.token}` }, body: png });
    assert.ok(put.ok, `file-server 上傳失敗：${put.status}`);
    const saved = await call('PUT', '/api/admin/settings', { token: admin, body: { englishName: 'Demo Trading Co., Ltd.', logoFileId: session.data.fileId } });
    assert.equal(saved.status, 204, JSON.stringify(saved.data));
    const settings = (await call('GET', '/api/admin/settings', { token: admin })).data;
    assert.equal(settings.company.englishName, 'Demo Trading Co., Ltd.');
    assert.equal(settings.company.logoFileId, session.data.fileId);
    const preview = await fetch(`${fileUrl}${settings.company.logoUrl}`);
    assert.equal(preview.status, 200, 'Logo 預覽網址可讀取');
    const pdf = await call('GET', `/api/quotes/${quoteId}/pdf`, { token: admin });
    assert.equal(pdf.status, 200);
    assert.equal(String.fromCharCode(...pdf.bytes.slice(0, 4)), '%PDF');
    assert.ok(hasImage(pdf.bytes), 'PDF 內含 Logo 圖片（API 經內網向 file-server 取得 Logo）');
    const unknown = await call('PUT', '/api/admin/settings', { token: admin, body: { logoFileId: '00000000-0000-0000-0000-000000000001' } });
    assert.equal(unknown.status, 400, '不存在的 Logo 檔案被拒');
  });

  await step('移除 Logo 後 PDF 仍正常', async () => {
    assert.equal((await call('PUT', '/api/admin/settings', { token: admin, body: { removeLogo: true } })).status, 204);
    assert.equal((await call('GET', '/api/admin/settings', { token: admin })).data.company.logoUrl, null);
    const pdf = await call('GET', `/api/quotes/${quoteId}/pdf`, { token: admin });
    assert.equal(pdf.status, 200);
    assert.ok(!hasImage(pdf.bytes), '移除後 PDF 不再有圖片');
  });
} finally {
  const restored = await call('PUT', '/api/admin/settings', {
    token: admin,
    body: {
      defaultTerms: original.defaultTerms ?? '',
      quoteNumberPattern: original.quoteNumberPattern,
      englishName: original.company.englishName ?? '',
      phone: original.company.phone ?? '',
      address: original.company.address ?? '',
      ...(original.company.logoFileId ? { logoFileId: original.company.logoFileId } : { removeLogo: true }),
    },
  });
  console.log(restored.status === 204 ? '（已還原公司參數）' : `還原公司參數失敗：${JSON.stringify(restored.data)}`);
}

console.log(`\n全部 ${passed} 項通過`);
