/**
 * 第三階段 API 端對端驗證：報價計算、客戶快照、狀態流程與權限、改版、客戶接受回寫專案、
 * PDF／Excel 匯出、Excel 範本匯入與舊報價對照匯入、品項價目、客戶 Excel 匯出入、資料範圍。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動，且已執行 Seeder（SeedDemoData=true）。
 * 用法：node tests/quotes-smoke.mjs [baseUrl]
 */
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
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
  const bytes = new Uint8Array(await response.arrayBuffer());
  return { status: response.status, bytes, type, disposition: response.headers.get('content-disposition') };
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

const xlsxForm = (bytes, fields = {}) => {
  const form = new FormData();
  form.append('file', new Blob([bytes]), 'quote.xlsx');
  Object.entries(fields).forEach(([key, value]) => value !== undefined && form.append(key, value));
  return form;
};

const items = [
  { lineType: 'section', name: '一、設備' },
  { itemCode: 'A1', name: '伺服器', unit: '台', quantity: 2, unitPrice: 1500, unitCost: 1000 },
  { name: '安裝工資', unit: '式', quantity: 1, unitPrice: 999.5 },
];

const admin = await login('demo-admin');
const manager = await login('demo-manager');
const otherSales = await login('demo-sales');
let sales;
let clientId;
let projectId;
let quoteId;

await step('準備：建立專用業務帳號、客戶與專案', async () => {
  const roles = (await call('GET', '/api/admin/roles', { token: admin })).data;
  const departments = (await call('GET', '/api/admin/departments', { token: admin })).data;
  const created = await call('POST', '/api/admin/users', {
    token: admin,
    body: { account: `qs-${suffix}`, displayName: `報價業務 ${suffix}`, password: seedPassword, departmentId: departments.find((d) => d.code === 'SALES-1').id, roleId: roles.find((r) => r.code === 'sales').id },
  });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  sales = await login(`qs-${suffix}`);
  const client = await call('POST', '/api/clients', {
    token: sales,
    body: { name: `${suffix} 報價客戶`, invoiceTitle: `${suffix} 報價客戶股份有限公司`, taxId: '24536806', paymentTerms: '月結 45 天', contacts: [{ name: '周採購', mobile: '0912345678', email: 'chou@example.com', isPrimary: true }] },
  });
  clientId = client.data.id;
  const project = await call('POST', '/api/projects', { token: sales, body: { name: `${suffix} 報價專案`, clientId } });
  projectId = project.data.id;
});

await step('建立報價：客戶快照、預設條件、金額計算、單號格式、專案轉為報價中', async () => {
  const created = await call('POST', `/api/projects/${projectId}/quotes`, { token: sales, body: { title: '機房設備', discountType: 'percent', discountValue: 10, items } });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  assert.match(created.data.quoteNo, /^Q\d{6}-\d{4}$/);
  quoteId = created.data.id;
  const quote = (await call('GET', `/api/quotes/${quoteId}`, { token: sales })).data;
  assert.equal(quote.clientName, `${suffix} 報價客戶股份有限公司`, '有發票抬頭時以發票抬頭為客戶名稱');
  assert.equal(quote.contactName, '周採購');
  assert.equal(quote.contactPhone, '0912345678');
  assert.equal(quote.paymentTerms, '月結 45 天');
  assert.deepEqual([quote.subtotal, quote.discountAmount, quote.netAmount, quote.taxAmount, quote.total], [4000, 400, 3600, 180, 3780]);
  assert.equal(quote.items[2].amount, 1000);
  assert.equal(quote.canSeeCost, false);
  assert.equal(quote.items[1].unitCost, null, '沒有 view_cost 的人送出的成本不會存，也看不到');
  assert.deepEqual(quote.actions, ['save', 'submit', 'delete']);
  const project = (await call('GET', `/api/projects/${projectId}`, { token: sales })).data;
  assert.equal(project.status, 'quoting');
});

