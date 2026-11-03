/**
 * @file use-presence.ts
 * 全後台共用的上線狀態：AdminShell 登入後呼叫 startPresence 建立唯一一條 hub 連線，
 * 任何元件以 presenceOf(membershipId) 讀取狀態；敲門通知也在這裡顯示，所以在後台任何頁面都收得到。
 * 前端只負責顯示，是否在線、能否進入辦公室都由後端判斷。
 */
import { h, reactive, ref } from 'vue';
import { Button, notification, Space } from 'ant-design-vue';
import { HubConnectionState } from '@microsoft/signalr';
import { ensureFreshAccessToken, redirectToLogin, storeAccessToken } from '@/shared/api/http-client';
import { getPresenceApi, officeConnection, RETRY_DELAYS, RETRY_FALLBACK_DELAY, type OfficeKnock, type PresenceEntry } from '@/shared/services/office-service';
import type { Me } from '@/shared/types/auth';
import { hasPermission } from '@/shared/types/permissions';

const ACTIVITY_INTERVAL_MS = 60_000;

const presence = reactive(new Map<string, PresenceEntry>());
const lastSeen = reactive(new Map<string, string>());
const reconnectedHandlers = new Set<() => void>();
const connectedWaiters: (() => void)[] = [];
let knockHandler: ((knock: OfficeKnock) => void) | null = null;
let started = false;
let forced = false;
let everConnected = false;
let restartAttempt = 0;
let restartTimer: ReturnType<typeof setTimeout> | undefined;

/** 全域 hub 連線狀態，供辦公室頁顯示「重新連線中」之類的提示。 */
export const connectionState = ref<'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected'>('idle');

/**
 * 讀取成員目前的上線狀態。
 * @param membershipId 成員身分 ID。
 * @returns 在線（含閒置）時回傳狀態；離線或未知時為 undefined。
 */
export function presenceOf(membershipId: string): PresenceEntry | undefined {
  return presence.get(membershipId);
}

/**
 * 讀取成員在本次頁面生命週期內收到的最後在線時間。
 * @param membershipId 成員身分 ID。
 * @returns ISO 時間字串；沒有收到過離線事件時為 undefined。
 */
export function lastSeenOf(membershipId: string): string | undefined {
  return lastSeen.get(membershipId);
}

/**
 * 等待連線建立；辦公室頁在 JoinOffice 前呼叫。
 * @returns 連線已建立時 resolve。
 */
export function waitForOfficeConnection(): Promise<void> {
  if (officeConnection().state === HubConnectionState.Connected) return Promise.resolve();
  return new Promise((resolve) => connectedWaiters.push(resolve));
}

/**
 * 註冊斷線重連成功後要做的事（辦公室頁用來重新 JoinOffice）。
 * @param handler 重連成功後呼叫的函式。
 * @returns 取消註冊函式。
 */
export function onOfficeReconnected(handler: () => void): () => void {
  reconnectedHandlers.add(handler);
  return () => reconnectedHandlers.delete(handler);
}

/**
 * 設定敲門「前往」的處理函式；辦公室頁開著時由它處理，不必整頁跳轉。
 * @param handler 處理函式；傳 null 代表離開辦公室頁、恢復為整頁跳轉。
 */
export function setOfficeKnockHandler(handler: ((knock: OfficeKnock) => void) | null): void {
  knockHandler = handler;
}

/**
 * 回覆別人的敲門。
 * @param from 敲門者的 membershipId。
 * @param accept 是否接受。
 * @param reply 附帶的回覆訊息。
 */
export function answerKnock(from: string, accept: boolean, reply?: string): Promise<void> {
  return officeConnection().invoke('AnswerKnock', from, accept, reply ?? null);
}

/**
 * AdminShell 取得 me 後呼叫；平台範圍、代入公司或沒有 office 權限時不連線。
 * @param me 目前登入者的工作階段資訊。
 */
