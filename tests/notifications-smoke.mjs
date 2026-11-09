/**
 * 今日待辦與站內通知驗證：待辦的區間、「只看我的」與資料範圍；背景工作產生到期通知（收件人、不重複、
 * 專案停止進行或看不到時不通知）、已讀、改期與不用跟進後的「已處理」、專案換負責人後的可見性，以及公司隔離。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false Quote__FollowUpNotificationSweepSeconds=5 Quote__FollowUpNotificationStartHour=0
 * 啟動（每 5 秒掃描一次、不受測試執行時間影響），並已執行 Seeder。
 * 用法：node tests/notifications-smoke.mjs [baseUrl]
 */
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { setTimeout as sleep } from 'node:timers/promises';

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

async function ok(promise, expected = [200, 201, 204]) {
  const result = await promise;
  assert.ok(expected.includes(result.status), `${result.status} ${JSON.stringify(result.data)}`);
  return result.data;
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

/** 背景工作每 5 秒掃一次，最多等 40 秒直到條件成立。 */
async function waitFor(description, probe, timeoutMs = 40_000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    last = await probe();
    if (last.done) return last.value;
    await sleep(1000);
  }
  assert.fail(`等不到：${description}（最後一次：${JSON.stringify(last?.value)}）`);
}

const taipeiDate = (offsetDays = 0) => new Date(Date.now() + 8 * 3600_000 + offsetDays * 86400_000).toISOString().slice(0, 10);
const hoursAgo = (hours) => new Date(Date.now() - hours * 3600_000).toISOString();
const write = (token, project, body) => call('POST', `/api/projects/${project}/followups`, { token, body });
const todo = (token, query = '') => call('GET', `/api/followups/todo${query}`, { token });
const notices = (token, query = '') => call('GET', `/api/notifications${query}`, { token });
const dueDates = (result) => result.data.items.map((value) => value.dueOn).sort();

const platform = await login('admin');
let admin;
let manager;
let salesA;
let salesB;
let accountant;
let userIds = {};
let teamDept;
let projects = {};
let noticeIds = [];

await step('準備：新公司、業務部與一課、財務部、主管、兩位業務、會計', async () => {
  await ok(call('POST', '/api/platform/companies', { token: platform, body: { name: `Notice ${suffix}`, slug: `nt-${suffix}`, adminAccount: `nt-${suffix}`, adminDisplayName: '通知管理員', adminPassword: seedPassword } }));
  admin = await login(`nt-${suffix}`);
  const root = (await ok(call('GET', '/api/admin/departments', { token: admin })))[0];
  const salesDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: root.id, name: '業務部' } }))).id;
  teamDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: salesDept, name: '業務一課' } }))).id;
  const financeDept = (await ok(call('POST', '/api/admin/departments', { token: admin, body: { parentId: root.id, name: '財務部' } }))).id;
  const roles = await ok(call('GET', '/api/admin/roles', { token: admin }));
  const role = (code) => roles.find((value) => value.code === code).id;
  for (const [account, roleCode, departmentId, displayName] of [
    ['m', 'sales-manager', salesDept, '主管'],
    ['a', 'sales', teamDept, '業務A'],
    ['b', 'sales', teamDept, '業務B'],
    ['c', 'accountant', financeDept, '會計'],
  ]) {
    await ok(call('POST', '/api/admin/users', { token: admin, body: { account: `nt-${account}-${suffix}`, displayName, password: seedPassword, departmentId, roleId: role(roleCode) } }));
  }
  manager = await login(`nt-m-${suffix}`);
  salesA = await login(`nt-a-${suffix}`);
  salesB = await login(`nt-b-${suffix}`);
  accountant = await login(`nt-c-${suffix}`);
  const members = (await ok(call('GET', '/api/admin/users?pageSize=50', { token: admin }))).items;
  userIds = Object.fromEntries(['a', 'b'].map((key) => [key, members.find((value) => value.account === `nt-${key}-${suffix}`).userId]));
});

await step('準備：業務 A 五個專案（逾期、今天、七天內、二十天後、未得標）、業務 B 一個逾期專案', async () => {
  const client = (await ok(call('POST', '/api/clients', { token: admin, body: { name: '通知客戶', shortName: `通知${suffix}`, departmentId: teamDept } }))).id;
  const create = async (token, name) => (await ok(call('POST', '/api/projects', { token, body: { name: `${name} ${suffix}`, clientId: client } }))).id;
  projects = {
    over: await create(salesA, 'NT-逾期'),
    today: await create(salesA, 'NT-今天'),
    soon: await create(salesA, 'NT-七天內'),
    far: await create(salesA, 'NT-二十天後'),
    lost: await create(salesA, 'NT-未得標'),
    b: await create(salesB, 'NT-B逾期'),
  };
  // 先把專案改成未得標再寫跟進：背景工作每 5 秒掃描，先寫會有機會在改狀態前就被通知。
  await ok(call('POST', `/api/projects/${projects.lost}/status`, { token: salesA, body: { status: 'quoting' } }));
  await ok(call('POST', `/api/projects/${projects.lost}/status`, { token: salesA, body: { status: 'lost', reason: '價格' } }));
  const record = (token, project, occurredAt, next, nextAction) => ok(write(token, project, { type: 'call', occurredAt, summary: '電話聯絡', nextFollowUpOn: next, nextAction }), [201]);
  await record(salesA, projects.over, hoursAgo(120), taipeiDate(-3), '寄修正版報價');
  await record(salesA, projects.today, hoursAgo(3), taipeiDate(0), '確認交期');
  await record(salesA, projects.soon, hoursAgo(3), taipeiDate(3), '約現場會勘');
  await record(salesA, projects.far, hoursAgo(3), taipeiDate(20), '季度回訪');
  await record(salesA, projects.lost, hoursAgo(72), taipeiDate(-2), '重新報價');
  await record(salesB, projects.b, hoursAgo(48), taipeiDate(-1), '追款');
  // 主管補一筆，成為「今天」這個專案最新一筆的撰寫人。
  await record(manager, projects.today, hoursAgo(1), taipeiDate(0), '主管追蹤');
});