await step('驗證：負數、折扣超過 100%、有效期限早於報價日都會被拒絕', async () => {
  const bad = (patch) => call('PUT', `/api/quotes/${quoteId}`, { token: sales, body: { items, ...patch } });
  assert.equal((await bad({ items: [{ name: 'x', quantity: -1, unitPrice: 1 }] })).status, 400);
  assert.equal((await bad({ discountType: 'percent', discountValue: 120 })).status, 400);
  assert.equal((await bad({ quoteDate: '2026-10-29', validUntil: '2026-10-01' })).status, 400);
});

await step('含稅報價：總額 1,050 → 未稅 1,000、稅 50', async () => {
  const result = await call('PUT', `/api/quotes/${quoteId}`, { token: sales, body: { taxIncluded: true, discountType: 'none', items: [{ name: '維護', quantity: 1, unitPrice: 1050 }] } });
  assert.equal(result.status, 200);
  const quote = (await call('GET', `/api/quotes/${quoteId}`, { token: sales })).data;
  assert.deepEqual([quote.netAmount, quote.taxAmount, quote.total], [1000, 50, 1050]);
  await call('PUT', `/api/quotes/${quoteId}`, { token: sales, body: { taxIncluded: false, discountType: 'percent', discountValue: 10, items } });
});

await step('送審後鎖定；業務不能核准；主管退回需原因', async () => {
  assert.equal((await call('POST', `/api/quotes/${quoteId}/submit`, { token: sales })).status, 200);
  assert.equal((await call('PUT', `/api/quotes/${quoteId}`, { token: sales, body: { items } })).status, 400);
  assert.equal((await call('POST', `/api/quotes/${quoteId}/approve`, { token: sales })).status, 403);
  assert.equal((await call('POST', `/api/quotes/${quoteId}/return`, { token: manager, body: {} })).status, 400);
  assert.equal((await call('POST', `/api/quotes/${quoteId}/return`, { token: manager, body: { reason: '單價請再確認' } })).status, 200);
  const quote = (await call('GET', `/api/quotes/${quoteId}`, { token: sales })).data;
  assert.equal(quote.status, 'draft');
  assert.equal(quote.statusReason, '單價請再確認');
});

await step('核准 → 送出 → 改版：新版為草稿、舊版不再是最新', async () => {
  await call('POST', `/api/quotes/${quoteId}/submit`, { token: sales });
  assert.equal((await call('POST', `/api/quotes/${quoteId}/approve`, { token: manager })).status, 200);
  assert.equal((await call('POST', `/api/quotes/${quoteId}/send`, { token: sales })).status, 200);
  const revised = await call('POST', `/api/quotes/${quoteId}/revise`, { token: sales });
  assert.equal(revised.status, 201);
  assert.equal(revised.data.revisionNo, 1);
  const original = (await call('GET', `/api/quotes/${quoteId}`, { token: sales })).data;
  assert.equal(original.isLatest, false);
  assert.equal(original.status, 'sent', '新版送審前舊版維持原狀態');
  assert.deepEqual(original.actions, []);
  // 刪除尚未送審的新版，舊版恢復為最新。
  assert.equal((await call('DELETE', `/api/quotes/${revised.data.id}`, { token: sales })).status, 403, '業務沒有 quotes:delete');
  assert.equal((await call('DELETE', `/api/quotes/${revised.data.id}`, { token: admin })).status, 204);
  assert.equal((await call('GET', `/api/quotes/${quoteId}`, { token: sales })).data.isLatest, true);
});

