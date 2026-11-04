/**
 * @file use-presence.ts
 * 全後台共用的上線狀態：AdminShell 登入後呼叫 startPresence 建立唯一一條 hub 連線，
 * 任何元件以 presenceOf(membershipId) 讀取狀態；敲門通知也在這裡顯示，所以在後台任何頁面都收得到。
 * 前端只負責顯示，是否在線、能否進入辦公室都由後端判斷。
 *
 * 斷線後的重連全部走這裡的 connect()（不使用 SignalR 內建的自動重連），好讓重連前能先處理 token：
 * - 後端送 ReauthRequired（權限版本變更）：不登出，下一次連線前強制換發 token（與 REST 收到 401 的行為一致），
 *   再重新讀取權限；換發被拒就回登入頁，已沒有 office 權限就安靜停用，不影響後台其他功能。
 * - 後端送 ForceDisconnect（帳號、成員身分或公司已停用）：直接登出，不重連。
 * - hub.start() 收到 401（token 已過時）：同樣先強制換發；網路錯誤則不換發，避免伺服器掛掉時每次重試都輪替 refresh token。
 */
import { h, reactive, ref } from 'vue';
import { Button, notification, Space } from 'ant-design-vue';
import { HttpError, HubConnectionState } from '@microsoft/signalr';
import { ensureFreshAccessToken, readAccessToken, redirectToLogin, refreshAccessToken, storeAccessToken } from '@/shared/api/http-client';
import { getMeApi } from '@/shared/services/auth-service';
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
// 辦公室對這個工作階段不可用（權限被收回）；不重連，也不登出。
let stopped = false;
let connecting = false;
// 下一次連線前必須用 refreshAccessToken 強制換發；ensureFreshAccessToken 只在快過期時才換，換不到新的權限版本。
let needsTokenRefresh = false;
// 換發成功但還沒確認新權限是否仍有 office:read。
let needsPermissionCheck = false;
let everConnected = false;
let restartAttempt = 0;
let restartTimer: ReturnType<typeof setTimeout> | undefined;

/** 全域 hub 連線狀態，供辦公室頁顯示「重新連線中」之類的提示；unavailable 表示這個工作階段不會連線。 */
export const connectionState = ref<'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'unavailable'>('idle');

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
  if (started || !canUsePresence(me)) return;
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
  hub.on('ReauthRequired', () => {
    needsTokenRefresh = true;
    restartAttempt = 0;
    // 不等伺服器 Abort：自己先關，才能確定走到下面「先換發再重連」的路徑。
    void hub.stop();
  });
  hub.onclose(() => {
    if (stopped) return;
    connectionState.value = 'disconnected';
    scheduleRestart();
  });
  watchActivity();
  await connect();
}

function canUsePresence(me: Me): boolean {
  return me.scope.kind === 'company' && !me.scope.isImpersonating && hasPermission(me.grants, 'office');
}

async function connect(): Promise<void> {
  const hub = officeConnection();
  if (forced || stopped || connecting || hub.state !== HubConnectionState.Disconnected) return;
  connecting = true;
  connectionState.value = everConnected ? 'reconnecting' : 'connecting';
  try {
    if (!(await prepareToken())) return;
    await hub.start();
    restartAttempt = 0;
    markConnected();
    await loadSnapshot();
    if (everConnected) reconnectedHandlers.forEach((handler) => handler());
    everConnected = true;
  } catch (error) {
    connectionState.value = 'disconnected';
    if (error instanceof HttpError && error.statusCode === 401) {
      // token 內的權限版本已過時：下一次連線前強制換發。
      needsTokenRefresh = true;
    } else if (!(await ensureFreshAccessToken())) {
      // 先看 token 還在不在：refresh 被拒時會被清除（真的登出）；網路錯誤則保留，稍後重試。
      // 用 ensureFreshAccessToken 而不是直接 refresh，伺服器掛掉時才不會每次重試都輪替 refresh token。
      redirectToLogin();
      return;
    }
    scheduleRestart();
  } finally {
    connecting = false;
  }
}

// 回傳 false 代表這次不連線（已導向登入、已停用或已排定重試）。
async function prepareToken(): Promise<boolean> {
  if (needsTokenRefresh) {
    if (!(await refreshAccessToken())) {
      // 換發被拒時 http-client 已清除 token，才是真的登出；token 還在代表只是網路問題。
      if (readAccessToken() === null) redirectToLogin();
      else retryLater();
      return false;
    }
    needsTokenRefresh = false;
    needsPermissionCheck = true;
  }
  if (needsPermissionCheck) {
    try {
      if (!canUsePresence(await getMeApi())) {
        stopPresence();
        return false;
      }
      needsPermissionCheck = false;
    } catch {
      retryLater();
      return false;
    }
  }
  return true;
}

function retryLater(): void {
  connectionState.value = 'disconnected';
  scheduleRestart();
}

// 權限被收回：安靜停用 presence，後台其他功能照常。
function stopPresence(): void {
  stopped = true;
  clearTimeout(restartTimer);
  restartTimer = undefined;
  void officeConnection().stop();
  presence.clear();
  connectionState.value = 'unavailable';
}

// 伺服器在 token 到期時會主動關閉連線，onclose 與 start 失敗共用同一組重試間隔，避免每次到期都離線 30 秒以上。
function scheduleRestart(): void {
  if (forced || stopped || restartTimer !== undefined) return;
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
