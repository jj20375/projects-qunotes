/**
 * 第五階段 Dashboard 驗證：在獨立的新公司建立已知資料，核對三種業績口徑、月／季／年、去年同期、
 * 案件數與成交率、業務排行、到期保固、應收帳款、資料範圍、金額權限、部門篩選與會計年度設定。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動。
 * 用法：node tests/dashboard-smoke.mjs [baseUrl]
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
  const headers = {};
  if (token) headers.authorization = `Bearer ${token}`;
  if (body !== undefined) headers['content-type'] = 'application/json';
  const response = await fetch(`${baseUrl}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  return { status: response.status, data: text ? JSON.parse(text) : null };
}

async function login(account) {
  const result = await call('POST', '/api/auth/login', { body: { account, password: seedPassword } });
  assert.equal(result.status, 200, `${account} 登入失敗：${JSON.stringify(result.data)}`);
  return result.data.accessToken;
}

async function ok(promise, expected = [200, 201]) {
  const result = await promise;
  assert.ok(expected.includes(result.status), JSON.stringify(result.data));
  return result.data;
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

// 台灣時間的今天、昨天、30 天後、去年同日
const taipei = (offsetDays = 0, years = 0) => {
  const date = new Date(Date.now() + 8 * 3600_000 + offsetDays * 86400_000);
  date.setUTCFullYear(date.getUTCFullYear() + years);
  return date.toISOString().slice(0, 10);
};
const today = taipei();
const summary = async (token, query = '') => ok(call('GET', `/api/dashboard/summary?metric=orders${query}`, { token }));

const platform = await login('admin');
let admin;
let salesA;
let salesB;
let viewer;
let salesDept;
let financeDept;

await step('準備：新公司、部門、兩位業務、一位唯讀成員', async () => {
  await ok(call('POST', '/api/platform/companies', { token: platform, body: { name: `Dashboard ${suffix}`, slug: `dash-${suffix}`, adminAccount: `dash-${suffix}`, adminDisplayName: '戰情管理員', adminPassword: seedPassword } }));
  admin = await login(`dash-${suffix}`);
  const root = (await ok(call('GET', '/api/admin/departments', { token: admin })))[0];
  salesDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: root.id, name: '業務部' } }))).id;
  financeDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: root.id, name: '財務部' } }))).id;
  const roles = await ok(call('GET', '/api/admin/roles', { token: admin }));
  const role = (code) => roles.find((value) => value.code === code).id;
  for (const [account, roleCode] of [['a', 'sales'], ['b', 'sales'], ['v', 'viewer']]) {
    await ok(call('POST', '/api/admin/users', { token: admin, body: { account: `dash-${account}-${suffix}`, displayName: `業務${account.toUpperCase()}`, password: seedPassword, departmentId: salesDept, roleId: role(roleCode) } }));
  }
  salesA = await login(`dash-a-${suffix}`);
  salesB = await login(`dash-b-${suffix}`);
  viewer = await login(`dash-v-${suffix}`);
});

let p1;
let p2;
await step('準備：定案報價、訂單（含去年同月與已取消）、發票（逾期未收與已收）、未得標、保固', async () => {
  // 客戶歸在業務部：兩位業務才選得到（客戶依所屬部門隔離）。
  const client = (await ok(call('POST', '/api/clients', { token: admin, body: { name: '戰情客戶', departmentId: salesDept } }))).id;
  p1 = (await ok(call('POST', '/api/projects', { token: salesA, body: { name: 'P1', clientId: client } }))).id;
  p2 = (await ok(call('POST', '/api/projects', { token: salesB, body: { name: 'P2', clientId: client } }))).id;
  const p3 = (await ok(call('POST', '/api/projects', { token: admin, body: { name: 'P3 財務部', clientId: client, departmentId: financeDept } }))).id;
  const p4 = (await ok(call('POST', '/api/projects', { token: salesB, body: { name: 'P4 未得標', clientId: client } }))).id;

  // 訂單必須掛在定案報價下：P1、P2、P3 各接受一份報價（報價接受口徑合計 10,000 + 20,000 + 5,000）。
  const accept = async (project, token, unitPrice) => {
    const quote = await ok(call('POST', `/api/projects/${project}/quotes`, { token, body: { items: [{ name: '服務', quantity: 1, unitPrice }] } }));
    await ok(call('POST', `/api/quotes/${quote.id}/submit`, { token }));
    await ok(call('POST', `/api/quotes/${quote.id}/approve`, { token: admin }));
    await ok(call('POST', `/api/quotes/${quote.id}/send`, { token }));
    await ok(call('POST', `/api/quotes/${quote.id}/accept`, { token }));
    return quote.id;
  };
  const quotes = { [p1]: await accept(p1, salesA, 10000), [p2]: await accept(p2, salesB, 20000), [p3]: await accept(p3, admin, 5000) };

  const order = async (project, date, amount, status) =>
    (await ok(call('POST', `/api/projects/${project}/orders`, { token: admin, body: { quoteId: quotes[project], orderNo: `O-${amount}`, orderDate: date, amount, taxAmount: 0, status } }))).id;
  const p1Order = await order(p1, today, 100000);
  await order(p1, taipei(0, -1), 50000);
  const p2Order = await order(p2, today, 30000);
  await order(p2, today, 999999, 'cancelled');
  await order(p3, today, 7000);

  const invoice = (project, orderId, no, amount, dueDate) =>
    ok(call('POST', `/api/projects/${project}/invoices`, { token: admin, body: { orderId, invoiceNo: no, invoiceDate: today, salesAmount: amount, taxAmount: amount * 0.05, dueDate } }));
  const n = String(Date.now()).slice(-8);
  await invoice(p1, p1Order, `DA-${n}`, 80000, taipei(-1));
  const paid = await invoice(p2, p2Order, `DB-${n}`, 20000, taipei(30));
  await ok(call('POST', `/api/invoices/${paid.id}/payment`, { token: admin, body: { paidAmount: 21000 } }));

  await ok(call('POST', `/api/projects/${p4}/status`, { token: salesB, body: { status: 'quoting' } }));
  await ok(call('POST', `/api/projects/${p4}/status`, { token: salesB, body: { status: 'lost', reason: '價格' } }));
  await ok(call('POST', `/api/projects/${p1}/warranties`, { token: admin, body: { orderId: p1Order, warrantyNo: 'W1', subject: '主機', startDate: today, endDate: taipei(30) } }));
});

await step('公司管理員：接單口徑的本月、去年同月、年度累計與成長率（排除已取消訂單）', async () => {
  const data = await summary(admin);
  const month = data.currentMonthIndex;
  assert.equal(data.trend.thisYear[month], 137000);
  assert.equal(data.trend.lastYear[month], 50000);
  assert.equal(data.kpi.yearToDate, 137000);
  assert.equal(data.kpi.lastYearToDate, 50000);
  assert.equal(data.kpi.growthRate, 174);
  assert.equal(data.kpi.currentMonth, 137000);
  assert.equal(data.quarters.thisYear[Math.floor(month / 3)], 137000);
  assert.equal(data.monthLabels.length, 12);
});

await step('三種業績口徑的年度合計（未稅）', async () => {
  const data = await summary(admin);
  assert.deepEqual(data.totals, { accepted: 35000, orders: 137000, invoiced: 100000 });
  const accepted = await ok(call('GET', '/api/dashboard/summary?metric=accepted', { token: admin }));
  assert.equal(accepted.trend.thisYear[accepted.currentMonthIndex], 35000);
  assert.equal((await call('GET', '/api/dashboard/summary?metric=profit', { token: admin })).status, 400);
});

await step('案件數與成交率：得標 3、未得標 1 → 75%', async () => {
  const { projects } = await summary(admin);
  assert.equal(projects.won, 3);
  assert.equal(projects.lost, 1);
  assert.equal(projects.winRate, 75);
  assert.equal(projects.createdThisYear, 4);
  assert.equal(projects.statusCounts.won, 3);
  assert.equal(projects.statusCounts.lost, 1);
});

await step('業務排行、到期保固、應收帳款（逾期）', async () => {
  const data = await summary(admin);
  assert.deepEqual(data.ranking.map((row) => [row.name, row.amount]), [['業務A', 100000], ['業務B', 30000], ['戰情管理員', 7000]]);
  assert.equal(data.expiringWarranties.count, 1);
  assert.equal(data.receivables.count, 1);
  assert.equal(data.receivables.overdueCount, 1);
  assert.equal(data.receivables.outstanding, 84000);
  assert.equal(data.receivables.items[0].isOverdue, true);
});

await step('部門篩選：業務部不含財務部的專案', async () => {
  const data = await summary(admin, `&departmentId=${salesDept}`);
  assert.equal(data.totals.orders, 130000);
});

await step('資料範圍：業務 A 只看到自己的業績，沒有全員排行', async () => {
  const data = await summary(salesA);
  assert.deepEqual(data.totals, { accepted: 10000, orders: 100000, invoiced: 80000 });
  assert.equal(data.ranking, null);
  assert.equal(data.receivables.outstanding, 84000);
  const b = await summary(salesB);
  assert.equal(b.totals.orders, 30000);
  assert.equal(b.projects.lost, 1);
  assert.equal(b.projects.won, 1);
});

await step('沒有 view_amount：只有件數、金額全為 null', async () => {
  const data = await summary(viewer);
  assert.equal(data.canViewAmount, false);
  assert.equal(data.kpi.yearToDate, null);
  assert.ok(data.trend.thisYear.every((value) => value === null));
  assert.deepEqual(data.totals, { accepted: null, orders: null, invoiced: null });
  assert.equal(typeof data.projects.createdThisYear, 'number');
});

await step('公司參數：會計年度起始月影響月份標籤；業務不能修改', async () => {
  assert.equal((await call('PUT', '/api/admin/settings', { token: salesA, body: { fiscalYearStartMonth: 4 } })).status, 403);
  assert.equal((await call('PUT', '/api/admin/settings', { token: admin, body: { fiscalYearStartMonth: 13 } })).status, 400);
  assert.equal((await call('PUT', '/api/admin/settings', { token: admin, body: { fiscalYearStartMonth: 4, defaultTaxRate: 5, quoteValidDays: 45 } })).status, 204);
  const settings = await ok(call('GET', '/api/admin/settings', { token: admin }));
  assert.equal(settings.quoteValidDays, 45);
  const data = await summary(admin);
  assert.equal(data.fiscalYearStartMonth, 4);
  assert.match(data.monthLabels[0], /-04$/);
  assert.equal(data.kpi.yearToDate, 137000, '本月仍在同一會計年度內');
});

console.log(`\n全部 ${passed} 項通過`);
