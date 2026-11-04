/**
 * 專案跟進紀錄驗證：資料範圍與權限、最新一筆規則（補登、刪除倒回、不用跟進）、輸入檢查、已結案唯讀、
 * 專案內列表與跨專案總表的篩選分頁，以及專案列表的跟進狀態篩選與排序。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動，並已執行 Seeder（followups 權限已建立）。
 * 用法：node tests/followups-smoke.mjs [baseUrl]
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
  assert.ok(expected.includes(result.status), `${result.status} ${JSON.stringify(result.data)}`);
  return result.data;
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

// 台灣日期（offsetDays 天後）與 N 小時前的 ISO 時間
const taipeiDate = (offsetDays = 0) => new Date(Date.now() + 8 * 3600_000 + offsetDays * 86400_000).toISOString().slice(0, 10);
const hoursAgo = (hours) => new Date(Date.now() - hours * 3600_000).toISOString();
const sameInstant = (a, b) => Math.abs(Date.parse(a) - Date.parse(b)) < 1000;

const write = (token, project, body) => call('POST', `/api/projects/${project}/followups`, { token, body });
const base = (extra = {}) => ({ type: 'call', occurredAt: hoursAgo(2), summary: '確認規格', ...extra });

const platform = await login('admin');
let admin;
let manager;
let salesA;
let salesB;
let accountant;
let salesDept;
let teamDept;
let financeDept;
let otherClientContact;
let contactId;
let pA;
let pB;
let pF;
let r0;
let r1;
let r1At;
let rB;
let rBAuthor;
let rF;

await step('準備：新公司、業務部與下層一課、財務部、主管、兩位業務、會計', async () => {
  await ok(call('POST', '/api/platform/companies', { token: platform, body: { name: `Followups ${suffix}`, slug: `fu-${suffix}`, adminAccount: `fu-${suffix}`, adminDisplayName: '跟進管理員', adminPassword: seedPassword } }));
  admin = await login(`fu-${suffix}`);
  const root = (await ok(call('GET', '/api/admin/departments', { token: admin })))[0];
  salesDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: root.id, name: '業務部' } }))).id;
  teamDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: salesDept, name: '業務一課' } }))).id;
  financeDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: root.id, name: '財務部' } }))).id;
  const roles = await ok(call('GET', '/api/admin/roles', { token: admin }));
  const role = (code) => roles.find((value) => value.code === code).id;
  for (const [account, roleCode, departmentId, displayName] of [
    ['m', 'sales-manager', salesDept, '主管'],
    ['a', 'sales', teamDept, '業務A'],
    ['b', 'sales', teamDept, '業務B'],
    ['c', 'accountant', financeDept, '會計'],
  ]) {
    await ok(call('POST', '/api/admin/users', { token: admin, body: { account: `fu-${account}-${suffix}`, displayName, password: seedPassword, departmentId, roleId: role(roleCode) } }));
  }
  manager = await login(`fu-m-${suffix}`);
  salesA = await login(`fu-a-${suffix}`);
  salesB = await login(`fu-b-${suffix}`);
  accountant = await login(`fu-c-${suffix}`);
});

await step('準備：客戶（兩位聯絡人）、另一個客戶、三個專案（業務 A、業務 B、財務部）', async () => {
  // 客戶歸在業務一課：本人範圍的業務才選得到（客戶依所屬部門隔離）。
  const client = (await ok(call('POST', '/api/clients', { token: admin, body: { name: '跟進客戶', departmentId: teamDept, contacts: [{ name: '陳經理', title: '採購', isPrimary: true }, { name: '李工程師', isPrimary: false }] } }))).id;
  const other = (await ok(call('POST', '/api/clients', { token: admin, body: { name: '其他客戶', departmentId: teamDept, contacts: [{ name: '王小姐', isPrimary: true }] } }))).id;
  otherClientContact = (await ok(call('GET', `/api/clients/${other}`, { token: admin }))).contacts[0].id;
  pA = (await ok(call('POST', '/api/projects', { token: salesA, body: { name: `FU-A ${suffix}`, clientId: client } }))).id;
  pB = (await ok(call('POST', '/api/projects', { token: salesB, body: { name: `FU-B ${suffix}`, clientId: client } }))).id;
  pF = (await ok(call('POST', '/api/projects', { token: admin, body: { name: `FU-F ${suffix}`, clientId: client, departmentId: financeDept } }))).id;
});

await step('新增：選了聯絡人就以聯絡人姓名為準，回傳重算後的下次聯絡日', async () => {
  const contacts = await ok(call('GET', `/api/projects/${pA}/followups/contacts?keyword=${encodeURIComponent('陳')}`, { token: salesA }));
  assert.deepEqual(contacts.map((value) => value.name), ['陳經理']);
  contactId = contacts[0].id;
  const created = await ok(write(salesA, pA, base({ contactId, contactName: '亂打的名字', nextFollowUpOn: taipeiDate(3), nextAction: '寄修正版報價' })), [201]);
  r1 = created.item.id;
  r1At = created.item.occurredAt;
  assert.equal(created.item.contactName, '陳經理');
  assert.equal(created.item.createdBy.displayName, '業務A');
  assert.equal(created.item.updatedBy, null);
  assert.equal(created.project.nextFollowUpOn, taipeiDate(3));
  assert.equal(created.project.nextAction, '寄修正版報價');
  assert.ok(sameInstant(created.project.lastFollowUpAt, r1At));
});

await step('權限：會計沒有 followups（403）；看不到專案的業務 B 讀寫皆 404', async () => {
  assert.equal((await call('GET', `/api/projects/${pA}/followups`, { token: accountant })).status, 403);
  for (const result of [
    await call('GET', `/api/projects/${pA}/followups`, { token: salesB }),
    await call('GET', `/api/projects/${pA}/followups/contacts`, { token: salesB }),
    await write(salesB, pA, base()),
    await call('PUT', `/api/followups/${r1}`, { token: salesB, body: base() }),
    await call('DELETE', `/api/followups/${r1}`, { token: salesB }),
    await call('POST', `/api/projects/${pA}/followups/dismiss-next`, { token: salesB }),
  ]) {
    assert.equal(result.status, 404, JSON.stringify(result.data));
  }
});

await step('最新一筆：補登較早的紀錄不改變下次聯絡；新的一筆沒排定就清空，刪掉後倒回', async () => {
  const backfill = await ok(write(salesA, pA, base({ type: 'visit', occurredAt: hoursAgo(72), summary: `補登拜訪 ${suffix}`, nextFollowUpOn: taipeiDate(10) })), [201]);
  r0 = backfill.item.id;
  assert.equal(backfill.project.nextFollowUpOn, taipeiDate(3));
  const newer = await ok(write(salesA, pA, base({ type: 'meeting', occurredAt: hoursAgo(1), summary: '開會' })), [201]);
  assert.equal(newer.project.nextFollowUpOn, null);
  assert.equal(newer.project.nextAction, null);
  const removed = await ok(call('DELETE', `/api/followups/${newer.item.id}`, { token: salesA }));
  assert.equal(removed.project.nextFollowUpOn, taipeiDate(3));
  assert.equal(removed.project.nextAction, '寄修正版報價');
  assert.ok(sameInstant(removed.project.lastFollowUpAt, r1At));
});

await step('不用跟進：清掉下次聯絡、再按一次 400；主管修改日期後重新提醒', async () => {
  const dismissed = await ok(call('POST', `/api/projects/${pA}/followups/dismiss-next`, { token: salesA }));
  assert.equal(dismissed.project.nextFollowUpOn, null);
  const again = await call('POST', `/api/projects/${pA}/followups/dismiss-next`, { token: salesA });
  assert.equal(again.status, 400);
  assert.equal(again.data.message, '目前沒有排定下次聯絡。');
  // 主管（部門樹）不是撰寫人也能改：能寫專案的人都能改紀錄。
  const updated = await ok(call('PUT', `/api/followups/${r1}`, { token: manager, body: base({ occurredAt: r1At, contactId, summary: '確認規格（主管補充）', nextFollowUpOn: taipeiDate(5), nextAction: '寄修正版報價' }) }));
  assert.equal(updated.project.nextFollowUpOn, taipeiDate(5));
  assert.equal(updated.item.nextDismissedAt, null);
  assert.equal(updated.item.updatedBy.displayName, '主管');
  assert.equal(updated.item.createdBy.displayName, '業務A');
});

await step('輸入檢查：未來時間、下次聯絡早於發生日、別的客戶的聯絡人、空主旨、錯誤類型皆 400', async () => {
  const bad = async (body, text) => {
    const result = await write(salesA, pA, body);
    assert.equal(result.status, 400, JSON.stringify(result.data));
    assert.equal(result.data.message, text);
  };
  await bad(base({ occurredAt: new Date(Date.now() + 3600_000).toISOString() }), '發生時間不可晚於現在。');
  await bad(base({ occurredAt: hoursAgo(72), nextFollowUpOn: taipeiDate(-5) }), '下次聯絡日不可早於發生日期。');
  await bad(base({ contactId: otherClientContact }), '聯絡人不屬於此專案的客戶。');
  await bad(base({ summary: '  ' }), '請輸入主旨。');
  await bad(base({ type: 'fax' }), '請選擇跟進類型。');
});

await step('專案內列表：依最新一筆排序、關鍵字、類型、分頁與 total', async () => {
  const all = await ok(call('GET', `/api/projects/${pA}/followups`, { token: salesA }));
  assert.equal(all.total, 2);
  assert.deepEqual(all.items.map((value) => value.id), [r1, r0]);
  assert.equal(all.canCreate, true);
  assert.equal(all.project.nextFollowUpOn, taipeiDate(5));
  const byKeyword = await ok(call('GET', `/api/projects/${pA}/followups?keyword=${encodeURIComponent(`補登拜訪 ${suffix}`)}`, { token: salesA }));
  assert.deepEqual(byKeyword.items.map((value) => value.id), [r0]);
  const byType = await ok(call('GET', `/api/projects/${pA}/followups?type=visit`, { token: salesA }));
  assert.equal(byType.total, 1);
  const paged = await ok(call('GET', `/api/projects/${pA}/followups?page=2&pageSize=1`, { token: salesA }));
  assert.equal(paged.total, 2);
  assert.deepEqual(paged.items.map((value) => value.id), [r0]);
  assert.equal((await call('GET', `/api/projects/${pA}/followups?type=fax`, { token: salesA })).status, 400);
});

await step('準備：業務 B 的逾期紀錄、財務部專案的紀錄', async () => {
  const created = await ok(write(salesB, pB, base({ occurredAt: hoursAgo(120), summary: '催回覆', nextFollowUpOn: taipeiDate(-2) })), [201]);
  rB = created.item.id;
  rBAuthor = created.item.createdBy.id;
  rF = (await ok(write(admin, pF, base({ summary: `財務部的紀錄 ${suffix}` })), [201])).item.id;
});

await step('總表：主管看得到部屬（部門樹）的紀錄、看不到財務部；業務只看到自己的；管理員看全部', async () => {
  const managerAll = await ok(call('GET', '/api/followups', { token: manager }));
  assert.equal(managerAll.total, 3);
  assert.ok(managerAll.items.every((value) => value.project.id !== pF));
  assert.equal((await ok(call('GET', '/api/followups', { token: salesA }))).total, 2);
  const adminAll = await ok(call('GET', '/api/followups', { token: admin }));
  assert.equal(adminAll.total, 4);
  assert.equal(adminAll.items[0].clientName, '跟進客戶');
  assert.equal(adminAll.items.find((value) => value.id === r1).project.nextFollowUpOn, taipeiDate(5));
  assert.equal((await call('GET', '/api/followups', { token: accountant })).status, 403);
});

await step('總表：部門（含下層）、撰寫人、關鍵字、日期區間、分頁與排序', async () => {
  const ids = async (query) => (await ok(call('GET', `/api/followups?${query}`, { token: admin }))).items.map((value) => value.id);
  assert.deepEqual(await ids(`departmentId=${financeDept}`), [rF]);
  assert.equal((await ok(call('GET', `/api/followups?departmentId=${salesDept}`, { token: admin }))).total, 3, '部門篩選含下層部門');
  assert.deepEqual(await ids(`createdBy=${rBAuthor}`), [rB]);
  assert.deepEqual(await ids(`keyword=${encodeURIComponent(`FU-B ${suffix}`)}`), [rB], '關鍵字比對專案名稱');
  assert.deepEqual(await ids(`projectId=${pA}`), [r1, r0]);
  assert.deepEqual(await ids(`from=${taipeiDate(-4)}&to=${taipeiDate(-2)}`), [r0]);
  const paged = await ok(call('GET', '/api/followups?page=2&pageSize=3', { token: admin }));
  assert.equal(paged.total, 4);
  assert.deepEqual(paged.items.map((value) => value.id), [rB], '依發生時間新到舊，最舊的在最後一頁');
  assert.equal((await ids('sort=occurredAt&desc=false'))[0], rB);
  assert.equal((await call('GET', `/api/followups?from=${taipeiDate()}&to=${taipeiDate(-1)}`, { token: admin })).status, 400);
});

await step('專案明細與列表：沒有 followups:read 的會計看到的跟進欄位為 null', async () => {
  const detail = await ok(call('GET', `/api/projects/${pA}`, { token: accountant }));
  assert.equal(detail.nextFollowUpOn, null);
  assert.equal(detail.nextAction, null);
  const list = await ok(call('GET', `/api/projects?keyword=${encodeURIComponent(`FU-A ${suffix}`)}`, { token: accountant }));
  assert.equal(list.items[0].nextFollowUpOn, null);
  assert.equal(list.items[0].lastFollowUpAt, null);
  const mine = await ok(call('GET', `/api/projects/${pA}`, { token: salesA }));
  assert.equal(mine.nextFollowUpOn, taipeiDate(5));
  assert.equal(mine.nextAction, '寄修正版報價');
});

const projectIds = async (query, token = admin) => (await ok(call('GET', `/api/projects?keyword=${encodeURIComponent(suffix)}&${query}`, { token }))).items.map((value) => value.id);

await step('專案列表：已逾期、待跟進、沒有下次聯絡的篩選，與下次聯絡排序（未排定排最後）', async () => {
  assert.deepEqual(await projectIds('followUp=overdue'), [pB]);
  assert.deepEqual((await projectIds('followUp=pending')).sort(), [pA, pB].sort());
  assert.deepEqual(await projectIds('followUp=none'), [pF]);
  assert.deepEqual(await projectIds('sort=nextFollowUpOn&desc=false'), [pB, pA, pF]);
  assert.deepEqual(await projectIds('sort=nextFollowUpOn&desc=true'), [pA, pB, pF]);
  assert.equal((await call('GET', '/api/projects?followUp=bogus', { token: admin })).status, 400);
  assert.equal((await projectIds('followUp=overdue', accountant)).length, 3, '沒有 followups:read 時忽略跟進篩選');
  assert.deepEqual(
    await projectIds('sort=nextFollowUpOn&desc=false', accountant),
    await projectIds('sort=createdAt&desc=false', accountant),
    '沒有 followups:read 時忽略下次聯絡排序'
  );
});

await step('提醒範圍：未得標後不算待跟進但日期保留；已結案的專案只能讀', async () => {
  await ok(call('POST', `/api/projects/${pB}/status`, { token: salesB, body: { status: 'quoting' } }));
  await ok(call('POST', `/api/projects/${pB}/status`, { token: salesB, body: { status: 'lost', reason: '價格' } }));
  assert.deepEqual(await projectIds('followUp=overdue'), []);
  assert.deepEqual(await projectIds('followUp=pending'), [pA]);
  assert.equal((await ok(call('GET', `/api/projects/${pB}`, { token: salesB }))).nextFollowUpOn, taipeiDate(-2));
  await ok(call('POST', `/api/projects/${pB}/status`, { token: admin, body: { status: 'closed' } }));
  const closed = await write(admin, pB, base());
  assert.equal(closed.status, 400);
  assert.equal(closed.data.message, '已結案的專案不可修改。');
  assert.equal((await call('PUT', `/api/followups/${rB}`, { token: admin, body: base() })).status, 400);
  const list = await ok(call('GET', `/api/projects/${pB}/followups`, { token: admin }));
  assert.equal(list.canCreate, false);
  assert.equal(list.canUpdate, false);
  assert.equal(list.total, 1);
});

console.log(`\n${passed} 項跟進紀錄驗證通過`);
