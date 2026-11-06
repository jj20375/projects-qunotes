/**
 * 虛擬辦公室端對端驗證：hub 連線權限、公司隔離、走動、靠近聊天、敲門、表情、狀態、
 * 權限版本變更（ReauthRequired，不登出）、停用帳號（ForceDisconnect）與離線寬限期。共 11 項。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false、Quote__OfficeAuthSweepSeconds=5 啟動，且已執行 Seeder（SeedDemoData=true）。
 * 用法：node tests/office-smoke.mjs [baseUrl]
 * 密碼讀自 .env.development-api.local 的 Quote__BootstrapAdminPassword，不會輸出到畫面。
 * 以 Node 24 內建 WebSocket 與 SignalR JSON 協定直接連線（skipNegotiation），不需要額外套件。
 */
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { setTimeout as sleep } from 'node:timers/promises';

// Node 20 沒有內建 WebSocket，不先擋下會在第二步才以 ReferenceError 崩潰，看不出原因。
if (typeof WebSocket === 'undefined') {
  console.error(`office-smoke 需要 Node 24（內建 WebSocket），目前是 ${process.version}；請先執行 nvm use 24。`);
  process.exit(1);
}

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
const wsBase = baseUrl.replace(/^http/, 'ws');
const RS = '\u001e';
const sweepWaitMs = Number(process.env.OFFICE_SWEEP_WAIT_MS ?? 15_000);
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

async function login(account, password = seedPassword) {
  const result = await call('POST', '/api/auth/login', { body: { account, password } });
  assert.equal(result.status, 200, `${account} 登入失敗：${JSON.stringify(result.data)}`);
  return { token: result.data.accessToken };
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

/** 最小的 SignalR JSON 協定客戶端：握手、呼叫、接收伺服器事件。 */
class HubClient {
  constructor(token) {
    this.events = [];
    this.pending = new Map();
    this.nextId = 1;
    this.closed = false;
    this.ws = new WebSocket(`${wsBase}/api/hubs/office?access_token=${encodeURIComponent(token)}`);
    this.ready = new Promise((resolve, reject) => {
      this.resolveReady = resolve;
      this.ws.addEventListener('open', () => this.ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS));
      this.ws.addEventListener('error', () => reject(new Error('WebSocket 連線失敗')));
    });
    this.ws.addEventListener('message', (event) => this.receive(String(event.data)));
    this.ws.addEventListener('close', () => {
      this.closed = true;
      for (const { reject } of this.pending.values()) reject(new Error('連線已關閉'));
      this.pending.clear();
    });
  }

  /** 握手完成後稍等，讓伺服器跑完 OnConnectedAsync（加入群組、登記上線）。 */
  static async open(token) {
    const client = new HubClient(token);
    await client.ready;
    await sleep(200);
    return client;
  }

  receive(data) {
    for (const frame of data.split(RS).filter(Boolean)) {
      const message = JSON.parse(frame);
      if (message.type === undefined) this.resolveReady();
      else if (message.type === 1) this.events.push({ target: message.target, args: message.arguments });
      else if (message.type === 3) {
        const waiter = this.pending.get(message.invocationId);
        this.pending.delete(message.invocationId);
        if (message.error) waiter?.reject(new Error(message.error));
        else waiter?.resolve(message.result);
      } else if (message.type === 7) this.closed = true;
    }
  }

  invoke(target, ...args) {
    const invocationId = String(this.nextId++);
    return new Promise((resolve, reject) => {
      this.pending.set(invocationId, { resolve, reject });
      this.ws.send(JSON.stringify({ type: 1, invocationId, target, arguments: args }) + RS);
    });
  }

  send(target, ...args) {
    this.ws.send(JSON.stringify({ type: 1, target, arguments: args }) + RS);
  }

  async waitFor(target, predicate = () => true, timeout = 3000) {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
      const index = this.events.findIndex((event) => event.target === target && predicate(...event.args));
      if (index >= 0) return this.events.splice(index, 1)[0].args;
      await sleep(25);
    }
    throw new Error(`等待 ${target} 逾時`);
  }

  async expectNone(target, predicate = () => true, wait = 600) {
    await sleep(wait);
    assert.ok(!this.events.some((event) => event.target === target && predicate(...event.args)), `不應收到 ${target}`);
  }

  async waitClosed(timeout) {
    const end = Date.now() + timeout;
    while (!this.closed) {
      if (Date.now() > end) throw new Error('連線沒有被關閉');
      await sleep(100);
    }
  }

  close() {
    this.ws.close();
  }
}