await step('今日待辦：業務 A 只看自己的，三個區間的件數與排序正確，未得標與太遠的不算', async () => {
  const counts = (await ok(todo(salesA))).counts;
  assert.deepEqual(counts, { overdue: 1, today: 1, upcoming: 1 });
  const overdue = await ok(todo(salesA, '?range=overdue'));
  assert.equal(overdue.total, 1);
  assert.equal(overdue.items[0].id, projects.over);
  assert.equal(overdue.items[0].nextAction, '寄修正版報價');
  assert.equal(overdue.items[0].owner.displayName, '業務A');
  assert.equal(overdue.items[0].nextFollowUpOn, taipeiDate(-3));
  assert.equal((await ok(todo(salesA, '?range=today'))).items[0].id, projects.today);
  assert.equal((await ok(todo(salesA, '?range=upcoming'))).items[0].id, projects.soon);
  assert.equal((await ok(todo(salesB))).counts.overdue, 1, '業務 B 只看得到自己的逾期專案');
});

await step('今日待辦：主管「只看我的」是自己排定的，關掉後看到部門樹內全部；分頁由後端處理；輸入檢查與權限', async () => {
  assert.deepEqual((await ok(todo(manager))).counts, { overdue: 0, today: 1, upcoming: 0 });
  const all = await ok(todo(manager, '?mine=false&range=overdue&pageSize=1&page=1'));
  assert.equal(all.total, 2);
  assert.equal(all.items.length, 1);
  assert.equal(all.items[0].id, projects.over, '逾期最久的排第一');
  assert.deepEqual((await ok(todo(manager, '?mine=false'))).counts, { overdue: 2, today: 1, upcoming: 1 });
  const second = await ok(todo(manager, '?mine=false&range=overdue&pageSize=1&page=2'));
  assert.equal(second.items[0].id, projects.b);
  assert.equal((await todo(salesA, '?range=nonsense')).status, 400);
  assert.equal((await todo(accountant)).status, 403, '沒有跟進紀錄權限就不能看待辦');
});

await step('通知：背景工作通知負責人與排定下次聯絡的人；未得標、七天內、二十天後與看不到的人都沒有', async () => {
  const forA = await waitFor('業務 A 收到 2 則通知', async () => {
    const result = await notices(salesA);
    return { done: result.data.total >= 2, value: result };
  });
  assert.equal(forA.data.total, 2);
  assert.equal(forA.data.unreadCount, 2);
  assert.deepEqual(dueDates(forA), [taipeiDate(-3), taipeiDate(0)].sort());
  noticeIds = forA.data.items.map((value) => value.id);
  const item = forA.data.items.find((value) => value.projectId === projects.over);
  assert.match(item.project.name, /NT-逾期/);
  assert.equal(item.project.clientName, `通知${suffix}`, '客戶顯示簡稱');
  assert.equal(item.resolved, false);

  const forManager = await waitFor('主管收到「今天」那一則', async () => {
    const result = await notices(manager);
    return { done: result.data.total >= 1, value: result };
  });
  assert.equal(forManager.data.total, 1, '主管只因為是最新一筆的撰寫人而收到');
  assert.equal(forManager.data.items[0].projectId, projects.today);

  const forB = await waitFor('業務 B 收到 1 則通知', async () => {
    const result = await notices(salesB);
    return { done: result.data.total >= 1, value: result };
  });
  assert.equal(forB.data.items[0].projectId, projects.b);
  assert.equal((await notices(admin)).data.total, 0, '管理員不是負責人也不是撰寫人，不收通知');
  assert.equal((await notices(accountant)).status, 403);
});

await step('通知：同一個到期日只通知一次（再等兩輪掃描，數量不變）', async () => {
  await sleep(12_000);
  assert.equal((await notices(salesA)).data.total, 2);
  assert.equal((await notices(manager)).data.total, 1);
  assert.equal((await notices(salesB)).data.total, 1);
});