let revisionId;
await step('改版送審後舊版作廢；客戶接受回寫專案成交金額並轉為得標', async () => {
  revisionId = (await call('POST', `/api/quotes/${quoteId}/revise`, { token: sales })).data.id;
  await call('PUT', `/api/quotes/${revisionId}`, { token: sales, body: { discountType: 'amount', discountValue: 600, items } });
  await call('POST', `/api/quotes/${revisionId}/submit`, { token: sales });
  assert.equal((await call('GET', `/api/quotes/${quoteId}`, { token: sales })).data.status, 'superseded');
  await call('POST', `/api/quotes/${revisionId}/approve`, { token: manager });
  await call('POST', `/api/quotes/${revisionId}/send`, { token: sales });
  assert.equal((await call('POST', `/api/quotes/${revisionId}/reject`, { token: sales, body: {} })).status, 400, '拒絕需原因');
  assert.equal((await call('POST', `/api/quotes/${revisionId}/accept`, { token: sales })).status, 200);
  const project = (await call('GET', `/api/projects/${projectId}`, { token: sales })).data;
  assert.equal(project.status, 'won');
  assert.equal(project.contractAmount, 3400);
  assert.equal(project.contractTotal, 3570);
  assert.match(project.history[0].reason, /客戶接受報價 Q\d{6}-\d{4}-R2/);
  const revisions = (await call('GET', `/api/quotes/${revisionId}`, { token: sales })).data.revisions;
  assert.deepEqual(revisions.map((r) => r.status), ['superseded', 'accepted']);
  // 搜尋可以直接輸入畫面上含版次的單號（例：Q202611-0012-R2）
  const quoteNo = (await call('GET', `/api/quotes/${revisionId}`, { token: sales })).data.quoteNo;
  const byLabel = (await call('GET', `/api/quotes?keyword=${encodeURIComponent(`${quoteNo}-R2`)}&latestOnly=false`, { token: sales })).data;
  assert.deepEqual(byLabel.items.map((row) => row.id), [revisionId]);
  assert.equal((await call('GET', `/api/quotes?keyword=${encodeURIComponent(`${quoteNo}-R9`)}&latestOnly=false`, { token: sales })).data.total, 0);
  // 每個專案只能有一份定案報價：另一份報價走完流程後，接受會被拒絕。
  const another = (await call('POST', `/api/projects/${projectId}/quotes`, { token: sales, body: { title: '追加報價', items } })).data.id;
  await call('POST', `/api/quotes/${another}/submit`, { token: sales });
  await call('POST', `/api/quotes/${another}/approve`, { token: manager });
  await call('POST', `/api/quotes/${another}/send`, { token: sales });
  const second = await call('POST', `/api/quotes/${another}/accept`, { token: sales });
  assert.equal(second.status, 400, '同專案已有定案報價');
  assert.match(second.data.message ?? JSON.stringify(second.data), /已有定案報價/);
  assert.equal((await call('GET', `/api/projects/${projectId}`, { token: sales })).data.contractAmount, 3400, '成交金額不被覆蓋');
});

await step('匯出 PDF 與 Excel', async () => {
  const pdf = await call('GET', `/api/quotes/${revisionId}/pdf`, { token: sales });
  assert.equal(pdf.status, 200);
  assert.equal(pdf.type, 'application/pdf');
  assert.equal(String.fromCharCode(...pdf.bytes.slice(0, 4)), '%PDF');
  const excel = await call('GET', `/api/quotes/${revisionId}/excel`, { token: sales });
  assert.equal(excel.status, 200);
  assert.equal(String.fromCharCode(...excel.bytes.slice(0, 2)), 'PK');
  assert.match(decodeURIComponent(excel.disposition), /-R2/, '刪除過的 R1 不重複使用，新版為 R2');
});