const createdMembers = [];
let demoAdmin;
let sales;
let memberA;
let memberB;
let outsider;
let hubA;
let hubB;
let hubX;
let snapshotA;
let salesRoleId;
let viewerRoleId;
let createMember;

await step('準備：專屬測試部門的兩位新成員與另一家公司', async () => {
  demoAdmin = await login('demo-admin');
  const departments = (await call('GET', '/api/admin/departments', { token: demoAdmin.token })).data;
  const root = departments.find((department) => department.parentId === null);
  assert.ok(root, '找不到根部門');
  // 自建部門：不依賴種子資料的部門名稱，也確保座位清單只有本次測試的成員。
  const createdDepartment = await call('POST', '/api/admin/departments', { token: demoAdmin.token, body: { parentId: root.id, name: `辦公室 ${suffix}` } });
  assert.equal(createdDepartment.status, 201, JSON.stringify(createdDepartment.data));
  sales = { id: createdDepartment.data.id };
  const roles = (await call('GET', '/api/admin/roles', { token: demoAdmin.token })).data;
  const salesRole = roles.find((role) => role.code === 'sales');
  salesRoleId = salesRole.id;
  viewerRoleId = roles.find((role) => role.code === 'viewer').id;
  createMember = async (prefix, roleId) => {
    const account = `${prefix}-${suffix}`;
    const created = await call('POST', '/api/admin/users', { token: demoAdmin.token, body: { account, displayName: account, password: seedPassword, departmentId: sales.id, roleId } });
    assert.equal(created.status, 201, JSON.stringify(created.data));
    createdMembers.push(created.data.membershipId);
    return { ...(await login(account)), membershipId: created.data.membershipId };
  };
  memberA = await createMember('oa', salesRole.id);
  memberB = await createMember('ob', salesRole.id);

  const admin = await login('admin');
  const company = await call('POST', '/api/platform/companies', {
    token: admin.token,
    body: { name: `辦公室測試 ${suffix}`, slug: `o-${suffix}`, adminAccount: `o-admin-${suffix}`, adminDisplayName: '外部管理員', adminPassword: seedPassword },
  });
  assert.equal(company.status, 201, JSON.stringify(company.data));
  outsider = await login(`o-admin-${suffix}`);
});

await step('同公司成員進入測試部門辦公室：地圖含兩人座位，後到的人不疊在同一格', async () => {
  hubA = await HubClient.open(memberA.token);
  hubB = await HubClient.open(memberB.token);
  snapshotA = await hubA.invoke('JoinOffice', sales.id);
  assert.deepEqual([snapshotA.me.x, snapshotA.me.y], [snapshotA.map.spawn.x, snapshotA.map.spawn.y]);
  const seatIds = snapshotA.map.seats.map((seat) => seat.membershipId);
  assert.ok(seatIds.includes(memberA.membershipId) && seatIds.includes(memberB.membershipId));
  await hubB.invoke('JoinOffice', sales.id);
  const [joined] = await hubA.waitFor('OccupantJoined', (occupant) => occupant.membershipId === memberB.membershipId);
  assert.notDeepEqual([joined.x, joined.y], [snapshotA.me.x, snapshotA.me.y]);
  const rooms = (await call('GET', '/api/office/rooms', { token: memberA.token })).data;
  const room = rooms.find((value) => value.departmentId === sales.id);
  assert.ok(room.memberCount >= 2 && room.onlineCount >= 2 && room.occupantCount >= 2, JSON.stringify(room));
});

