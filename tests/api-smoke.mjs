/**
 * 第一階段 API 端對端驗證：登入／refresh 輪替、平台切換公司、公司隔離、
 * 角色與個人權限計算、防越權規則、AuthVersion 失效與部門移動。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動，且已執行 Seeder（SeedDemoData=true）。
 * 用法：node tests/api-smoke.mjs [baseUrl]
 * 密碼讀自 .env.development-api.local 的 Quote__BootstrapAdminPassword，不會輸出到畫面。
 */
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
const envText = await readFile(new URL('../.env.development-api.local', import.meta.url), 'utf8');
const seedPassword = envText.match(/^Quote__BootstrapAdminPassword=(.+)$/m)?.[1]?.trim();
assert.ok(seedPassword, '找不到 Quote__BootstrapAdminPassword');

const suffix = Date.now().toString(36);
let passed = 0;

async function call(method, path, { token, body, cookie } = {}) {
  const headers = { 'content-type': 'application/json' };
  if (token) headers.authorization = `Bearer ${token}`;
  if (cookie) headers.cookie = cookie;
  const response = await fetch(`${baseUrl}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  const setCookie = response.headers.get('set-cookie');
  return { status: response.status, data: text ? JSON.parse(text) : null, cookie: setCookie?.split(';')[0] };
}

async function login(account, password = seedPassword) {
  const result = await call('POST', '/api/auth/login', { body: { account, password } });
  assert.equal(result.status, 200, `${account} 登入失敗：${JSON.stringify(result.data)}`);
  return { token: result.data.accessToken, cookie: result.cookie };
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

const hasGrant = (me, code, action) => me.grants.some((grant) => grant.code === code && grant.actions.includes(action));
const hasOption = (me, code, option) => me.grants.some((grant) => grant.code === code && grant.options.includes(option));

let admin;
let demoAdmin;
let newCompanyId;

await step('健康檢查', async () => {
  assert.equal((await call('GET', '/api/health')).status, 200);
});

await step('登入失敗不洩漏帳號是否存在', async () => {
  const wrong = await call('POST', '/api/auth/login', { body: { account: 'admin', password: 'wrong-password' } });
  const missing = await call('POST', '/api/auth/login', { body: { account: `nobody-${suffix}`, password: 'wrong-password' } });
  assert.equal(wrong.status, 401);
  assert.deepEqual(wrong.data, missing.data);
});

await step('admin 登入為 platform scope，選單只有平台功能', async () => {
  admin = await login('admin');
  const me = (await call('GET', '/api/auth/me', { token: admin.token })).data;
  assert.equal(me.scope.kind, 'platform');
  assert.ok(hasOption(me, 'platform.companies', 'switch_company'));
  const menu = (await call('GET', '/api/auth/menu', { token: admin.token })).data;
  assert.deepEqual(menu.map((node) => node.code), ['platform.system_manager']);
});

await step('既有業務角色預設取得虛擬辦公室 office:read', async () => {
  const sales = await login('demo-sales');
  const me = (await call('GET', '/api/auth/me', { token: sales.token })).data;
  assert.ok(hasGrant(me, 'office', 'read'));
});

await step('平台開通新公司，並拒絕重複代碼', async () => {
  const body = { name: `測試公司 ${suffix}`, slug: `t-${suffix}`, adminAccount: `t-admin-${suffix}`, adminDisplayName: '測試管理員', adminPassword: seedPassword };
  const created = await call('POST', '/api/platform/companies', { token: admin.token, body });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  newCompanyId = created.data.id;
  assert.equal((await call('POST', '/api/platform/companies', { token: admin.token, body })).status, 409);
});

await step('公司選單分頁與搜尋由後端處理：offset／limit 不重複、關鍵字過濾、可翻到示範公司', async () => {
  const get = async (query) => (await call('GET', `/api/auth/scopes?${query}`, { token: admin.token })).data.filter((scope) => scope.scopeKind === 'company');
  // 翻頁驗證需要至少 10 家公司；資料庫剛用 demo:reset 清乾淨時不夠，就自己補足，不能依賴先前測試留下的資料。
  const existing = (await get('limit=50&offset=0')).length;
  for (let index = existing; index < 10; index += 1) {
    const slug = `pg-${suffix}-${index}`;
    await call('POST', '/api/platform/companies', { token: admin.token, body: { name: `Paging ${suffix} ${index}`, slug, adminAccount: slug, adminDisplayName: '分頁測試', adminPassword: seedPassword } });
  }
  const first = await get('limit=5&offset=0');
  const second = await get('limit=5&offset=5');
  assert.equal(first.length, 5);
  assert.equal(second.length, 5);
  assert.equal(new Set([...first, ...second].map((scope) => scope.companyId)).size, 10, '兩頁不重複');
  // 示範公司名稱以中文開頭，排在英文開頭的測試公司之後；一頁一頁往後翻要能翻到。
  let found = false;
  for (let offset = 0; offset < 500 && !found; offset += 50) {
    const page = await get(`limit=50&offset=${offset}`);
    if (!page.length) break;
    found = page.some((scope) => scope.name.startsWith('示範工程'));
  }
  assert.ok(found, '往後翻頁可以找到示範公司');
  assert.ok((await get('keyword=%E7%A4%BA%E7%AF%84&limit=50')).every((scope) => scope.name.includes('示範')), '關鍵字由後端過濾');
});

await step('平台帳號不能直接讀公司資料，切換公司後才可以（impersonation）', async () => {
  assert.equal((await call('GET', '/api/admin/users', { token: admin.token })).status, 403);
  // 平台管理員可進入的公司很多，工作範圍列表只回傳前 20 間，要以名稱搜尋（由後端過濾）。
  const scopes = (await call('GET', `/api/auth/scopes?keyword=${encodeURIComponent('示範工程')}`, { token: admin.token })).data;
  const demo = scopes.find((scope) => scope.name.startsWith('示範工程'));
  assert.ok(demo?.isImpersonation);
  const switched = await call('POST', '/api/auth/scopes/select', { token: admin.token, cookie: admin.cookie, body: { scopeKind: 'company', companyId: demo.companyId } });
  assert.equal(switched.status, 200);
  const me = (await call('GET', '/api/auth/me', { token: switched.data.accessToken })).data;
  assert.equal(me.scope.isImpersonating, true);
  assert.ok((await call('GET', '/api/admin/users', { token: switched.data.accessToken })).data.total >= 3);
});

await step('refresh token 輪替；舊 token 重用會撤銷整組', async () => {
  const session = await login('demo-sales');
  const first = await call('POST', '/api/auth/refresh', { cookie: session.cookie });
  assert.equal(first.status, 200);
  const reuse = await call('POST', '/api/auth/refresh', { cookie: session.cookie });
  assert.equal(reuse.status, 401);
  assert.equal((await call('POST', '/api/auth/refresh', { cookie: first.cookie })).status, 401, '重用偵測後新 token 也必須失效');
});

await step('公司隔離：新公司管理員只看得到自己公司的成員與部門', async () => {
  const testAdmin = await login(`t-admin-${suffix}`);
  const users = (await call('GET', '/api/admin/users', { token: testAdmin.token })).data;
  assert.deepEqual(users.items.map((user) => user.account), [`t-admin-${suffix}`]);
  assert.equal(users.total, 1);
  const departments = (await call('GET', '/api/admin/departments', { token: testAdmin.token })).data;
  assert.equal(departments.length, 1);
});

await step('業務（own）沒有帳號管理權限', async () => {
  const sales = await login('demo-sales');
  assert.equal((await call('GET', '/api/admin/users', { token: sales.token })).status, 403);
  const me = (await call('GET', '/api/auth/me', { token: sales.token })).data;
  assert.equal(me.scope.dataScope, 'own');
  assert.equal(me.scope.departmentName, '業務一課');
});

await step('業務主管只能讀成員，不能建立帳號或指派角色', async () => {
  const manager = await login('demo-manager');
  assert.equal((await call('GET', '/api/admin/users', { token: manager.token })).status, 200);
  const create = await call('POST', '/api/admin/users', { token: manager.token, body: { account: `x-${suffix}`, displayName: 'x', password: seedPassword } });
  assert.equal(create.status, 403);
});

let memberId;
let memberAccount;
await step('公司管理員建立業務帳號', async () => {
  demoAdmin = await login('demo-admin');
  const roles = (await call('GET', '/api/admin/roles', { token: demoAdmin.token })).data;
  const departments = (await call('GET', '/api/admin/departments', { token: demoAdmin.token })).data;
  memberAccount = `sales-${suffix}`;
  const created = await call('POST', '/api/admin/users', {
    token: demoAdmin.token,
    body: { account: memberAccount, displayName: '測試業務', password: seedPassword, departmentId: departments.find((d) => d.code === 'SALES-1').id, roleId: roles.find((r) => r.code === 'sales').id },
  });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  memberId = created.data.membershipId;
});

await step('列表搜尋、分頁由後端處理：成員、公司、系統帳號、通訊錄、工作範圍', async () => {
  const get = async (path, token) => {
    const result = await call('GET', path, { token });
    assert.equal(result.status, 200, `${path}：${JSON.stringify(result.data)}`);
    return result.data;
  };
  // 成員：關鍵字（姓名、帳號、Email）與分頁在資料庫處理，total 是符合條件的總數。
  const all = await get('/api/admin/users?pageSize=200', demoAdmin.token);
  // 每頁上限 200：停用的測試帳號會一直留在列表，成員數可能超過一頁，所以只檢查「這一頁最多 200 筆、total 是整體筆數」。
  assert.ok(all.total >= 3 && all.items.length === Math.min(all.total, 200));
  const one = await get('/api/admin/users?keyword=demo-manager', demoAdmin.token);
  assert.deepEqual([one.total, one.items.map((user) => user.account)], [1, ['demo-manager']]);
  assert.equal((await get('/api/admin/users?keyword=不存在的人', demoAdmin.token)).total, 0);
  const page1 = await get('/api/admin/users?pageSize=2&page=1', demoAdmin.token);
  const page2 = await get('/api/admin/users?pageSize=2&page=2', demoAdmin.token);
  assert.equal(page1.items.length, 2);
  assert.equal(page1.total, all.total, 'total 不受分頁影響');
  assert.ok(page2.items.every((user) => !page1.items.some((value) => value.membershipId === user.membershipId)), '第二頁與第一頁不重複');
  // 成員列表的「個人權限數」只統計目前頁面的人，仍然正確。
  assert.ok(page1.items.every((user) => typeof user.personalPermissionCount === 'number'));

  // 平台：公司與系統帳號
  const companies = await get('/api/platform/companies?pageSize=200', admin.token);
  assert.ok(companies.total >= 2);
  const demo = await get('/api/platform/companies?keyword=demo', admin.token);
  assert.ok(demo.total >= 1 && demo.items.every((company) => `${company.name}${company.slug}`.toLowerCase().includes('demo')));
  assert.equal((await get('/api/platform/companies?keyword=demo&pageSize=1', admin.token)).items.length, 1);
  const systemUsers = await get('/api/platform/system-users?keyword=admin', admin.token);
  assert.ok(systemUsers.items.some((user) => user.account === 'admin'));
  assert.equal((await get('/api/platform/system-users?keyword=不存在的帳號', admin.token)).total, 0);

  // 通訊錄（人員下拉）：keyword 比對姓名與部門，ids 取回指定人員，excludeIds 排除，limit 限制筆數。
  const directory = await get('/api/directory/users', demoAdmin.token);
  assert.ok(directory.length >= 3 && directory.length <= 20);
  const manager = (await get('/api/directory/users?keyword=王業務', demoAdmin.token))[0];
  assert.equal(manager.displayName, '王業務主管');
  assert.deepEqual((await get(`/api/directory/users?ids=${manager.userId}`, demoAdmin.token)).map((user) => user.userId), [manager.userId]);
  assert.ok(!(await get(`/api/directory/users?excludeIds=${manager.userId}`, demoAdmin.token)).some((user) => user.userId === manager.userId));
  assert.equal((await get('/api/directory/users?limit=1', demoAdmin.token)).length, 1);
  assert.equal((await get('/api/directory/users?keyword=不存在的人', demoAdmin.token)).length, 0);

  // 工作範圍切換：公司名稱搜尋由後端處理，目前所在的公司一定在結果中。
  const scopes = await get('/api/auth/scopes?keyword=示範工程', admin.token);
  assert.ok(scopes.some((scope) => scope.name.startsWith('示範工程')) && scopes.every((scope) => scope.scopeKind === 'platform' || scope.name.includes('示範工程')));
  assert.ok((await get('/api/auth/scopes?limit=1', admin.token)).length <= 2, '平台項目加上最多 limit 間公司');
});

await step('個人權限：加權 quotes:approve、禁止 clients:update，並反映在有效權限', async () => {
  const before = await login(memberAccount);
  const replaced = await call('PUT', `/api/admin/users/${memberId}/permissions`, {
    token: demoAdmin.token,
    body: {
      items: [
        { code: 'quotes', effect: 'grant', perCreate: false, perRead: false, perUpdate: false, perDelete: false, options: ['approve'], reason: '代理主管核准' },
        { code: 'clients', effect: 'deny', perCreate: false, perRead: false, perUpdate: true, perDelete: false, options: [] },
      ],
    },
  });
  assert.equal(replaced.status, 200, JSON.stringify(replaced.data));
  assert.equal((await call('GET', '/api/auth/me', { token: before.token })).status, 401, '個人權限異動後舊 token 必須失效');
  const me = (await call('GET', '/api/auth/me', { token: (await login(memberAccount)).token })).data;
  assert.ok(hasOption(me, 'quotes', 'approve'));
  assert.ok(hasGrant(me, 'clients', 'read'));
  assert.ok(!hasGrant(me, 'clients', 'update'));
  const preview = (await call('GET', `/api/admin/users/${memberId}/permissions`, { token: demoAdmin.token })).data;
  assert.equal(preview.overrides.length, 2);
});

await step('個人權限不可超出目錄或上限', async () => {
  const bad = await call('PUT', `/api/admin/users/${memberId}/permissions`, {
    token: demoAdmin.token,
    body: { items: [{ code: 'platform.companies', effect: 'grant', perRead: true, perCreate: false, perUpdate: false, perDelete: false, options: [] }] },
  });
  assert.equal(bad.status, 400);
});

await step('不可修改自己的角色、停用主管理員', async () => {
  const users = (await call('GET', '/api/admin/users?keyword=demo-admin', { token: demoAdmin.token })).data;
  const self = users.items.find((user) => user.isSelf);
  assert.equal((await call('PATCH', `/api/admin/users/${self.membershipId}`, { token: demoAdmin.token, body: { isActive: false } })).status, 400);
});

await step('自訂角色不可超過 company-admin；角色權限異動讓成員舊 token 失效', async () => {
  const overCeiling = await call('POST', '/api/admin/roles', {
    token: demoAdmin.token,
    body: { code: `bad-${suffix}`, name: '越權', dataScope: 'own', permissions: [{ code: 'platform.roles', perRead: true, perCreate: false, perUpdate: false, perDelete: false, options: [] }] },
  });
  assert.equal(overCeiling.status, 400);
  const created = await call('POST', '/api/admin/roles', {
    token: demoAdmin.token,
    body: { code: `custom-${suffix}`, name: '自訂業務', dataScope: 'department', permissions: [{ code: 'projects', perRead: true, perCreate: true, perUpdate: false, perDelete: false, options: [] }] },
  });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  const assigned = await call('PATCH', `/api/admin/users/${memberId}`, { token: demoAdmin.token, body: { roleId: created.data.id } });
  assert.equal(assigned.status, 200);
  const session = await login(memberAccount);
  const update = await call('PUT', `/api/admin/roles/${created.data.id}/permissions`, {
    token: demoAdmin.token,
    body: { permissions: [{ code: 'projects', perRead: true, perCreate: false, perUpdate: false, perDelete: false, options: [] }] },
  });
  assert.equal(update.status, 200);
  assert.equal((await call('GET', '/api/auth/me', { token: session.token })).status, 401);
  const me = (await call('GET', '/api/auth/me', { token: (await login(memberAccount)).token })).data;
  assert.equal(me.scope.dataScope, 'department');
  assert.ok(hasGrant(me, 'projects', 'read') && !hasGrant(me, 'projects', 'create'));
});

await step('自訂角色只有建立者能選用；測完清掉自己建立的角色', async () => {
  const roles = (await call('GET', '/api/admin/roles', { token: demoAdmin.token })).data;
  const custom = roles.find((role) => role.code === `custom-${suffix}`);
  const departments = (await call('GET', '/api/admin/departments', { token: demoAdmin.token })).data;
  const admin2Account = `admin2-${suffix}`;
  const admin2 = await call('POST', '/api/admin/users', {
    token: demoAdmin.token,
    body: { account: admin2Account, displayName: '第二位管理員', password: seedPassword, departmentId: departments.find((d) => d.code === 'SALES').id, roleId: roles.find((r) => r.code === 'company-admin').id },
  });
  assert.equal(admin2.status, 201, JSON.stringify(admin2.data));
  const admin2Token = (await login(admin2Account)).token;

  // 成員表單的角色選單（forAssignment）：建立者看得到自訂角色，其他人看不到；角色管理頁仍列出全部
  const assignable = async (token) => (await call('GET', '/api/admin/roles?forAssignment=true', { token })).data.map((role) => role.code);
  assert.ok((await assignable(demoAdmin.token)).includes(`custom-${suffix}`), '建立者看得到');
  assert.ok(!(await assignable(admin2Token)).includes(`custom-${suffix}`), '其他管理員不列出別人的自訂角色');
  assert.ok((await assignable(admin2Token)).includes('sales'), '系統角色照常列出');
  assert.ok((await call('GET', '/api/admin/roles', { token: admin2Token })).data.some((role) => role.code === `custom-${suffix}`), '角色管理頁仍列出全部');

  // 後端也擋：其他人不能指派別人建立的自訂角色；成員目前就是這個角色時，改其他欄位不受影響
  const salesRoleId = roles.find((r) => r.code === 'sales').id;
  assert.equal((await call('PATCH', `/api/admin/users/${memberId}`, { token: admin2Token, body: { displayName: '改名不受影響' } })).status, 200, '成員已是這個自訂角色時，其他人仍能改其他欄位');
  assert.equal((await call('PATCH', `/api/admin/users/${memberId}`, { token: demoAdmin.token, body: { roleId: salesRoleId } })).status, 200);
  const blocked = await call('PATCH', `/api/admin/users/${memberId}`, { token: admin2Token, body: { roleId: custom.id } });
  assert.equal(blocked.status, 400, JSON.stringify(blocked.data));
  assert.equal((await call('POST', '/api/admin/users', { token: admin2Token, body: { account: `blocked-${suffix}`, displayName: 'x', password: seedPassword, departmentId: departments.find((d) => d.code === 'SALES').id, roleId: custom.id } })).status, 400);

  // 清理：刪除測試用的自訂角色（成員前面已改回業務）、停用第二位管理員
  assert.equal((await call('DELETE', `/api/admin/roles/${custom.id}`, { token: demoAdmin.token })).status, 204);
  const members = (await call('GET', `/api/admin/users?keyword=${admin2Account}`, { token: demoAdmin.token })).data.items;
  assert.equal((await call('PATCH', `/api/admin/users/${members[0].membershipId}`, { token: demoAdmin.token, body: { isActive: false } })).status, 200);
});

await step('資料範圍上限：建角色、指派角色、個別資料範圍都不能大於自己的', async () => {
  // 部門範圍、可管角色與成員的管理者：不能靠建立全公司範圍的角色再指派給自己來提升可見範圍
  const roles = (await call('GET', '/api/admin/roles', { token: demoAdmin.token })).data;
  const departments = (await call('GET', '/api/admin/departments', { token: demoAdmin.token })).data;
  const full = (code, options = []) => ({ code, perRead: true, perCreate: true, perUpdate: true, perDelete: true, options });
  const manager = await call('POST', '/api/admin/roles', {
    token: demoAdmin.token,
    body: { code: `scoped-${suffix}`, name: '部門角色管理者', dataScope: 'department', permissions: [
      full('company.roles'),
      { ...full('company.users'), perDelete: false, options: ['assign_role'] },
      // 讀取權限涵蓋「唯讀」角色，才能把成員指派成唯讀；這樣被擋下的原因只會是資料範圍。
      ...['dashboard', 'office', 'clients', 'products', 'projects', 'followups', 'quotes', 'orders', 'warranties', 'invoices'].map((code) => ({ code, perRead: true, perCreate: false, perUpdate: false, perDelete: false, options: [] })),
    ] },
  });
  assert.equal(manager.status, 201, JSON.stringify(manager.data));
  const account = `scoped-${suffix}`;
  const member = await call('POST', '/api/admin/users', {
    token: demoAdmin.token,
    body: { account, displayName: '部門角色管理者', password: seedPassword, departmentId: departments.find((d) => d.code === 'SALES-1').id, roleId: manager.data.id },
  });
  assert.equal(member.status, 201, JSON.stringify(member.data));
  const token = (await login(account)).token;
  const role = (code, dataScope) => call('POST', '/api/admin/roles', { token, body: { code, name: code, dataScope, permissions: [] } });

  const tooWide = await role(`wide-${suffix}`, 'company');
  assert.equal(tooWide.status, 400, '不能建立全公司範圍的角色');
  assert.match(tooWide.data.message, /資料範圍不可大於自己/);
  assert.equal((await role(`tree-${suffix}`, 'department_tree')).status, 400, '不能建立含下層部門的角色');
  const same = await role(`dept-${suffix}`, 'department');
  assert.equal(same.status, 201, JSON.stringify(same.data));
  const narrower = await role(`own-${suffix}`, 'own');
  assert.equal(narrower.status, 201, '同範圍與更小的範圍可以');
  assert.equal((await call('PATCH', `/api/admin/roles/${same.data.id}`, { token, body: { dataScope: 'company' } })).status, 400, '不能把自己的角色改成更大的範圍');
  assert.equal((await call('PATCH', `/api/admin/roles/${same.data.id}`, { token, body: { dataScope: 'own' } })).status, 200, '改成更小的可以');

  // 別人建立、範圍比自己大的角色：不能改、不能刪、不能指派
  const wideByAdmin = await call('POST', '/api/admin/roles', { token: demoAdmin.token, body: { code: `wide-admin-${suffix}`, name: '全公司唯讀', dataScope: 'company', permissions: [{ ...full('dashboard'), perCreate: false, perUpdate: false, perDelete: false }] } });
  assert.equal(wideByAdmin.status, 201);
  assert.equal((await call('PATCH', `/api/admin/roles/${wideByAdmin.data.id}`, { token, body: { name: '偷改' } })).status, 400);
  assert.equal((await call('DELETE', `/api/admin/roles/${wideByAdmin.data.id}`, { token })).status, 400);
  const target = await call('POST', '/api/admin/users', { token, body: { account: `scoped-t-${suffix}`, displayName: '被指派者', password: seedPassword, departmentId: departments.find((d) => d.code === 'SALES-1').id, roleId: roles.find((r) => r.code === 'viewer').id } });
  assert.equal(target.status, 201, JSON.stringify(target.data));
  const assignWide = await call('PATCH', `/api/admin/users/${target.data.membershipId}`, { token, body: { roleId: wideByAdmin.data.id } });
  assert.equal(assignWide.status, 400, '不能指派範圍更大的角色');
  assert.match(assignWide.data.message, /資料範圍.*不能指派/);
  const overrideWide = await call('PATCH', `/api/admin/users/${target.data.membershipId}`, { token, body: { dataScopeOverride: 'company' } });
  assert.equal(overrideWide.status, 400, '不能設定更大的個別資料範圍');
  assert.match(overrideWide.data.message, /個人資料範圍不可大於自己/);
  assert.equal((await call('PATCH', `/api/admin/users/${target.data.membershipId}`, { token, body: { dataScopeOverride: 'department' } })).status, 200, '同範圍可以');
  const createdOverride = await call('POST', '/api/admin/users', { token, body: { account: `scoped-o-${suffix}`, displayName: 'x', password: seedPassword, departmentId: departments.find((d) => d.code === 'SALES-1').id, roleId: roles.find((r) => r.code === 'viewer').id, dataScopeOverride: 'company' } });
  assert.equal(createdOverride.status, 400, '新增成員時也不能指定更大的範圍');

  // 全公司範圍的管理者不受限
  assert.equal((await call('POST', '/api/admin/roles', { token: demoAdmin.token, body: { code: `wide2-${suffix}`, name: 'x', dataScope: 'company', permissions: [] } })).status, 201);

  // 清理：刪掉這一步建立的角色與成員改回
  for (const id of [same.data.id, narrower.data.id, wideByAdmin.data.id]) await call('DELETE', `/api/admin/roles/${id}`, { token: demoAdmin.token });
  const wide2 = (await call('GET', '/api/admin/roles', { token: demoAdmin.token })).data.find((r) => r.code === `wide2-${suffix}`);
  await call('DELETE', `/api/admin/roles/${wide2.id}`, { token: demoAdmin.token });
  const users = (await call('GET', `/api/admin/users?keyword=scoped-&pageSize=50`, { token: demoAdmin.token })).data.items.filter((u) => u.account.endsWith(suffix));
  for (const user of users) await call('PATCH', `/api/admin/users/${user.membershipId}`, { token: demoAdmin.token, body: { isActive: false, roleId: roles.find((r) => r.code === 'viewer').id } });
  await call('DELETE', `/api/admin/roles/${manager.data.id}`, { token: demoAdmin.token }); // 還有成員使用時會被拒絕，留著也無妨
});

await step('系統角色不可由公司修改', async () => {
  const roles = (await call('GET', '/api/admin/roles', { token: demoAdmin.token })).data;
  const sales = roles.find((role) => role.code === 'sales');
  assert.equal((await call('PUT', `/api/admin/roles/${sales.id}/permissions`, { token: demoAdmin.token, body: { permissions: [] } })).status, 404);
});

await step('部門新增、移動（closure 深度）與刪除規則', async () => {
  const departments = (await call('GET', '/api/admin/departments', { token: demoAdmin.token })).data;
  const salesDept = departments.find((d) => d.code === 'SALES');
  const finance = departments.find((d) => d.code === 'FIN');
  const created = await call('POST', '/api/admin/departments', { token: demoAdmin.token, body: { parentId: salesDept.id, name: `臨時 ${suffix}` } });
  assert.equal(created.status, 201);
  const child = await call('POST', '/api/admin/departments', { token: demoAdmin.token, body: { parentId: created.data.id, name: `臨時子 ${suffix}` } });
  assert.equal((await call('PATCH', `/api/admin/departments/${created.data.id}`, { token: demoAdmin.token, body: { parentId: child.data.id } })).status, 400, '不可移到自己的下層');
  assert.equal((await call('PATCH', `/api/admin/departments/${created.data.id}`, { token: demoAdmin.token, body: { parentId: finance.id } })).status, 200);
  const after = (await call('GET', '/api/admin/departments', { token: demoAdmin.token })).data;
  assert.equal(after.find((d) => d.id === created.data.id).depth, 2);
  assert.equal(after.find((d) => d.id === child.data.id).depth, 3);
  assert.equal((await call('DELETE', `/api/admin/departments/${created.data.id}`, { token: demoAdmin.token })).status, 400, '有下層時不可刪');
  assert.equal((await call('DELETE', `/api/admin/departments/${child.data.id}`, { token: demoAdmin.token })).status, 204);
  assert.equal((await call('DELETE', `/api/admin/departments/${created.data.id}`, { token: demoAdmin.token })).status, 204);
});

await step('停用公司後該公司成員 token 立即失效', async () => {
  const testAdmin = await login(`t-admin-${suffix}`);
  const suspended = await call('PATCH', `/api/platform/companies/${newCompanyId}`, { token: admin.token, body: { status: 'suspended' } });
  assert.equal(suspended.status, 200);
  assert.equal((await call('GET', '/api/auth/me', { token: testAdmin.token })).status, 401);
  assert.equal((await call('POST', '/api/auth/login', { body: { account: `t-admin-${suffix}`, password: seedPassword } })).status, 403);
});

await step('清理：停用這次建立的測試帳號（不讓公司通訊錄與成員下拉堆滿測試帳號）', async () => {
  for (const account of [memberAccount]) {
    const found = (await call('GET', `/api/admin/users?keyword=${account}`, { token: demoAdmin.token })).data.items.find((user) => user.account === account);
    if (found) assert.equal((await call('PATCH', `/api/admin/users/${found.membershipId}`, { token: demoAdmin.token, body: { isActive: false } })).status, 200, account);
  }
});

console.log(`\n全部 ${passed} 項通過`);
