<script setup lang="ts">
/**
 * @component NotificationBell
 * 頁首的通知鈴鐺：未讀則數、最近的跟進到期通知，點通知會標為已讀並開啟該專案的跟進紀錄。
 * 未讀則數每分鐘自動更新；打開面板時再更新一次。
 */
import { computed, ref, watch } from 'vue';
import { BellOutlined } from '@ant-design/icons-vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { followUpDue } from '@/shared/services/followup-service';
import { listNotificationsApi, markAllNotificationsReadApi, markNotificationReadApi, type NotificationItem } from '@/shared/services/notification-service';
import { withBasePath } from '@/shared/utils/base-path';

const queryClient = useQueryClient();
const open = ref(false);
const page = ref(1);
const pageSize = 8;

const list = useQuery({
  queryKey: ['notifications', page],
  queryFn: () => listNotificationsApi({ page: page.value, pageSize }),
  placeholderData: keepPreviousData,
  refetchInterval: 60_000,
});
const unread = computed(() => list.data.value?.unreadCount ?? 0);
const total = computed(() => list.data.value?.total ?? 0);

watch(open, (value) => {
  if (!value) return;
  page.value = 1;
  list.refetch();
});

const refresh = () => queryClient.invalidateQueries({ queryKey: ['notifications'] });
const markRead = useMutation({ mutationFn: markNotificationReadApi, onSuccess: refresh });
const markAll = useMutation({ mutationFn: markAllNotificationsReadApi, onSuccess: refresh });

/** 通知的狀態文字：到期日與今天的距離。 */
function statusText(item: NotificationItem) {
  const due = followUpDue(item.dueOn, item.project?.status);
  if (due.state === 'overdue') return `已逾期 ${-due.days} 天`;
  return due.state === 'today' ? '今天到期' : `${due.days} 天後`;
}

function openItem(item: NotificationItem) {
  if (!item.readAt) markRead.mutate(item.id);
  window.location.assign(withBasePath(`/projects/detail?id=${item.projectId}&tab=followups`));
}
</script>

<template>
  <a-popover v-model:open="open" trigger="click" placement="bottomRight" overlay-class-name="notification-popover">
    <a-badge :count="unread" :overflow-count="99" :offset="[-4, 4]">
      <a-button type="text" shape="circle" aria-label="通知"><BellOutlined style="font-size: 18px" /></a-button>
    </a-badge>
    <template #title>
      <div class="head">
        <span>通知</span>
        <a-button type="link" size="small" :disabled="!unread" :loading="markAll.isPending.value" @click="markAll.mutate()">全部標為已讀</a-button>
      </div>
    </template>
    <template #content>
      <a-empty v-if="!list.data.value?.items.length" description="沒有通知" :image-style="{ height: '48px' }" />
      <ul v-else class="items">
        <li v-for="item in list.data.value.items" :key="item.id" :class="{ read: item.readAt }" @click="openItem(item)">
          <span v-if="!item.readAt" class="dot" />
          <div class="body">
            <div class="title">跟進到期：{{ item.project?.projectNo }} {{ item.project?.name }}</div>
            <div class="meta">
              {{ item.project?.clientName }} ・ 下次聯絡 {{ item.dueOn }}
              <a-tag color="red" class="tag">{{ statusText(item) }}</a-tag>
            </div>
          </div>
        </li>
      </ul>
      <a-pagination v-if="total > pageSize" v-model:current="page" :total="total" :page-size="pageSize" size="small" simple class="pager" />
    </template>
  </a-popover>
</template>

<style>
.notification-popover .ant-popover-inner-content {
  width: 380px;
  max-width: calc(100vw - 32px);
}
</style>

<style scoped>
.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.items {
  margin: 0;
  padding: 0;
  list-style: none;
}
.items li {
  display: flex;
  gap: 8px;
  padding: 10px 8px;
  border-bottom: 1px solid #f0f0f0;
  cursor: pointer;
}
.items li:hover {
  background: #fafafa;
}
.items li.read .title {
  font-weight: 400;
  color: #666;
}
.dot {
  flex: none;
  width: 8px;
  height: 8px;
  margin-top: 7px;
  border-radius: 50%;
  background: #1677ff;
}
.items li.read {
  padding-left: 24px;
}
.title {
  font-weight: 600;
  line-height: 1.5;
}
.meta {
  margin-top: 2px;
  color: #888;
  font-size: 12px;
}
.tag {
  margin-left: 6px;
}
.pager {
  margin-top: 8px;
  text-align: center;
}
</style>