await step('公司隔離：另一家公司進不了這間辦公室，也看不到兩人的上線狀態', async () => {
  hubX = await HubClient.open(outsider.token);
  await assert.rejects(hubX.invoke('JoinOffice', sales.id), /找不到此部門的辦公室/);
  const outsiderRoot = (await call('GET', '/api/admin/departments', { token: outsider.token })).data[0];
  await hubX.invoke('JoinOffice', outsiderRoot.id);
  const seen = (await call('GET', '/api/office/presence', { token: outsider.token })).data;
  assert.ok(!seen.some((entry) => [memberA.membershipId, memberB.membershipId].includes(entry.membershipId)));
  const mine = (await call('GET', '/api/office/presence', { token: demoAdmin.token })).data;
  assert.ok([memberA.membershipId, memberB.membershipId].every((id) => mine.some((entry) => entry.membershipId === id && entry.presence === 'online')));
});

await step('走動：相鄰格廣播給同辦公室、斜走被拉回、其他公司收不到', async () => {
  const { spawn } = snapshotA.map;
  hubA.send('Move', spawn.x + 1, spawn.y, 'right');
  await hubB.waitFor('Moved', (id, x, y) => id === memberA.membershipId && x === spawn.x + 1 && y === spawn.y);
  hubA.send('Move', spawn.x + 2, spawn.y + 1, 'right');
  await hubA.waitFor('PositionCorrected', (x, y) => x === spawn.x + 1 && y === spawn.y);
  await hubX.expectNone('Moved');
});

await step('靠近即聊：附近的人收到，對方回座位後無人可聊', async () => {
  await hubA.invoke('SendChat', '哈囉');
  const [message] = await hubB.waitFor('ChatReceived', (value) => value.text === '哈囉');
  assert.deepEqual(message.recipients, [memberB.membershipId]);
  await hubX.expectNone('ChatReceived');
  await hubB.invoke('GoToSeat');
  await hubA.waitFor('Moved', (id) => id === memberB.membershipId);
  await assert.rejects(hubA.invoke('SendChat', '還在嗎'), /附近沒有人/);
});

await step('敲門：送達對方、30 秒內不能重複、接受後被放到敲門者旁邊；其他公司敲不到', async () => {
  const { spawn } = snapshotA.map;
  await assert.rejects(hubB.invoke('AnswerKnock', memberA.membershipId, true, null), /沒有待回應的敲門/);
  await hubA.invoke('Knock', memberB.membershipId, '來一下');
  const [knock] = await hubB.waitFor('Knocked');
  assert.equal(knock.fromMembershipId, memberA.membershipId);
  assert.equal(knock.departmentId, sales.id);
  assert.equal(knock.message, '來一下');
  await assert.rejects(hubA.invoke('Knock', memberB.membershipId, null), /30 秒/);
  await hubB.invoke('AnswerKnock', memberA.membershipId, true, null);
  await hubA.waitFor('KnockAnswered', (id, _name, accepted) => id === memberB.membershipId && accepted === true);
  const [, x, y] = await hubA.waitFor('Moved', (id) => id === memberB.membershipId);
  assert.equal(Math.abs(x - (spawn.x + 1)) + Math.abs(y - spawn.y), 1);
  await assert.rejects(hubX.invoke('Knock', memberA.membershipId, null), /找不到此成員或對方不在線上/);
});

await step('表情與狀態：只接受定義的值並廣播給同公司', async () => {
  await assert.rejects(hubA.invoke('Emote', 'skull'), /表情不正確/);
  await hubA.invoke('Emote', 'wave');
  await hubB.waitFor('Emoted', (id, key) => id === memberA.membershipId && key === 'wave');
  await assert.rejects(hubA.invoke('SetStatus', 'sleeping', null), /狀態不正確/);
  await hubA.invoke('SetStatus', 'meeting', '週會');
  await hubB.waitFor('PresenceChanged', (entry) => entry.membershipId === memberA.membershipId && entry.status === 'meeting' && entry.statusText === '週會');
  await hubX.expectNone('PresenceChanged', (entry) => entry.membershipId === memberA.membershipId);
});

