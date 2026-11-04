<script setup lang="ts">
/**
 * @component OfficeView
 * 部門虛擬辦公室：上方工具列（部門切換、狀態、表情、角色、主題），左側 Phaser 地圖，右側成員與聊天。
 * 所有互動都經過全域 hub 連線（use-presence）；後端驗證後才廣播，這裡只送意圖並依事件更新畫面。
 */
import { computed, onBeforeUnmount, onMounted, reactive, ref, shallowRef } from 'vue';
import { message } from 'ant-design-vue';
import { useMe } from '@/app/composables/use-me';
import { answerKnock, connectionState, onOfficeReconnected, setOfficeKnockHandler, waitForOfficeConnection } from '@/app/composables/use-presence';
import {
  hubErrorMessage,
  listOfficeRoomsApi,
  officeConnection,
  updateRoomThemeApi,
  OFFICE_AVATARS,
  OFFICE_EMOTES,
  OFFICE_STATUS_LABELS,
  OFFICE_THEMES,
  type Facing,
  type OfficeChatMessage,
  type OfficeKnock,
  type OfficeOccupant,
  type OfficeRoom,
  type OfficeSnapshot,
  type OfficeStatus,
  type PresenceEntry,
} from '@/shared/services/office-service';
import { AVATAR_COLORS } from './office-textures';
import { chatRecipients, zoneCenter } from './office-grid';
import type { OfficeScene } from './office-scene';
import OfficeSidePanel from './OfficeSidePanel.vue';

const { me, can } = useMe();
const meId = me.value!.scope.membershipId!;
const hub = officeConnection();
const canvasHost = ref<HTMLDivElement>();
const rooms = ref<OfficeRoom[]>([]);
const departmentId = ref<string>();
const snapshot = shallowRef<OfficeSnapshot | null>(null);
const occupants = reactive(new Map<string, OfficeOccupant>());
const messages = ref<OfficeChatMessage[]>([]);
const status = ref<OfficeStatus>('available');
const statusText = ref('');
const savedStatusText = ref('');
const replaced = ref(false);
const joining = ref(false);
const canEditTheme = computed(() => can('company.departments', 'update'));
let scene: OfficeScene | null = null;
let game: { destroy(removeCanvas: boolean): void } | null = null;
let stopReconnected: (() => void) | null = null;

const statusOptions = Object.entries(OFFICE_STATUS_LABELS).map(([value, label]) => ({ value, label }));
const themeOptions = OFFICE_THEMES.map((theme) => ({ value: theme.value, label: theme.label }));
const avatarColor = (key: string) => `#${(AVATAR_COLORS[key] ?? 0x999999).toString(16).padStart(6, '0')}`;

// 部門樹已完整載入（小型資料），這裡只依層級縮排顯示。
const roomOptions = computed(() => {
  const byId = new Map(rooms.value.map((room) => [room.departmentId, room]));
  const depth = (room: OfficeRoom): number => (room.parentId && byId.has(room.parentId) ? depth(byId.get(room.parentId)!) + 1 : 0);
  return rooms.value.map((room) => ({ value: room.departmentId, label: `${'　'.repeat(depth(room))}${room.name}（${room.onlineCount} 人在線）` }));
});

const occupantList = computed(() => [...occupants.values()]);
const recipients = computed(() => {
  const map = snapshot.value?.map;
  if (!map) return [];
  return chatRecipients(map, meId, new Map(occupantList.value.map((occupant) => [occupant.membershipId, { x: occupant.x, y: occupant.y }])));
});

function updateOccupant(membershipId: string, patch: Partial<OfficeOccupant>) {
  const current = occupants.get(membershipId);
  if (current) occupants.set(membershipId, { ...current, ...patch });
}

const subscriptions: [string, (...args: any[]) => void][] = [
  ['OccupantJoined', (occupant: OfficeOccupant) => { occupants.set(occupant.membershipId, occupant); scene?.upsertOccupant(occupant); }],
  ['OccupantLeft', (id: string) => { occupants.delete(id); scene?.removeOccupant(id); }],
  ['Moved', (id: string, x: number, y: number, facing: Facing) => { updateOccupant(id, { x, y, facing }); scene?.moveOccupant(id, x, y, facing, id !== meId); }],
  ['PositionCorrected', (x: number, y: number, facing: Facing) => { updateOccupant(meId, { x, y, facing }); scene?.moveOccupant(meId, x, y, facing, false); }],
  ['Emoted', (id: string, key: string) => scene?.showEmote(id, OFFICE_EMOTES.find((emote) => emote.key === key)?.icon ?? '❔')],
  ['AvatarChanged', (id: string, key: string) => { updateOccupant(id, { avatarKey: key }); scene?.setAvatar(id, key); }],
  ['ChatReceived', (chat: OfficeChatMessage) => { messages.value = [...messages.value.slice(-99), chat]; scene?.showBubble(chat.fromMembershipId, chat.text); }],
  ['KnockAnswered', (_id: string, name: string, accepted: boolean, reply: string | null) => (accepted ? message.success(`${name} 正在過來`) : message.info(`${name}：${reply || '稍後再過去'}`))],
  ['OfficeSessionReplaced', () => { replaced.value = true; }],
  ['PresenceChanged', (entry: PresenceEntry) => { updateOccupant(entry.membershipId, { status: entry.status, statusText: entry.statusText }); scene?.setStatus(entry.membershipId, entry.status); }],
];

