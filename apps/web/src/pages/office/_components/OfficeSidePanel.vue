<script setup lang="ts">
/**
 * @component OfficeSidePanel
 * 辦公室右側面板：目前在場的人、部門成員（上線狀態與敲門）、靠近聊天。
 * 「會傳給誰」只是提示，實際收訊對象由後端依位置決定。
 */
import { computed, ref } from 'vue';
import dayjs from 'dayjs';
import PresenceDot from '@/app/components/PresenceDot.vue';
import { presenceOf } from '@/app/composables/use-presence';
import { OFFICE_STATUS_LABELS, type OfficeChatMessage, type OfficeOccupant, type OfficeSeat } from '@/shared/services/office-service';

const props = defineProps<{
  departmentId: string | undefined;
  seats: OfficeSeat[];
  occupants: OfficeOccupant[];
  meId: string;
  recipients: string[];
  messages: OfficeChatMessage[];
  /** 送出聊天；回傳 false 時保留輸入內容。 */
  send: (text: string) => Promise<boolean>;
}>();
const emit = defineEmits<{ knock: [membershipId: string] }>();

const draft = ref('');
const sending = ref(false);
const present = computed(() => [...props.occupants].sort((a, b) => a.name.localeCompare(b.name, 'zh-Hant')));
const nameOf = (id: string) => props.occupants.find((value) => value.membershipId === id)?.name ?? props.seats.find((value) => value.membershipId === id)?.name ?? '同事';
const recipientHint = computed(() => (props.recipients.length ? `會傳給：${props.recipients.map(nameOf).join('、')}` : '附近沒有人，走近同事就能聊天'));
const canKnock = (id: string) => id !== props.meId && presenceOf(id) !== undefined;

async function submit() {
  const text = draft.value.trim();
  if (!text || sending.value) return;
  sending.value = true;
  try {
    if (await props.send(text)) draft.value = '';
  } finally {
    sending.value = false;
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
    event.preventDefault();
    void submit();
  }
}
</script>

<template>
  <aside class="side">
    <section>
      <h4>在這裡（{{ present.length }}）</h4>
      <div v-for="occupant in present" :key="occupant.membershipId" class="row">
        <PresenceDot :membership-id="occupant.membershipId" />
        <span class="name">{{ occupant.name }}</span>
        <a-tag v-if="occupant.homeDepartmentId !== departmentId" color="blue">訪客</a-tag>
        <span v-if="occupant.status !== 'available'" class="muted">{{ OFFICE_STATUS_LABELS[occupant.status] }}</span>
      </div>
    </section>
    <section>
      <h4>部門成員（{{ seats.length }}）</h4>
      <div v-for="seat in seats" :key="seat.membershipId" class="row">
        <PresenceDot :membership-id="seat.membershipId" :last-seen-at="seat.lastSeenAt" />
        <span class="name">{{ seat.name }}</span>
        <a-button v-if="canKnock(seat.membershipId)" size="small" type="link" @click="emit('knock', seat.membershipId)">敲門</a-button>
      </div>
    </section>
    <section class="chat">
      <h4>附近聊天</h4>
      <div class="messages">
        <p v-if="!messages.length" class="muted">走到同事旁邊或進入會議室就能聊天，訊息不會保存。</p>
        <div v-for="(message, index) in messages" :key="index" class="message">
          <span class="muted">{{ dayjs(message.sentAt).format('HH:mm') }}</span>
          <strong>{{ message.fromName }}</strong>
          <span>{{ message.text }}</span>
        </div>
      </div>
      <p class="muted hint">{{ recipientHint }}</p>
      <a-textarea v-model:value="draft" :auto-size="{ minRows: 1, maxRows: 4 }" :maxlength="500" placeholder="輸入訊息，Enter 送出" @keydown="onKeydown" />
      <a-button type="primary" block :loading="sending" :disabled="!draft.trim()" @click="submit">送出</a-button>
    </section>
  </aside>
</template>

<style scoped>
.side { display: flex; flex-direction: column; gap: 12px; width: 280px; min-width: 0; overflow-y: auto; }
.side h4 { margin: 0 0 6px; font-size: 13px; }
.row { display: flex; align-items: center; gap: 6px; min-height: 28px; }
.name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.muted { color: rgba(0, 0, 0, 0.45); font-size: 12px; }
.chat { display: flex; flex: 1; flex-direction: column; gap: 6px; min-height: 220px; }
.messages { flex: 1; overflow-y: auto; }
.message { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 4px; word-break: break-word; }
.hint { margin: 0; }
@media (max-width: 960px) { .side { width: 100%; } }
</style>
