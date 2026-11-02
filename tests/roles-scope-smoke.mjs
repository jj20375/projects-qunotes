/**
 * 角色 × 部門的資料範圍矩陣驗證：每個內建角色（業務、業務主管、會計、唯讀、公司管理員）與「本部門」自訂角色，
 * 在一個獨立的測試部門樹裡，各自看得到哪些客戶、專案、報價與訂單，以及不能做哪些寫入；
 * 另外驗證個別資料範圍覆寫與成員換部門會立刻改變可見範圍，以及選專案成員的名單（部門、角色、姓名篩選與分頁）。
 *
 * 測試部門樹（建在示範公司最上層部門底下）：
 *   總部（TOP）── 甲課（A）
 *             └─ 乙課（B）
 *   稽核（AUD，與總部同層）
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動，且已執行 Seeder（SeedDemoData=true）。
 * 用法：node tests/roles-scope-smoke.mjs [baseUrl]
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

const ok = async (promise, expected = [200, 201]) => {
  const result = await promise;
  assert.ok(expected.includes(result.status), JSON.stringify(result.data));
  return result.data;
};

const admin = await login('demo-admin');
const dept = {};
const users = {};
const tokens = {};
const clients = {};
const projects = {};

/** 這次測試建立的客戶中，某人看得到哪些（以短名表示，排序後比較）。 */
async function visibleClients(token) {
  const result = await call('GET', `/api/clients?keyword=${suffix}&pageSize=100`, { token });
  if (result.status === 403) return 'forbidden';
  assert.equal(result.status, 200, JSON.stringify(result.data));
  return result.data.items.map((item) => item.name.split(' ').pop()).sort();
}

async function visibleProjects(token) {
  const result = await call('GET', `/api/projects?keyword=${suffix}&pageSize=100`, { token });
  if (result.status === 403) return 'forbidden';
  assert.equal(result.status, 200, JSON.stringify(result.data));
  return result.data.items.map((item) => item.name.split(' ').pop()).sort();
}

await step('準備：測試部門樹、各角色帳號', async () => {
  const departments = await ok(call('GET', '/api/admin/departments', { token: admin }));
  const root = departments.find((value) => value.parentId === null);
  const create = async (name, parentId) => (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId, name: `${name}-${suffix}` } }))).id;
  dept.TOP = await create('總部', root.id);
  dept.A = await create('甲課', dept.TOP);
  dept.B = await create('乙課', dept.TOP);
  dept.AUD = await create('稽核', root.id);

  const roles = await ok(call('GET', '/api/admin/roles', { token: admin }));
  const roleId = (code) => roles.find((value) => value.code === code).id;
  // 「本部門」範圍的自訂角色：可以讀寫客戶與專案
  const rw = (code) => ({ code, perRead: true, perCreate: true, perUpdate: true, perDelete: false, options: [] });
  const deptRole = await ok(call('POST', '/api/admin/roles', { token: admin, body: { code: `dept-${suffix}`, name: '本部門業務', dataScope: 'department', permissions: [rw('clients'), rw('projects'), rw('quotes')] } }));
  const people = [
    ['salesA1', 'sales', 'A'],
    ['salesA2', 'sales', 'A'],
    ['salesB', 'sales', 'B'],
    ['manager', 'sales-manager', 'TOP'],
    ['accountant', 'accountant', 'AUD'],
    ['viewer', 'viewer', 'A'],
    ['deptRole', null, 'A'],
  ];
  for (const [key, code, department] of people) {
    const created = await ok(call('POST', '/api/admin/users', {
      token: admin,
      body: { account: `rs-${key}-${suffix}`, displayName: `${key}-${suffix}`, password: seedPassword, departmentId: dept[department], roleId: code ? roleId(code) : deptRole.id },
    }));
    users[key] = created;
    tokens[key] = await login(`rs-${key}-${suffix}`);
  }
  users.deptRoleId = deptRole.id;
});