await step('Excel 往返：匯出的報價再匯入，明細與金額相同', async () => {
  const excel = await call('GET', `/api/quotes/${revisionId}/excel`, { token: sales });
  const uploaded = await call('POST', '/api/quote-imports', { token: sales, form: xlsxForm(excel.bytes, { kind: 'excel_template' }) });
  assert.equal(uploaded.status, 201, JSON.stringify(uploaded.data));
  const preview = uploaded.data.preview;
  assert.equal(preview.items.length, 3);
  assert.equal(preview.header.clientName ?? preview.header.ClientName, `${suffix} 報價客戶股份有限公司`);
  assert.deepEqual(preview.warnings, []);
  const header = preview.header;
  const quote = {
    title: header.Title,
    discountType: header.DiscountType,
    discountValue: Number(header.DiscountValue),
    items: preview.items.map(({ lineType, itemCode, name, spec, unit, quantity, unitPrice, discountRate, remark }) => ({ lineType, itemCode, name, spec, unit, quantity, unitPrice, discountRate, remark })),
  };
  const confirmed = await call('POST', `/api/quote-imports/${uploaded.data.id}/confirm`, { token: sales, body: { projectId, quote } });
  assert.equal(confirmed.status, 201, JSON.stringify(confirmed.data));
  const imported = (await call('GET', `/api/quotes/${confirmed.data.id}`, { token: sales })).data;
  assert.equal(imported.source, 'excel_template');
  assert.equal(imported.total, 3570);
  assert.equal((await call('POST', `/api/quote-imports/${uploaded.data.id}/confirm`, { token: sales, body: { projectId, quote } })).status, 400, '同一份匯入只能確認一次');
});

await step('匯入範本下載；非 .xlsx 被拒', async () => {
  const template = await call('GET', '/api/quotes/import-template', { token: sales });
  assert.equal(template.status, 200);
  const form = new FormData();
  form.append('file', new Blob(['a,b']), 'old.xls');
  assert.equal((await call('POST', '/api/quote-imports', { token: sales, form })).status, 400);
});

await step('舊報價：以對照設定（欄字母／儲存格）匯入', async () => {
  const profile = await call('POST', '/api/import-mapping-profiles', {
    token: sales,
    body: { name: `對照 ${suffix}`, headerRow: 11, dataStartRow: 12, columns: { Name: 'C', Unit: 'E', Quantity: 'F', UnitPrice: 'G' }, cells: { ClientName: 'B5', Title: 'B8' } },
  });
  assert.equal(profile.status, 201, JSON.stringify(profile.data));
  assert.equal((await call('POST', '/api/import-mapping-profiles', { token: sales, body: { name: 'bad', columns: { Name: '3' } } })).status, 400);
  const excel = await call('GET', `/api/quotes/${revisionId}/excel`, { token: sales });
  const uploaded = await call('POST', '/api/quote-imports', { token: sales, form: xlsxForm(excel.bytes, { kind: 'legacy_excel', mappingProfileId: profile.data.id }) });
  assert.equal(uploaded.status, 201, JSON.stringify(uploaded.data));
  const preview = uploaded.data.preview;
  assert.deepEqual(preview.items.map((item) => item.name), ['一、設備', '伺服器', '安裝工資']);
  assert.equal(preview.items[1].quantity, 2);
  assert.equal(preview.header.ClientName, `${suffix} 報價客戶股份有限公司`);
  assert.equal(preview.header.Title, '機房設備');
});

await step('資料範圍：其他業務看不到也搜不到這些報價', async () => {
  assert.equal((await call('GET', `/api/quotes/${revisionId}`, { token: otherSales })).status, 404);
  const search = (await call('GET', `/api/quotes?keyword=${encodeURIComponent(suffix)}`, { token: otherSales })).data;
  assert.equal(search.total, 0);
  const mine = (await call('GET', `/api/quotes?keyword=${encodeURIComponent(suffix)}&status=accepted`, { token: sales })).data;
  assert.equal(mine.total, 1);
  const all = (await call('GET', `/api/projects/${projectId}/quotes`, { token: sales })).data;
  assert.equal(all.total, 4, '專案報價清單包含舊版本、被擋下的追加報價與匯入的新報價');
});

