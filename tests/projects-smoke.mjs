/**
 * 第二階段 API 端對端驗證：客戶與聯絡人、專案資料範圍（own／department_tree／company）、
 * 專案成員（viewer 只能讀）、搜尋（狀態／時間／金額）、狀態流程、刪除規則、單號併發與公司隔離。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動，且已執行 Seeder（SeedDemoData=true）。
 * 用法：node tests/projects-smoke.mjs [baseUrl]
 */
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
const envText = await readFile(new URL('../.env.development-api.local', import.meta.url), 'utf8');
const seedPassword = envText.match(/^Quote__BootstrapAdminPassword=(.+)$/m)?.[1]?.trim();
assert.ok(seedPassword, '找不到 Quote__BootstrapAdminPassword');

const suffix = Date.now().toString(36);
let passed = 0;

async function call(method, path, { token, body } = {}) {
  const headers = { 'content-type': 'application/json' };
  if (token) headers.authorization = `Bearer ${token}`;
  const response = await fetch(`${baseUrl}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  return { status: response.status, data: text ? JSON.parse(text) : null };
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

const today = new Date(Date.now() + 8 * 3600_000).toISOString().slice(0, 10);
const search = async (token, query = {}) => {
  const params = new URLSearchParams({ keyword: suffix, pageSize: '100' });
  Object.entries(query).forEach(([key, value]) => (Array.isArray(value) ? value.forEach((v) => params.append(key, v)) : params.set(key, String(value))));
  const result = await call('GET', `/api/projects?${params}`, { token });
  assert.equal(result.status, 200, JSON.stringify(result.data));
  return result.data;
};
const names = (result) => result.items.map((item) => item.name.split(' ')[0]).sort();

const admin = await login('demo-admin');
const manager = await login('demo-manager');
const sales = await login('demo-sales');
let departments;
let users;
let salesClientId;
let adminClientId;
const projects = {};

await step('通訊錄：業務也能讀取成員與部門選項', async () => {
  // 人員下拉由後端搜尋：以姓名關鍵字查詢，不是一次取回整份名單。
  const lookups = await Promise.all(['林業務', '王業務主管', '示範公司管理員'].map((keyword) => call('GET', `/api/directory/users?keyword=${encodeURIComponent(keyword)}`, { token: sales })));
  const departmentResult = await call('GET', '/api/directory/departments', { token: sales });
  assert.ok(lookups.every((result) => result.status === 200));
  assert.equal(departmentResult.status, 200);
  users = Object.fromEntries(lookups.flatMap((result) => result.data).map((user) => [user.displayName, user.userId]));
  departments = Object.fromEntries(departmentResult.data.map((department) => [department.name, department.id]));
  assert.ok(users['林業務'] && departments['財務部']);
});

await step('業務建立客戶：自動代碼、聯絡人與主要聯絡人', async () => {
  const created = await call('POST', '/api/clients', {
    token: sales,
    body: {
      name: `${suffix} 客戶甲`,
      taxId: '24536806',
      paymentTerms: '月結 30 天',
      contacts: [{ name: '陳經理', email: 'chen@example.com', isPrimary: true }, { name: '李工程師', isPrimary: false }],
    },
  });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  assert.match(created.data.code, /^C\d{4}$/);
  salesClientId = created.data.id;
  const detail = (await call('GET', `/api/clients/${salesClientId}`, { token: sales })).data;
  assert.equal(detail.contacts.length, 2);
  assert.equal(detail.contacts[0].name, '陳經理');
  assert.equal(detail.canEdit, true);
});

await step('客戶驗證：統編格式、主要聯絡人只能一位', async () => {
  assert.equal((await call('POST', '/api/clients', { token: sales, body: { name: 'x', taxId: '123' } })).status, 400);
  const twoPrimary = await call('POST', '/api/clients', { token: sales, body: { name: 'x', contacts: [{ name: 'a', isPrimary: true }, { name: 'b', isPrimary: true }] } });
  assert.equal(twoPrimary.status, 400);
});

await step('客戶依所屬部門隔離：本人與部門看自己部門、部門樹含下層、全公司看全部；只有全公司範圍能移動', async () => {
  // 業務（業務一課）建立的客戶預設歸在自己的部門；指定其他部門會被拒絕。
  const detailA = (await call('GET', `/api/clients/${salesClientId}`, { token: sales })).data;
  assert.equal(detailA.departmentName, '業務一課');
  assert.equal(detailA.canChangeDepartment, false);
  const spoof = await call('POST', '/api/clients', { token: sales, body: { name: 'x', departmentId: departments['財務部'] } });
  assert.equal(spoof.status, 403, '業務不能指定其他部門');

  // 公司管理員（全公司範圍）建立財務部的客戶 B、業務部（業務一課的上層）的客戶 C。
  const finance = await call('POST', '/api/clients', { token: admin, body: { name: `${suffix} 客戶乙`, departmentId: departments['財務部'] } });
  assert.equal(finance.status, 201, JSON.stringify(finance.data));
  adminClientId = finance.data.id;
  const salesDept = await call('POST', '/api/clients', { token: admin, body: { name: `${suffix} 客戶丙`, departmentId: departments['業務部'] } });
  assert.equal(salesDept.status, 201);
  const upperClientId = salesDept.data.id;

  const ids = async (token) => (await call('GET', `/api/clients?keyword=${suffix}&pageSize=100`, { token })).data.items.map((item) => item.name.replace(`${suffix} `, '')).sort();
  assert.deepEqual(await ids(sales), ['客戶甲'], '業務一課只看到自己部門的客戶，看不到上層部門與其他部門的');
  assert.deepEqual(await ids(manager), ['客戶丙', '客戶甲'], '業務部（部門樹）看到自己與下層業務一課，看不到財務部');
  assert.deepEqual(await ids(admin), ['客戶丙', '客戶乙', '客戶甲'], '全公司範圍看全部');
  assert.equal((await call('GET', `/api/clients/${adminClientId}`, { token: sales })).status, 404, '看不到的客戶一律 404');
  assert.equal((await call('GET', `/api/clients/${upperClientId}`, { token: sales })).status, 404);
  assert.equal((await call('GET', `/api/clients/${salesClientId}`, { token: manager })).status, 200);
  assert.equal((await call('PUT', `/api/clients/${adminClientId}`, { token: sales, body: { name: 'hack' } })).status, 404);

  // 看不到的客戶不能拿來建專案；報價的客戶選擇也一樣。
  const hidden = await call('POST', '/api/projects', { token: sales, body: { name: `${suffix} x`, clientId: adminClientId } });
  assert.equal(hidden.status, 400);

  // 只有全公司範圍的管理者能移動所屬部門；移走後原部門的人立刻看不到。
  assert.equal((await call('PUT', `/api/clients/${salesClientId}`, { token: sales, body: { name: `${suffix} 客戶甲`, departmentId: departments['財務部'] } })).status, 403);
  assert.equal((await call('GET', `/api/clients/${adminClientId}`, { token: admin })).data.canChangeDepartment, true);
  const moved = await call('PUT', `/api/clients/${salesClientId}`, { token: admin, body: { name: `${suffix} 客戶甲`, departmentId: departments['財務部'] } });
  assert.equal(moved.status, 200, JSON.stringify(moved.data));
  assert.equal((await call('GET', `/api/clients/${salesClientId}`, { token: sales })).status, 404, '被移走的客戶原部門看不到');
  assert.deepEqual(await ids(sales), []);
  assert.equal((await call('PUT', `/api/clients/${salesClientId}`, { token: admin, body: { name: `${suffix} 客戶甲`, departmentId: departments['業務一課'] } })).status, 200);
  assert.deepEqual(await ids(sales), ['客戶甲'], '移回後恢復');

  // 匯出只含看得到的客戶（匯入也只比對看得到的）
  const csvAdmin = await fetch(`${baseUrl}/api/clients/export`, { headers: { authorization: `Bearer ${admin}` } });
  assert.equal(csvAdmin.status, 200);
});

await step('業務建立專案：預設自己部門、專案編號格式', async () => {
  const created = await call('POST', '/api/projects', {
    token: sales,
    body: { name: `A ${suffix} 機房工程`, clientId: salesClientId, startDate: today, expectedAmount: 500000 },
  });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  assert.match(created.data.projectNo, /^P\d{4}-\d{4}$/);
  projects.A = created.data.id;
  const detail = (await call('GET', `/api/projects/${projects.A}`, { token: sales })).data;
  assert.equal(detail.departmentName, '業務一課');
  assert.equal(detail.myRole, 'owner');
  assert.deepEqual(detail.nextStatuses, ['quoting', 'cancelled']);
});

await step('業務不能把專案建在自己範圍外的部門', async () => {
  const outside = await call('POST', '/api/projects', { token: sales, body: { name: `X ${suffix}`, clientId: salesClientId, departmentId: departments['財務部'] } });
  assert.equal(outside.status, 400);
});

await step('公司管理員建立其他部門的專案', async () => {
  const b = await call('POST', '/api/projects', { token: admin, body: { name: `B ${suffix} 總部專案`, clientId: adminClientId, departmentId: departments['財務部'], expectedAmount: 2000000 } });
  const c = await call('POST', '/api/projects', { token: admin, body: { name: `C ${suffix} 業務部專案`, clientId: adminClientId, departmentId: departments['業務部'], expectedAmount: 800000 } });
  assert.equal(b.status, 201);
  assert.equal(c.status, 201);
  projects.B = b.data.id;
  projects.C = c.data.id;
});

await step('資料範圍：own／department_tree／company 看到不同專案', async () => {
  assert.deepEqual(names(await search(sales)), ['A']);
  assert.deepEqual(names(await search(manager)), ['A', 'C']);
  assert.deepEqual(names(await search(admin)), ['A', 'B', 'C']);
  assert.equal((await call('GET', `/api/projects/${projects.B}`, { token: sales })).status, 404);
});

await step('部門主管可修改下層專案（不靠成員身分）', async () => {
  const detail = (await call('GET', `/api/projects/${projects.A}`, { token: manager })).data;
  assert.equal(detail.canWrite, true);
});

await step('被加入為 viewer 只能讀；改為 member 後可修改', async () => {
  const salesUserId = users['林業務'];
  assert.equal((await call('PUT', `/api/projects/${projects.B}/members`, { token: admin, body: [{ userId: salesUserId, projectRole: 'viewer' }] })).status, 200);
  const asViewer = await call('GET', `/api/projects/${projects.B}`, { token: sales });
  assert.equal(asViewer.status, 200);
  assert.equal(asViewer.data.canWrite, false);
  const body = { name: `B ${suffix} 總部專案`, clientId: adminClientId, expectedAmount: 2000000 };
  assert.equal((await call('PUT', `/api/projects/${projects.B}`, { token: sales, body })).status, 403);
  assert.equal((await call('PUT', `/api/projects/${projects.B}/members`, { token: admin, body: [{ userId: salesUserId, projectRole: 'member' }] })).status, 200);
  assert.equal((await call('PUT', `/api/projects/${projects.B}`, { token: sales, body })).status, 200);
  assert.deepEqual(names(await search(sales)), ['A', 'B']);
  assert.deepEqual(names(await search(sales, { mine: true })), ['A', 'B']);
});

await step('業務沒有 assign_member 以外的越權：不能把負責人加為成員、不能加重複成員', async () => {
  const adminUserId = users['示範公司管理員'];
  assert.equal((await call('PUT', `/api/projects/${projects.B}/members`, { token: admin, body: [{ userId: adminUserId, projectRole: 'member' }] })).status, 400);
});

await step('搜尋：金額區間、日期區間、狀態多選與狀態計數', async () => {
  assert.deepEqual(names(await search(admin, { amountMin: 1000000 })), ['B']);
  assert.deepEqual(names(await search(admin, { amountMin: 600000, amountMax: 900000 })), ['C']);
  assert.deepEqual(names(await search(admin, { dateField: 'start', from: today, to: today })), ['A']);
  assert.deepEqual(names(await search(admin, { dateField: 'created', from: today, to: today })), ['A', 'B', 'C']);
  assert.equal((await call('GET', '/api/projects?from=2026-02-01&to=2026-01-01', { token: admin })).status, 400);
  assert.equal((await call('POST', `/api/projects/${projects.A}/status`, { token: sales, body: { status: 'quoting' } })).status, 200);
  const quoting = await search(admin, { status: ['quoting'] });
  assert.deepEqual(names(quoting), ['A']);
  assert.equal(quoting.statusCounts.draft, 2);
  assert.equal(quoting.statusCounts.quoting, 1);
  assert.deepEqual(names(await search(admin, { status: ['quoting', 'draft'] })), ['A', 'B', 'C']);
  const sorted = await search(admin, { sort: 'expectedAmount', desc: false });
  assert.deepEqual(sorted.items.map((item) => item.name[0]), ['A', 'C', 'B']);
  assert.equal(sorted.sumExpectedAmount, 3300000);
});

await step('狀態流程：不合法轉換、未得標需原因、結案需 close 權限、結案後不可改', async () => {
  const change = (token, status, reason) => call('POST', `/api/projects/${projects.A}/status`, { token, body: { status, reason } });
  assert.equal((await change(sales, 'completed')).status, 400);
  assert.equal((await change(sales, 'lost')).status, 400);
  assert.equal((await change(sales, 'won')).status, 200);
  assert.ok((await call('GET', `/api/projects/${projects.A}`, { token: sales })).data.wonAt);
  assert.equal((await change(sales, 'in_progress')).status, 200);
  assert.equal((await change(sales, 'completed')).status, 200);
  const detail = (await call('GET', `/api/projects/${projects.A}`, { token: sales })).data;
  assert.ok(!detail.nextStatuses.includes('closed'), '業務沒有 close 子功能，不應出現結案選項');
  assert.equal((await change(sales, 'closed')).status, 403);
  assert.equal((await change(admin, 'closed')).status, 200);
  const history = (await call('GET', `/api/projects/${projects.A}`, { token: admin })).data.history;
  assert.equal(history.length, 6);
  assert.equal((await call('PUT', `/api/projects/${projects.A}`, { token: admin, body: { name: 'x', clientId: salesClientId } })).status, 400);
});

await step('刪除規則：只有草稿／已取消可刪；有專案的客戶不可刪', async () => {
  assert.equal((await call('DELETE', `/api/projects/${projects.A}`, { token: admin })).status, 400);
  assert.equal((await call('DELETE', `/api/projects/${projects.C}`, { token: admin })).status, 204);
  assert.equal((await call('GET', `/api/projects/${projects.C}`, { token: admin })).status, 404);
  assert.equal((await call('DELETE', `/api/clients/${salesClientId}`, { token: admin })).status, 400);
  const lonely = await call('POST', '/api/clients', { token: admin, body: { name: `${suffix} 無專案客戶` } });
  assert.equal((await call('DELETE', `/api/clients/${lonely.data.id}`, { token: admin })).status, 204);
});

await step('同時建立 8 個專案，編號不重複', async () => {
  const results = await Promise.all(
    Array.from({ length: 8 }, (_, index) => call('POST', '/api/projects', { token: admin, body: { name: `Z${index} ${suffix} 併發`, clientId: adminClientId } })),
  );
  assert.ok(results.every((result) => result.status === 201), JSON.stringify(results.map((r) => r.data)));
  assert.equal(new Set(results.map((result) => result.data.projectNo)).size, 8);
});

await step('公司隔離：其他公司看不到示範公司的專案與客戶', async () => {
  const platform = await login('admin');
  const created = await call('POST', '/api/platform/companies', {
    token: platform,
    body: { name: `隔離測試 ${suffix}`, slug: `iso-${suffix}`, adminAccount: `iso-${suffix}`, adminDisplayName: '隔離管理員', adminPassword: seedPassword },
  });
  assert.equal(created.status, 201);
  const other = await login(`iso-${suffix}`);
  assert.equal((await call('GET', `/api/projects/${projects.B}`, { token: other })).status, 404);
  assert.equal((await call('GET', `/api/clients/${adminClientId}`, { token: other })).status, 404);
  assert.equal((await search(other)).total, 0);
  const otherClient = await call('POST', '/api/clients', { token: other, body: { name: '別家客戶' } });
  assert.equal(otherClient.data.code, 'C0001', '每間公司的客戶代碼各自從 C0001 起算');
  const crossProject = await call('POST', '/api/projects', { token: other, body: { name: 'x', clientId: adminClientId } });
  assert.equal(crossProject.status, 400, '不可用別家公司的客戶建立專案');
  await call('PATCH', `/api/platform/companies/${created.data.id}`, { token: platform, body: { status: 'suspended' } });
});

console.log(`\n全部 ${passed} 項通過`);