const wakeScene = () => scene?.wake();

// 下拉選單選完後焦點會留在它的搜尋輸入框，場景會把它當成正在打字而忽略方向鍵。
const releaseFocus = () => (document.activeElement as HTMLElement | null)?.blur();

onMounted(async () => {
  const params = new URLSearchParams(window.location.search);
  const answer = params.get('answer');
  if (params.has('dept') || answer) history.replaceState(null, '', '/office');
  rooms.value = await listOfficeRoomsApi();
  const wanted = params.get('dept') ?? me.value!.scope.departmentId ?? rooms.value[0]?.departmentId;
  const { createOfficeGame } = await import('./office-scene');
  const created = await createOfficeGame(canvasHost.value!, { step: onStep }, () => message.warning('部分素材載入失敗，已改用簡易圖案。'));
  scene = created.scene;
  game = created.game;
  subscriptions.forEach(([name, handler]) => hub.on(name, handler));
  setOfficeKnockHandler(goToKnock);
  window.addEventListener('keydown', wakeScene);
  window.addEventListener('pointerdown', wakeScene);
  await waitForOfficeConnection();
  if (wanted) await join(wanted);
  // 重連處理器在首次 join 之後才註冊：waitForOfficeConnection 在重連時也會放行等待者，
  // 先註冊的話首次載入遇到重連會 JoinOffice 兩次。
  stopReconnected = onOfficeReconnected(() => {
    if (departmentId.value) void join(departmentId.value);
  });
  if (answer) await respond(answer);
});

onBeforeUnmount(() => {
  void hub.send('LeaveOffice').catch(() => undefined);
  subscriptions.forEach(([name, handler]) => hub.off(name, handler));
  setOfficeKnockHandler(null);
  stopReconnected?.();
  window.removeEventListener('keydown', wakeScene);
  window.removeEventListener('pointerdown', wakeScene);
  game?.destroy(true);
});

const localThemeKey = (deptId: string) => `quote:office-theme:${deptId}`;

// 瀏覽器封鎖網站資料時，連存取 window.localStorage 都會丟 SecurityError；
// 此時 JoinOffice 已成功，不能讓主題偏好的讀寫失敗拖垮整個辦公室載入。
function readLocalTheme(deptId: string): string | null {
  try {
    const saved = localStorage.getItem(localThemeKey(deptId));
    return saved && OFFICE_THEMES.some((item) => item.value === saved) ? saved : null;
  } catch {
    return null;
  }
}

function writeLocalTheme(deptId: string, theme: string) {
  try {
    localStorage.setItem(localThemeKey(deptId), theme);
  } catch {
    // 儲存被封鎖時，本次仍會套用在目前畫面，只是重新整理後不會記住。
  }
}

async function join(target: string) {
  joining.value = true;
  try {
    const result = await hub.invoke<OfficeSnapshot>('JoinOffice', target);
    const savedLocalTheme = canEditTheme.value ? null : readLocalTheme(target);
    if (savedLocalTheme) result.map.theme = savedLocalTheme;
    if (target !== departmentId.value) messages.value = [];
    departmentId.value = target;
    snapshot.value = result;
    replaced.value = false;
    occupants.clear();
    result.occupants.forEach((occupant) => occupants.set(occupant.membershipId, occupant));
    status.value = result.me.status;
    statusText.value = savedStatusText.value = result.me.statusText ?? '';
    scene?.loadOffice(result, meId);
    rooms.value = await listOfficeRoomsApi();
  } catch (error) {
    message.error(hubErrorMessage(error));
  } finally {
    joining.value = false;
  }
}

function onStep(x: number, y: number, facing: Facing) {
  updateOccupant(meId, { x, y, facing });
  void hub.send('Move', x, y, facing).catch(() => undefined);
}

async function invoke(method: string, ...args: unknown[]): Promise<boolean> {
  try {
    await hub.invoke(method, ...args);
    return true;
  } catch (error) {
    message.warning(hubErrorMessage(error));
    return false;
  }
}

