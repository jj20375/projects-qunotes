/**
 * @file office-service.ts
 * 虛擬辦公室的型別、REST 呼叫與全後台共用的 SignalR 連線。
 *
 * hub 的方法與事件名稱必須與後端 OfficeHub／IOfficeClient 一致；所有驗證都在後端，前端只送意圖。
 */
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { apiRequest, ensureFreshAccessToken, redirectToLogin } from '@/shared/api/http-client';

/** 連線狀態：online 上線、idle 閒置（一段時間沒操作）、offline 離線。 */
export type PresenceState = 'online' | 'idle' | 'offline';
/** 使用者自行設定的狀態：有空、忙碌、開會中、休息中、外出。 */
export type OfficeStatus = 'available' | 'busy' | 'meeting' | 'break' | 'away';
/** 角色面向的方向。 */
export type Facing = 'up' | 'down' | 'left' | 'right';

/** 全公司可見的上線狀態 */
export interface PresenceEntry {
  membershipId: string;
  presence: PresenceState;
  status: OfficeStatus;
  statusText: string | null;
  /** 所屬部門 */
  homeDepartmentId: string | null;
  /** 目前所在的辦公室；不在任何辦公室時為 null */
  officeDepartmentId: string | null;
}

/** 地圖上的功能區塊（座位區、會議區、茶水間、入口），以格子為單位。 */
export interface OfficeZone {
  id: string;
  type: 'desk' | 'meeting' | 'pantry' | 'entrance';
  x: number;
  y: number;
  w: number;
  h: number;
}

/** 部門成員的固定座位；lastSeenAt 供離線成員的座位顯示最後在線時間。 */
export interface OfficeSeat {
  membershipId: string;
  name: string;
  x: number;
  y: number;
  facing: Facing;
  lastSeenAt: string | null;
}

/** 地圖上的家具擺設。 */
export interface OfficeFurniture {
  kind: string;
  x: number;
  y: number;
}

/** 後端產生的地圖；grid 每列一個字串：# 牆、. 可走、f 家具 */
export interface OfficeMap {
  width: number;
  height: number;
  tileSize: number;
  theme: string;
  grid: string[];
  furniture: OfficeFurniture[];
  zones: OfficeZone[];
  seats: OfficeSeat[];
  spawn: { x: number; y: number };
}

/** 目前在辦公室內的成員。 */
export interface OfficeOccupant {
  membershipId: string;
  name: string;
  avatarKey: string;
  x: number;
  y: number;
  facing: Facing;
  status: OfficeStatus;
  statusText: string | null;
  homeDepartmentId: string | null;
}

/** JoinOffice 回傳的辦公室快照。 */
export interface OfficeSnapshot {
  map: OfficeMap;
  departmentName: string;
  me: OfficeOccupant;
  occupants: OfficeOccupant[];
}

/** 聊天訊息；recipients 為後端依距離算出的接收者。 */
export interface OfficeChatMessage {
  fromMembershipId: string;
  fromName: string;
  text: string;
  recipients: string[];
  sentAt: string;
}

/** 敲門通知。 */
export interface OfficeKnock {
  fromMembershipId: string;
  fromName: string;
  departmentId: string;
  departmentName: string;
  message: string | null;
}

/** 辦公室列表的一列（每個部門一間辦公室）。 */
export interface OfficeRoom {
  departmentId: string;
  parentId: string | null;
  name: string;
  sortOrder: number;
  memberCount: number;
  onlineCount: number;
  occupantCount: number;
}

/** 狀態的顯示文字。 */
export const OFFICE_STATUS_LABELS: Record<OfficeStatus, string> = {
  available: '有空',
  busy: '忙碌',
  meeting: '開會中',
  break: '休息中',
  away: '外出',
};

/** 可用的表情，key 與後端白名單一致。 */
export const OFFICE_EMOTES = [
  { key: 'wave', icon: '👋' },
  { key: 'coffee', icon: '☕' },
  { key: 'thumbsup', icon: '👍' },
  { key: 'party', icon: '🎉' },
  { key: 'question', icon: '❓' },
] as const;

/** 可選的角色造型 key。 */
export const OFFICE_AVATARS = ['a1', 'a2', 'a3', 'a4', 'a5', 'a6', 'a7', 'a8'] as const;

/** 辦公室地板主題。 */
export const OFFICE_THEMES = [
  { value: 'wood', label: '木地板' },
  { value: 'carpet', label: '地毯' },
  { value: 'dark', label: '深色' },
] as const;

/**
 * 取得目前使用者可見的辦公室列表。
 * @returns 各部門辦公室與在線人數。
 */
export const listOfficeRoomsApi = () => apiRequest<OfficeRoom[]>('/api/office/rooms');

/**
 * 取得全公司成員的上線狀態快照（只含在線者；離線者不在清單內）。
 * @returns 上線狀態陣列。
 */
export const getPresenceApi = () => apiRequest<PresenceEntry[]>('/api/office/presence');

/**
 * 更新辦公室地板主題（需要管理權限，由後端判斷）。
 * @param departmentId 部門 ID。
 * @param theme 主題值，須為 OFFICE_THEMES 之一。
 */
export const updateRoomThemeApi = (departmentId: string, theme: string) =>
  apiRequest<void>(`/api/office/rooms/${departmentId}/theme`, { method: 'PUT', body: { theme } });

/** 重連間隔（毫秒）：0、2、5、10 秒，之後每 30 秒一次，不放棄；自動重連與 use-presence 的重新啟動共用。 */
export const RETRY_DELAYS = [0, 2_000, 5_000, 10_000];
/** RETRY_DELAYS 用完之後的固定重試間隔（毫秒）。 */
export const RETRY_FALLBACK_DELAY = 30_000;

let connection: HubConnection | null = null;

/**
 * 取得全後台共用的 hub 連線（多頁靜態站，每次換頁重建，由後端的 15 秒寬限期吸收）。
 * 後端在 access token 到期時會主動關閉連線，所以自動重連加 accessTokenFactory 換發是正常流程。
 * @returns 尚未啟動的單例 HubConnection；啟動由 use-presence 負責。
 */
export function officeConnection(): HubConnection {
  connection ??= new HubConnectionBuilder()
    .withUrl('/api/hubs/office', {
      accessTokenFactory: async () => {
        const token = await ensureFreshAccessToken();
        if (!token) {
          redirectToLogin();
          throw new Error('登入已逾期，請重新登入。');
        }
        return token;
      },
    })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => RETRY_DELAYS[context.previousRetryCount] ?? RETRY_FALLBACK_DELAY })
    .configureLogging(LogLevel.Warning)
    .build();
  return connection;
}

/**
 * 取出 hub 錯誤中給使用者看的訊息。
 * SignalR 把 HubException 包成「An unexpected error occurred invoking 'X' on the server. HubException: 訊息」，只取訊息。
 * @param error invoke 丟出的錯誤。
 * @returns 繁體中文錯誤訊息；不是 HubException 時回傳通用訊息。
 */
export function hubErrorMessage(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const marker = 'HubException: ';
  const index = text.indexOf(marker);
  return index >= 0 ? text.slice(index + marker.length) : '連線發生問題，請稍後再試。';
}
