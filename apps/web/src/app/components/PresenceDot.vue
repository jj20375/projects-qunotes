<script setup lang="ts">
/**
 * @component PresenceDot
 * 成員上線狀態圓點：綠＝上線、黃＝閒置／休息／外出、紅＝忙碌／開會、灰＝離線（附最後在線時間）。
 * 任何列出成員的地方放上這個元件即可顯示上線符號；資料來自 use-presence 的全域連線。
 */
import { computed } from 'vue';
import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import { lastSeenOf, presenceOf } from '@/app/composables/use-presence';
import { OFFICE_STATUS_LABELS } from '@/shared/services/office-service';

dayjs.extend(relativeTime);

const props = defineProps<{ membershipId: string; lastSeenAt?: string | null }>();

const entry = computed(() => presenceOf(props.membershipId));
const tone = computed(() => {
  const value = entry.value;
  if (!value || value.presence === 'offline') return 'offline';
  if (value.status === 'busy' || value.status === 'meeting') return 'busy';
  if (value.presence === 'idle' || value.status === 'break' || value.status === 'away') return 'idle';
  return 'online';
});
const title = computed(() => {
  const value = entry.value;
  if (!value || value.presence === 'offline') {
    const seen = lastSeenOf(props.membershipId) ?? props.lastSeenAt;
    return seen ? `離線 · ${dayjs(seen).fromNow()}在線` : '離線';
  }
  const base = value.presence === 'idle' ? '閒置' : '上線';
  const status = value.status === 'available' ? '' : ` · ${OFFICE_STATUS_LABELS[value.status]}`;
  return `${base}${status}${value.statusText ? `：${value.statusText}` : ''}`;
});
</script>

<template>
  <a-tooltip :title="title">
    <span class="presence-dot" :class="`presence-dot--${tone}`" role="img" :aria-label="title" />
  </a-tooltip>
</template>

<style scoped>
.presence-dot {
  display: inline-block;
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: 50%;
}
.presence-dot--online { background: #52c41a; }
.presence-dot--idle { background: #faad14; }
.presence-dot--busy { background: #ff4d4f; }
.presence-dot--offline { background: #bfbfbf; }
</style>