await step('已讀：單則、只看未讀、全部已讀；不能動別人的通知', async () => {
  assert.equal((await call('POST', `/api/notifications/${noticeIds[0]}/read`, { token: salesB })).status, 404, '別人的通知回 404');
  await ok(call('POST', `/api/notifications/${noticeIds[0]}/read`, { token: salesA }));
  const afterOne = await ok(notices(salesA));
  assert.equal(afterOne.unreadCount, 1);
  assert.equal(afterOne.items[0].readAt, null, '未讀排在前面');
  assert.notEqual(afterOne.items[1].readAt, null);
  const unreadOnly = await ok(notices(salesA, '?unread=true'));
  assert.equal(unreadOnly.total, 1);
  assert.equal(unreadOnly.items.length, 1);
  assert.equal((await ok(notices(salesA, '?pageSize=1&page=2'))).items.length, 1, '分頁由後端處理');
  await ok(call('POST', '/api/notifications/read-all', { token: salesA }));
  assert.equal((await ok(notices(salesA))).unreadCount, 0);
  assert.equal((await ok(notices(salesB))).unreadCount, 1, '全部已讀只影響自己');
});

await step('改期與不用跟進：舊通知變成已處理，改期後的新到期日會有新通知', async () => {
  // 補登一筆較新的紀錄（發生在兩天前）並把下次聯絡改成昨天。
  await ok(write(salesA, projects.over, { type: 'visit', occurredAt: hoursAgo(48), summary: '現場拜訪', nextFollowUpOn: taipeiDate(-1), nextAction: '回覆規格' }), [201]);
  const result = await waitFor('改期後產生新的通知', async () => {
    const value = await notices(salesA);
    return { done: value.data.total >= 3, value };
  });
  const forOver = result.data.items.filter((value) => value.projectId === projects.over);
  assert.equal(forOver.length, 2);
  assert.equal(forOver.find((value) => value.dueOn === taipeiDate(-3)).resolved, true, '舊到期日已改期，算已處理');
  assert.equal(forOver.find((value) => value.dueOn === taipeiDate(-1)).resolved, false);

  await ok(call('POST', `/api/projects/${projects.over}/followups/dismiss-next`, { token: salesA }));
  const after = await ok(notices(salesA));
  assert.ok(after.items.filter((value) => value.projectId === projects.over).every((value) => value.resolved), '不用跟進後全部算已處理');
  assert.equal((await ok(todo(salesA))).counts.overdue, 0, '待辦也跟著消失');
});

await step('專案換負責人：原收件人看不到專案就不再看到通知，新負責人收到通知', async () => {
  const clientId = (await ok(call('GET', `/api/projects/${projects.over}`, { token: salesA }))).client.id;
  const moved = (await ok(call('POST', '/api/projects', { token: salesA, body: { name: `NT-移交 ${suffix}`, clientId } }))).id;
  await ok(write(salesA, moved, { type: 'call', occurredAt: hoursAgo(144), summary: '電話聯絡', nextFollowUpOn: taipeiDate(-4), nextAction: '回電' }), [201]);
  await waitFor('業務 A 收到移交專案的通知', async () => {
    const value = await notices(salesA);
    return { done: value.data.items.some((item) => item.projectId === moved), value };
  });
  const detail = await ok(call('GET', `/api/projects/${moved}`, { token: admin }));
  await ok(call('PUT', `/api/projects/${moved}`, { token: admin, body: { name: detail.name, clientId, departmentId: detail.departmentId, ownerUserId: userIds.b } }));
  const afterMove = await ok(notices(salesA));
  assert.ok(!afterMove.items.some((item) => item.projectId === moved), '看不到專案就不顯示通知，也不洩漏專案名稱');
  const forB = await waitFor('新負責人業務 B 收到通知', async () => {
    const value = await notices(salesB);
    return { done: value.data.items.some((item) => item.projectId === moved), value };
  });
  assert.equal(forB.data.items.find((item) => item.projectId === moved).dueOn, taipeiDate(-4));
  assert.ok((await ok(todo(salesB, '?range=overdue&pageSize=20'))).items.some((item) => item.id === moved), '新負責人的待辦也有');
});

await step('公司隔離：另一家公司的人看不到這家的通知，也不能標已讀', async () => {
  await ok(call('POST', '/api/platform/companies', { token: platform, body: { name: `Notice2 ${suffix}`, slug: `nt2-${suffix}`, adminAccount: `nt2-${suffix}`, adminDisplayName: '另一家管理員', adminPassword: seedPassword } }));
  const other = await login(`nt2-${suffix}`);
  assert.equal((await ok(notices(other))).total, 0);
  assert.equal((await ok(todo(other, '?mine=false'))).counts.overdue, 0);
  assert.equal((await call('POST', `/api/notifications/${noticeIds[1]}/read`, { token: other })).status, 404);
});

await step('清理：停用這次建立的測試帳號', async () => {
  for (const account of ['m', 'a', 'b', 'c'].map((key) => `nt-${key}-${suffix}`)) {
    const found = (await ok(call('GET', `/api/admin/users?keyword=${account}`, { token: admin }))).items.find((user) => user.account === account);
    if (found) assert.equal((await call('PATCH', `/api/admin/users/${found.membershipId}`, { token: admin, body: { isActive: false } })).status, 200, account);
  }
});

console.log(`\n全部 ${passed} 項通過`);