async function changeStatus(next: OfficeStatus) {
  if (!(await invoke('SetStatus', next, statusText.value.trim() || null))) return;
  status.value = next;
  savedStatusText.value = statusText.value.trim();
  const map = snapshot.value?.map;
  const zoneType = next === 'meeting' ? 'meeting' : next === 'break' ? 'pantry' : null;
  const zone = map && zoneType ? map.zones.find((value) => value.type === zoneType) : undefined;
  if (map && zone) scene?.walkTo(zoneCenter(map, zone));
}

function saveStatusText() {
  if (statusText.value.trim() !== savedStatusText.value) void changeStatus(status.value);
}

const sendChat = (text: string) => invoke('SendChat', text);
const emote = (key: string) => void invoke('Emote', key);
const goToSeat = () => void invoke('GoToSeat');
const setAvatar = (key: string) => void invoke('SetAvatar', key);

async function knock(membershipId: string) {
  if (await invoke('Knock', membershipId, null)) message.success('已敲門，等對方回應');
}

async function respond(from: string) {
  try {
    await answerKnock(from, true);
  } catch (error) {
    message.error(hubErrorMessage(error));
  }
}

async function goToKnock(knockInfo: OfficeKnock) {
  if (knockInfo.departmentId !== departmentId.value) await join(knockInfo.departmentId);
  await respond(knockInfo.fromMembershipId);
}

async function changeTheme(theme: string) {
  if (!departmentId.value || !snapshot.value) return;
  if (!canEditTheme.value) {
    writeLocalTheme(departmentId.value, theme);
    snapshot.value = { ...snapshot.value, map: { ...snapshot.value.map, theme } };
    scene?.loadOffice({ ...snapshot.value, occupants: occupantList.value }, meId);
    return;
  }
  try {
    await updateRoomThemeApi(departmentId.value, theme);
    await join(departmentId.value);
  } catch (error) {
    message.error((error as Error).message);
  }
}
</script>

<template>
  <div class="office">
    <div class="toolbar">
      <a-select :value="departmentId" :options="roomOptions" :loading="joining" style="width: 240px" @change="(value: unknown) => { releaseFocus(); void join(String(value)); }" />
      <a-select :value="status" :options="statusOptions" style="width: 110px" @change="(value: unknown) => { releaseFocus(); void changeStatus(value as OfficeStatus); }" />
      <a-input v-model:value="statusText" :maxlength="40" placeholder="狀態說明（選填）" style="width: 180px" @press-enter="saveStatusText" @blur="saveStatusText" />
      <a-space :size="4">
        <a-button v-for="item in OFFICE_EMOTES" :key="item.key" size="small" @click="emote(item.key)">{{ item.icon }}</a-button>
      </a-space>
      <a-button @click="goToSeat">回到座位</a-button>
      <a-popover trigger="click" title="選擇角色">
        <template #content>
          <a-space wrap>
            <a-button v-for="key in OFFICE_AVATARS" :key="key" shape="circle" :style="{ background: avatarColor(key) }" @click="setAvatar(key)" />
          </a-space>
        </template>
        <a-button>角色</a-button>
      </a-popover>
      <a-tooltip v-if="snapshot" :title="canEditTheme ? undefined : '只套用在你自己的畫面'">
        <a-select :value="snapshot.map.theme" :options="themeOptions" style="width: 130px" @change="(value: unknown) => { releaseFocus(); void changeTheme(String(value)); }" />
      </a-tooltip>
    </div>
    <a-alert v-if="connectionState === 'reconnecting' || connectionState === 'disconnected'" type="warning" show-icon banner message="連線中斷，重新連線中…" />
    <div class="body">
      <div ref="canvasHost" class="canvas">
        <div v-if="replaced" class="overlay">
          <p>辦公室已在其他分頁開啟。</p>
          <a-button type="primary" @click="departmentId && join(departmentId)">在這裡繼續</a-button>
        </div>
      </div>
      <OfficeSidePanel
        :department-id="departmentId"
        :seats="snapshot?.map.seats ?? []"
        :occupants="occupantList"
        :me-id="meId"
        :recipients="recipients"
        :messages="messages"
        :send="sendChat"
        @knock="knock"
      />
    </div>
  </div>
</template>

<style scoped>
.office { display: flex; flex-direction: column; gap: 8px; height: calc(100vh - 140px); min-height: 480px; }
.toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.body { display: flex; flex: 1; gap: 12px; min-height: 0; }
.canvas { position: relative; flex: 1; min-width: 0; overflow: hidden; border-radius: 6px; background: #1b1f24; }
.overlay { position: absolute; inset: 0; z-index: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; color: #fff; background: rgba(0, 0, 0, 0.6); }
@media (max-width: 960px) {
  .office { height: auto; }
  .body { flex-direction: column; }
  .canvas { flex: none; height: 60vh; }
}
</style>