await step('準備：客戶（各部門）與專案（甲課兩個、乙課一個），唯讀帳號加入甲課專案 1', async () => {
  const client = async (key, token, departmentId) => {
    const body = { name: `${suffix} ${key}`, ...(departmentId ? { departmentId } : {}) };
    clients[key] = (await ok(call('POST', '/api/clients', { token, body }))).id;
  };
  await client('客A', tokens.salesA1); // 業務建立：自動歸甲課
  await client('客B', tokens.salesB); // 自動歸乙課
  await client('客總', admin, dept.TOP);
  await client('客稽', admin, dept.AUD);

  projects.A1 = (await ok(call('POST', '/api/projects', { token: tokens.salesA1, body: { name: `${suffix} 案A1`, clientId: clients['客A'] } }))).id;
  projects.A2 = (await ok(call('POST', '/api/projects', { token: tokens.salesA2, body: { name: `${suffix} 案A2`, clientId: clients['客A'] } }))).id;
  projects.B1 = (await ok(call('POST', '/api/projects', { token: tokens.salesB, body: { name: `${suffix} 案B1`, clientId: clients['客B'] } }))).id;
  await ok(call('PUT', `/api/projects/${projects.A1}/members`, { token: tokens.salesA1, body: [{ userId: users.viewer.userId, projectRole: 'viewer' }] }));
  await ok(call('POST', `/api/projects/${projects.A1}/quotes`, { token: tokens.salesA1, body: { title: '甲課報價', items: [{ name: '服務', quantity: 1, unitPrice: 1000 }] } }));
});

await step('客戶可見範圍：本人與本部門看自己部門、主管含下層、會計與管理員看全部', async () => {
  const expected = {
    salesA1: ['客A'], // 業務（本人）：自己部門（甲課）
    salesA2: ['客A'], // 同部門同事建立的也看得到
    salesB: ['客B'],
    viewer: ['客A'], // 唯讀（本人）：自己部門
    deptRole: ['客A'], // 本部門
    manager: ['客A', '客B', '客總'].sort(), // 業務主管（總部，部門樹）：總部、甲課、乙課；看不到稽核
    accountant: ['客A', '客B', '客總', '客稽'].sort(), // 會計（全公司）
  };
  for (const [key, names] of Object.entries(expected)) assert.deepEqual(await visibleClients(tokens[key]), names, `${key} 看到的客戶`);
  assert.deepEqual(await visibleClients(admin), ['客A', '客B', '客總', '客稽'].sort(), '公司管理員看全部');
  // 看不到的單筆一律 404
  assert.equal((await call('GET', `/api/clients/${clients['客B']}`, { token: tokens.salesA1 })).status, 404);
  assert.equal((await call('GET', `/api/clients/${clients['客總']}`, { token: tokens.salesA1 })).status, 404, '下層看不到上層部門的客戶');
  assert.equal((await call('GET', `/api/clients/${clients['客稽']}`, { token: tokens.manager })).status, 404, '主管看不到其他分支');
});

await step('專案可見範圍：本人只看自己負責或參與、本部門看部門、主管含下層、會計與管理員看全部', async () => {
  const expected = {
    salesA1: ['案A1'], // 業務（本人）：自己負責的
    salesA2: ['案A2'], // 同部門同事的專案看不到
    salesB: ['案B1'],
    viewer: ['案A1'], // 唯讀：只有被加為成員的
    deptRole: ['案A1', '案A2'], // 本部門：甲課全部
    manager: ['案A1', '案A2', '案B1'], // 部門樹
    accountant: ['案A1', '案A2', '案B1'], // 全公司
  };
  for (const [key, names] of Object.entries(expected)) assert.deepEqual(await visibleProjects(tokens[key]), names, `${key} 看到的專案`);
  assert.equal((await call('GET', `/api/projects/${projects.A1}`, { token: tokens.salesA2 })).status, 404);
  assert.equal((await call('GET', `/api/projects/${projects.B1}`, { token: tokens.deptRole })).status, 404);
});

await step('報價與訂單繼承專案的可見範圍', async () => {
  const quotes = async (token) => {
    const result = await call('GET', `/api/projects/${projects.A1}/quotes`, { token });
    return result.status === 200 ? result.data.total : result.status;
  };
  assert.equal(await quotes(tokens.salesA1), 1);
  assert.equal(await quotes(tokens.viewer), 1, '唯讀成員看得到所屬專案的報價');
  assert.equal(await quotes(tokens.manager), 1);
  assert.equal(await quotes(tokens.accountant), 1);
  assert.equal(await quotes(tokens.salesA2), 404, '同部門業務看不到別人專案的報價');
  assert.equal(await quotes(tokens.salesB), 404);
  // 報價總表也一樣
  const search = async (token) => (await call('GET', `/api/quotes?keyword=${encodeURIComponent('甲課報價')}&latestOnly=false&pageSize=100`, { token })).data.items.filter((row) => row.projectId === projects.A1).length;
  assert.equal(await search(tokens.salesA2), 0);
  assert.equal(await search(tokens.manager), 1);
  assert.equal((await call('GET', `/api/projects/${projects.A1}/orders`, { token: tokens.salesB })).status, 404, '訂單也繼承專案範圍');
  assert.equal((await call('GET', `/api/projects/${projects.A1}/orders`, { token: tokens.accountant })).status, 200);
});