await step('沒有 office 權限的角色無法建立連線，也不能讀房間列表', async () => {
  const role = await call('POST', '/api/admin/roles', { token: demoAdmin.token, body: { code: `no-office-${suffix}`, name: '無辦公室', dataScope: 'own', permissions: [] } });
  assert.equal(role.status, 201, JSON.stringify(role.data));
  const account = `oc-${suffix}`;
  const created = await call('POST', '/api/admin/users', { token: demoAdmin.token, body: { account, displayName: account, password: seedPassword, departmentId: sales.id, roleId: role.data.id } });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  createdMembers.push(created.data.membershipId);
  const memberC = await login(account);
  const hubC = new HubClient(memberC.token);
  await hubC.ready.catch(() => undefined);
  await hubC.waitClosed(5000);
  assert.equal((await call('GET', '/api/office/rooms', { token: memberC.token })).status, 403);
});

await step('權限版本變更：收到 ReauthRequired（不是 ForceDisconnect）後斷線，重新取得 token 即可再連線', async () => {
  const memberD = await createMember('od', salesRoleId);
  const hubD = await HubClient.open(memberD.token);
  await hubD.invoke('JoinOffice', sales.id);
  // 改角色會遞增 users.AuthVersion；REST 對這種情況只是 401 後換發，辦公室連線也不該把人登出。
  assert.equal((await call('PATCH', `/api/admin/users/${memberD.membershipId}`, { token: demoAdmin.token, body: { roleId: viewerRoleId } })).status, 200);
  await hubD.waitFor('ReauthRequired', () => true, sweepWaitMs);
  await hubD.waitClosed(5000);
  assert.ok(!hubD.events.some((event) => event.target === 'ForceDisconnect'), '權限版本變更不應收到 ForceDisconnect');
  assert.equal((await call('GET', '/api/office/rooms', { token: memberD.token })).status, 401, '舊 token 的權限版本已過時');
  const renewed = await login(`od-${suffix}`);
  const reconnected = await HubClient.open(renewed.token);
  const snapshot = await reconnected.invoke('JoinOffice', sales.id);
  assert.equal(snapshot.me.membershipId, memberD.membershipId);
  reconnected.close();
});

await step('停用帳號後，先收到 ForceDisconnect，連線在巡檢週期內被斷開並離開辦公室', async () => {
  assert.equal((await call('PATCH', `/api/admin/users/${memberB.membershipId}`, { token: demoAdmin.token, body: { isActive: false } })).status, 200);
  // 伺服器送出事件後等一小段才斷線，事件一定會先到；若這裡變得不穩，代表送達與斷線的順序被改壞了。
  await hubB.waitFor('ForceDisconnect', () => true, sweepWaitMs);
  await hubB.waitClosed(sweepWaitMs);
  await hubA.waitFor('OccupantLeft', (id) => id === memberB.membershipId, 30_000);
});

await step('離線寬限期：斷線 15 秒內仍在線，之後轉離線', async () => {
  hubA.close();
  await sleep(3000);
  const during = (await call('GET', '/api/office/presence', { token: demoAdmin.token })).data;
  assert.ok(during.some((entry) => entry.membershipId === memberA.membershipId), '寬限期內應仍在線');
  await sleep(20_000);
  const after = (await call('GET', '/api/office/presence', { token: demoAdmin.token })).data;
  assert.ok(!after.some((entry) => entry.membershipId === memberA.membershipId), '超過寬限期應轉離線');
});

hubX.close();
for (const membershipId of createdMembers) {
  await call('PATCH', `/api/admin/users/${membershipId}`, { token: demoAdmin.token, body: { isActive: false } });
}
console.log(`\n全部通過（${passed} 項）`);