export async function startPresence(me: Me): Promise<void> {
  if (started || me.scope.kind !== 'company' || me.scope.isImpersonating || !hasPermission(me.grants, 'office')) return;
  started = true;
  const hub = officeConnection();
  hub.on('PresenceChanged', applyPresence);
  hub.on('Knocked', showKnock);
  hub.on('ForceDisconnect', () => {
    forced = true;
    clearTimeout(restartTimer);
    restartTimer = undefined;
    void hub.stop();
    storeAccessToken(null);
    redirectToLogin();
  });
  hub.onreconnecting(() => (connectionState.value = 'reconnecting'));
  hub.onreconnected(async () => {
    markConnected();
    await loadSnapshot();
    reconnectedHandlers.forEach((handler) => handler());
  });
  hub.onclose(() => {
    connectionState.value = 'disconnected';
    scheduleRestart();
  });
  watchActivity();
  await connect();
}

async function connect(): Promise<void> {
  const hub = officeConnection();
  if (forced || hub.state !== HubConnectionState.Disconnected) return;
  connectionState.value = 'connecting';
  try {
    await hub.start();
    restartAttempt = 0;
    markConnected();
    await loadSnapshot();
    if (everConnected) reconnectedHandlers.forEach((handler) => handler());
    everConnected = true;
  } catch {
    connectionState.value = 'disconnected';
    // 先看 token 還在不在：refresh 被拒時會被清除（真的登出）；網路錯誤則保留，稍後重試。
    // 用 ensureFreshAccessToken 而不是直接 refresh，伺服器掛掉時才不會每次重試都輪替 refresh token。
    if (!(await ensureFreshAccessToken())) {
      redirectToLogin();
      return;
    }
    scheduleRestart();
  }
}

// 伺服器在 token 到期時會主動關閉連線，onclose 與 start 失敗共用同一組重試間隔，避免每次到期都離線 30 秒以上。
function scheduleRestart(): void {
  if (forced || restartTimer !== undefined) return;
  const delay = RETRY_DELAYS[restartAttempt] ?? RETRY_FALLBACK_DELAY;
  restartAttempt++;
  restartTimer = setTimeout(() => {
    restartTimer = undefined;
    void connect();
  }, delay);
}

function markConnected(): void {
  connectionState.value = 'connected';
  connectedWaiters.splice(0).forEach((resolve) => resolve());
}

async function loadSnapshot(): Promise<void> {
  try {
    const entries = await getPresenceApi();
    presence.clear();
    entries.forEach((entry) => presence.set(entry.membershipId, entry));
  } catch {
    // 快照失敗時保留目前狀態，後續事件仍會更新。
  }
}

function applyPresence(entry: PresenceEntry, seenAt: string | null): void {
  if (entry.presence === 'offline') {
    presence.delete(entry.membershipId);
    if (seenAt) lastSeen.set(entry.membershipId, seenAt);
    return;
  }
  presence.set(entry.membershipId, entry);
}

// 有操作時最多每 60 秒回報一次；後端 5 分鐘沒收到就轉閒置。
function watchActivity(): void {
  let lastReport = 0;
  const report = () => {
    const hub = officeConnection();
    if (Date.now() - lastReport < ACTIVITY_INTERVAL_MS || hub.state !== HubConnectionState.Connected) return;
    lastReport = Date.now();
    void hub.send('Activity');
  };
  for (const type of ['pointerdown', 'pointermove', 'keydown', 'wheel']) window.addEventListener(type, report, { passive: true });
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') report();
  });
}

function showKnock(knock: OfficeKnock): void {
  const key = `office-knock-${knock.fromMembershipId}`;
  notification.open({
    key,
    duration: 0,
    placement: 'bottomRight',
    message: `${knock.fromName} 在敲你的門`,
    description: `在${knock.departmentName}辦公室${knock.message ? `：「${knock.message}」` : ''}`,
    btn: () =>
      h(Space, null, () => [
        h(Button, { size: 'small', onClick: () => { notification.close(key); void answerKnock(knock.fromMembershipId, false).catch(() => undefined); } }, () => '稍後'),
        h(Button, { size: 'small', type: 'primary', onClick: () => { notification.close(key); goToKnock(knock); } }, () => '前往'),
      ]),
  });
}

function goToKnock(knock: OfficeKnock): void {
  if (knockHandler) knockHandler(knock);
  else window.location.assign(`/office?dept=${encodeURIComponent(knock.departmentId)}&answer=${encodeURIComponent(knock.fromMembershipId)}`);
}