await step('寫入權限：唯讀不能新增任何東西、會計不能改客戶與專案、看不到的就改不到', async () => {
  // 唯讀
  assert.equal((await call('POST', '/api/clients', { token: tokens.viewer, body: { name: 'x' } })).status, 403);
  assert.equal((await call('POST', '/api/projects', { token: tokens.viewer, body: { name: 'x', clientId: clients['客A'] } })).status, 403);
  assert.equal((await call('POST', `/api/projects/${projects.A1}/quotes`, { token: tokens.viewer, body: { title: 'x', items: [] } })).status, 403);
  assert.equal((await call('PUT', `/api/clients/${clients['客A']}`, { token: tokens.viewer, body: { name: 'x' } })).status, 403);
  // 會計：客戶、專案唯讀
  assert.equal((await call('POST', '/api/clients', { token: tokens.accountant, body: { name: 'x' } })).status, 403);
  assert.equal((await call('PUT', `/api/clients/${clients['客A']}`, { token: tokens.accountant, body: { name: 'x' } })).status, 403);
  assert.equal((await call('POST', '/api/projects', { token: tokens.accountant, body: { name: 'x', clientId: clients['客A'] } })).status, 403);
  // 業務：同部門同事的客戶看得到但不能改（修改權看負責人）；別部門的看不到也改不到
  const peer = await call('GET', `/api/clients/${clients['客A']}`, { token: tokens.salesA2 });
  assert.equal(peer.data.canEdit, false);
  assert.equal((await call('PUT', `/api/clients/${clients['客A']}`, { token: tokens.salesA2, body: { name: 'x' } })).status, 403);
  assert.equal((await call('PUT', `/api/clients/${clients['客B']}`, { token: tokens.salesA1, body: { name: 'x' } })).status, 404);
  // 業務不能用別部門的客戶開專案
  assert.equal((await call('POST', '/api/projects', { token: tokens.salesA1, body: { name: 'x', clientId: clients['客B'] } })).status, 400);
  // 主管可以改下層業務負責的客戶
  assert.equal((await call('GET', `/api/clients/${clients['客A']}`, { token: tokens.manager })).data.canEdit, true);
  // 只有全公司範圍能移動客戶所屬部門：主管（部門樹）也不行
  assert.equal((await call('PUT', `/api/clients/${clients['客A']}`, { token: tokens.manager, body: { name: `${suffix} 客A`, departmentId: dept.B } })).status, 403);
});

await step('個別資料範圍覆寫：業務改成「本部門」看到同事的專案；清除後恢復', async () => {
  await ok(call('PATCH', `/api/admin/users/${users.salesA2.membershipId}`, { token: admin, body: { dataScopeOverride: 'department' } }));
  assert.equal((await call('GET', '/api/auth/me', { token: tokens.salesA2 })).status, 401, '資料範圍異動後舊 token 失效');
  tokens.salesA2 = await login(`rs-salesA2-${suffix}`);
  assert.deepEqual(await visibleProjects(tokens.salesA2), ['案A1', '案A2']);
  assert.deepEqual(await visibleClients(tokens.salesA2), ['客A'], '客戶範圍一樣是自己部門');
  await ok(call('PATCH', `/api/admin/users/${users.salesA2.membershipId}`, { token: admin, body: { clearDataScopeOverride: true } }));
  tokens.salesA2 = await login(`rs-salesA2-${suffix}`);
  assert.deepEqual(await visibleProjects(tokens.salesA2), ['案A2']);
});