await step('品項價目：業務只能查詢，成本只給 view_cost', async () => {
  const created = await call('POST', '/api/products', { token: admin, body: { code: `P-${suffix}`, name: `${suffix} 交換器`, unit: '台', defaultPrice: 12000, defaultCost: 8000 } });
  assert.equal(created.status, 201);
  assert.equal((await call('POST', '/api/products', { token: sales, body: { code: 'x', name: 'x' } })).status, 403);
  const forSales = (await call('GET', `/api/products?keyword=${suffix}`, { token: sales })).data.items[0];
  assert.equal(forSales.defaultCost, null);
  const forAdmin = (await call('GET', `/api/products?keyword=${suffix}`, { token: admin })).data.items[0];
  assert.equal(forAdmin.defaultCost, 8000);
});

await step('客戶 Excel：匯出、dryRun 預覽、正式匯入；業務沒有匯出入權限', async () => {
  assert.equal((await call('GET', '/api/clients/export', { token: sales })).status, 403);
  const exported = await call('GET', '/api/clients/export', { token: admin });
  assert.equal(exported.status, 200);
  const dry = await call('POST', '/api/clients/import?dryRun=true', { token: admin, form: xlsxForm(exported.bytes) });
  assert.equal(dry.status, 200, JSON.stringify(dry.data));
  assert.equal(dry.data.summary.errors, 0);
  assert.equal(dry.data.summary.created, 0);
  assert.ok(dry.data.summary.updated > 0);
  const real = await call('POST', '/api/clients/import', { token: admin, form: xlsxForm(exported.bytes) });
  assert.equal(real.data.summary.updated, dry.data.summary.updated);
  const client = (await call('GET', `/api/clients/${clientId}`, { token: admin })).data;
  assert.equal(client.contacts.length, 1, '同名聯絡人不重複建立');
  assert.equal((await call('POST', '/api/clients/import?dryRun=true', { token: sales, form: xlsxForm(exported.bytes) })).status, 403);
});

await step('未得標專案重新報價：送審舊草稿或新增報價都回到報價中；報價列表可用客戶簡稱搜尋', async () => {
  const shortName = `簡${suffix}`;
  const client = await call('POST', '/api/clients', { token: sales, body: { name: `${suffix} 重報客戶`, shortName } });
  assert.equal(client.status, 201, JSON.stringify(client.data));
  const project = (await call('POST', '/api/projects', { token: sales, body: { name: `${suffix} 重新報價`, clientId: client.data.id } })).data.id;
  const status = async () => (await call('GET', `/api/projects/${project}`, { token: sales })).data;
  const markLost = async () => assert.equal((await call('POST', `/api/projects/${project}/status`, { token: sales, body: { status: 'lost', reason: '價格' } })).status, 200);

  const draft = (await call('POST', `/api/projects/${project}/quotes`, { token: sales, body: { items } })).data.id;
  assert.equal((await status()).status, 'quoting');
  await markLost();
  assert.equal((await call('POST', `/api/quotes/${draft}/submit`, { token: sales })).status, 200);
  let detail = await status();
  assert.equal(detail.status, 'quoting', '送審未得標前建立的草稿要回到報價中');
  assert.match(detail.history[0].reason, /重新報價/);

  await markLost();
  assert.equal((await call('POST', `/api/projects/${project}/quotes`, { token: sales, body: { items } })).status, 201);
  detail = await status();
  assert.equal(detail.status, 'quoting', '未得標的專案新增報價要回到報價中');
  assert.match(detail.history[0].reason, /重新報價：建立報價單/);

  const found = (await call('GET', `/api/quotes?keyword=${encodeURIComponent(shortName)}`, { token: sales })).data;
  assert.equal(found.total, 2, '以客戶簡稱找到這個客戶的兩份報價');
});

await step('清理：停用這次建立的測試帳號（不讓公司通訊錄與成員下拉堆滿測試帳號）', async () => {
  for (const account of [`qs-${suffix}`]) {
    const found = (await call('GET', `/api/admin/users?keyword=${account}`, { token: admin })).data.items.find((user) => user.account === account);
    if (found) assert.equal((await call('PATCH', `/api/admin/users/${found.membershipId}`, { token: admin, body: { isActive: false } })).status, 200, account);
  }
});

console.log(`\n全部 ${passed} 項通過`);