await step('成員換部門：業務從甲課調到乙課，客戶改看乙課的；自己負責的專案仍看得到', async () => {
  await ok(call('PATCH', `/api/admin/users/${users.salesA2.membershipId}`, { token: admin, body: { departmentId: dept.B } }));
  tokens.salesA2 = await login(`rs-salesA2-${suffix}`);
  assert.deepEqual(await visibleClients(tokens.salesA2), ['客B']);
  assert.deepEqual(await visibleProjects(tokens.salesA2), ['案A2'], '本人範圍看負責的專案，不受部門影響');
  // 唯讀帳號移出專案成員後就看不到
  await ok(call('PUT', `/api/projects/${projects.A1}/members`, { token: tokens.salesA1, body: [] }));
  assert.deepEqual(await visibleProjects(tokens.viewer), []);
});

await step('專案成員名單：依部門（含下層）、角色、姓名篩選，後端分頁；跨部門（會計）也能加入專案', async () => {
  const list = async (token, query) => {
    const params = new URLSearchParams({ keyword: `-${suffix}`, ...query });
    const result = await call('GET', `/api/directory/members?${params}`, { token });
    assert.equal(result.status, 200, JSON.stringify(result.data));
    return result.data;
  };
  const names = (data) => data.items.map((item) => item.displayName.replace(`-${suffix}`, '')).sort();
  // 業務 A2 已調到乙課，仍在總部底下
  assert.deepEqual(names(await list(tokens.salesA1, { departmentId: dept.TOP, pageSize: '50' })), ['deptRole', 'manager', 'salesA1', 'salesA2', 'salesB', 'viewer']);
  assert.deepEqual(names(await list(tokens.salesA1, { departmentId: dept.TOP, includeSubDepartments: 'false' })), ['manager'], '不含下層時只有總部本身');
  const roles = await ok(call('GET', '/api/directory/roles', { token: tokens.salesA1 }));
  assert.ok(roles.some((role) => role.name === '會計'), '任何成員都能讀角色名稱供篩選');
  const accountantRole = roles.find((role) => role.name === '會計').id;
  const accountants = await list(tokens.salesA1, { roleId: accountantRole });
  assert.deepEqual(names(accountants), ['accountant']);
  assert.equal(accountants.items[0].roleName, '會計');
  // 分頁與 total
  const first = await list(tokens.salesA1, { pageSize: '4', page: '1' });
  const second = await list(tokens.salesA1, { pageSize: '4', page: '2' });
  assert.equal(first.total, 7);
  assert.equal(first.items.length, 4);
  assert.equal(second.items.length, 3);
  assert.equal(new Set([...first.items, ...second.items].map((item) => item.userId)).size, 7, '兩頁不重複');
  // 自己部門（甲課）的人排最前面
  assert.ok(first.items.slice(0, 3).every((item) => item.departmentName === `甲課-${suffix}`), JSON.stringify(first.items.map((item) => item.departmentName)));
  // 排除已在專案裡的人
  const excluded = await list(tokens.salesA1, { excludeIds: users.manager.userId, pageSize: '50' });
  assert.ok(!excluded.items.some((item) => item.userId === users.manager.userId));
  // 會計（稽核部）可以被加為專案成員
  await ok(call('PUT', `/api/projects/${projects.A1}/members`, { token: tokens.salesA1, body: [{ userId: users.accountant.userId, projectRole: 'member' }] }));
  const detail = await ok(call('GET', `/api/projects/${projects.A1}`, { token: tokens.salesA1 }));
  assert.equal(detail.members[0].departmentName, `稽核-${suffix}`, '成員清單帶出部門');
});

await step('清理：停用測試帳號與測試部門、刪除自訂角色（客戶與專案保留，名稱含執行代號）', async () => {
  const roles = await ok(call('GET', '/api/admin/roles', { token: admin }));
  const viewer = roles.find((value) => value.code === 'viewer').id;
  for (const user of Object.values(users)) {
    if (user?.membershipId) await ok(call('PATCH', `/api/admin/users/${user.membershipId}`, { token: admin, body: { isActive: false, roleId: viewer } }));
  }
  assert.equal((await call('DELETE', `/api/admin/roles/${users.deptRoleId}`, { token: admin })).status, 204);
  // 測試部門停用後就不會出現在各處的部門選單
  for (const id of [dept.A, dept.B, dept.TOP, dept.AUD]) await ok(call('PATCH', `/api/admin/departments/${id}`, { token: admin, body: { isActive: false } }));
});

console.log(`\n全部 ${passed} 項通過`);
