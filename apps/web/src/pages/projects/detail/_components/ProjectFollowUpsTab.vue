<script setup lang="ts">
/**
 * @component ProjectFollowUpsTab
 * 專案的跟進紀錄：上方「下次聯絡」卡片（只看最新一筆），下方時間軸。
 * 搜尋、類型篩選與分頁都由後端處理；「載入更多」向 API 取下一頁。
 * 寫入後以後端回傳的專案跟進狀態更新專案明細快取，頂部資訊與卡片一起更新。
 */
import { computed, reactive, ref, type Component } from 'vue';
import dayjs from 'dayjs';
import { message, Modal } from 'ant-design-vue';
import { useInfiniteQuery, useQueryClient } from '@tanstack/vue-query';
import { CarOutlined, FileTextOutlined, MailOutlined, MessageOutlined, PhoneOutlined, TeamOutlined } from '@ant-design/icons-vue';
import type { ProjectDetail } from '@/shared/services/business-service';
import {
  deleteFollowUpApi,
  dismissNextFollowUpApi,
  FOLLOW_UP_TYPES,
  followUpDue,
  followUpTypeLabel,
  listProjectFollowUpsApi,
  type FollowUp,
  type FollowUpProjectState,
} from '@/shared/services/followup-service';
import ProjectFollowUpFormModal from './ProjectFollowUpFormModal.vue';

const props = defineProps<{ project: ProjectDetail }>();
const queryClient = useQueryClient();
const filters = reactive({ keyword: '', type: undefined as string | undefined });
const keywordInput = ref('');

const list = useInfiniteQuery({
  queryKey: ['followups', props.project.id, filters],
  queryFn: ({ pageParam }) =>
    listProjectFollowUpsApi(props.project.id, { keyword: filters.keyword || undefined, type: filters.type, page: pageParam, pageSize: 20 }),
  initialPageParam: 1,
  getNextPageParam: (last) => (last.page * last.pageSize < last.total ? last.page + 1 : undefined),
});
const pages = computed(() => list.data.value?.pages ?? []);
const items = computed(() => pages.value.flatMap((page) => page.items));
const first = computed(() => pages.value[0]);
const due = computed(() => followUpDue(props.project.nextFollowUpOn, props.project.status));
const typeIcons: Record<string, Component> = {
  call: PhoneOutlined,
  visit: CarOutlined,
  meeting: TeamOutlined,
  email: MailOutlined,
  message: MessageOutlined,
  other: FileTextOutlined,
};

async function applyState(state: FollowUpProjectState) {
  queryClient.setQueryData<ProjectDetail>(['project', props.project.id], (old) => (old ? { ...old, ...state } : old));
  await queryClient.invalidateQueries({ queryKey: ['followups', props.project.id] });
  // 下次聯絡日變了，頁首鈴鐺的通知與首頁的待辦也跟著變；不重新整理的話要等下一輪輪詢才會更新。
  await queryClient.invalidateQueries({ queryKey: ['notifications'] });
  await queryClient.invalidateQueries({ queryKey: ['followup-todo'] });
}

const formOpen = ref(false);
const editing = ref<FollowUp | null>(null);
function open(row: FollowUp | null) {
  editing.value = row;
  formOpen.value = true;
}

function dismiss() {
  Modal.confirm({
    title: '不用再跟進這個專案？',
    content: '會清掉目前的下次聯絡日；之後寫新的紀錄可以再排定。',
    onOk: async () => {
      try {
        await applyState((await dismissNextFollowUpApi(props.project.id)).project);
        message.success('已清除下次聯絡');
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}

function remove(row: FollowUp) {
  Modal.confirm({
    title: `刪除「${row.summary}」？`,
    okType: 'danger',
    onOk: async () => {
      try {
        await applyState((await deleteFollowUpApi(row.id)).project);
        message.success('已刪除');
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}

function search() {
  filters.keyword = keywordInput.value.trim();
}
</script>

<template>
  <a-space direction="vertical" :size="16" style="width: 100%">
    <a-card size="small" :class="['next', due.state]">
      <template v-if="project.nextFollowUpOn">
        <div class="next-head">
          <span class="muted">下次聯絡</span>
          <strong>{{ project.nextFollowUpOn }}</strong>
          <a-tag v-if="due.state === 'overdue'" color="red">逾期 {{ -due.days }} 天</a-tag>
          <a-tag v-else-if="due.state === 'today'" color="orange">今天</a-tag>
          <a-tag v-else-if="due.state === 'inactive'">專案已停止，不列入待跟進</a-tag>
          <a-button v-if="first?.canUpdate" size="small" class="push" @click="dismiss">不用跟進</a-button>
        </div>
        <div v-if="project.nextAction" class="next-action">{{ project.nextAction }}</div>
      </template>
      <span v-else class="muted">目前沒有排定下次聯絡</span>
    </a-card>

    <div class="toolbar">
      <a-space wrap>
        <a-input-search v-model:value="keywordInput" placeholder="主旨、內容、對象" allow-clear style="width: 240px" @search="search" />
        <a-select v-model:value="filters.type" :options="FOLLOW_UP_TYPES" allow-clear placeholder="全部類型" style="width: 120px" />
      </a-space>
      <a-button v-if="first?.canCreate" type="primary" @click="open(null)">新增紀錄</a-button>
    </div>

    <a-skeleton v-if="list.isLoading.value" active />
    <a-alert v-else-if="list.isError.value" type="error" show-icon :message="(list.error.value as Error | null)?.message || '跟進紀錄載入失敗'">
      <template #action>
        <a-button size="small" @click="list.refetch()">重新載入</a-button>
      </template>
    </a-alert>
    <a-empty v-else-if="items.length === 0" description="還沒有跟進紀錄" />
    <a-timeline v-else>
      <a-timeline-item v-for="row in items" :key="row.id">
        <template #dot><component :is="typeIcons[row.type] ?? FileTextOutlined" /></template>
        <div class="row-head">
          <a-tag>{{ followUpTypeLabel(row.type) }}</a-tag>
          <span>{{ dayjs(row.occurredAt).format('YYYY-MM-DD HH:mm') }}</span>
          <span v-if="row.contactName" class="muted">· {{ row.contactName }}</span>
          <span class="muted">· {{ row.createdBy.displayName ?? '—' }}</span>
          <a-space v-if="first?.canUpdate || first?.canDelete" class="push" :size="0">
            <a-button v-if="first?.canUpdate" type="link" size="small" @click="open(row)">編輯</a-button>
            <a-button v-if="first?.canDelete" type="link" size="small" danger @click="remove(row)">刪除</a-button>
          </a-space>
        </div>
        <div class="summary">{{ row.summary }}</div>
        <div v-if="row.content" class="content">{{ row.content }}</div>
        <div v-if="row.nextFollowUpOn" class="muted">
          下次聯絡：{{ row.nextFollowUpOn }}<template v-if="row.nextAction">　{{ row.nextAction }}</template>
          <template v-if="row.nextDismissedAt">（已設為不用跟進）</template>
        </div>
        <div v-if="row.updatedBy" class="muted">由 {{ row.updatedBy.displayName ?? '—' }} 修改於 {{ dayjs(row.updatedAt).format('YYYY-MM-DD HH:mm') }}</div>
      </a-timeline-item>
    </a-timeline>
    <div v-if="list.hasNextPage.value" class="more">
      <a-button :loading="list.isFetchingNextPage.value" @click="list.fetchNextPage()">載入更多（共 {{ first?.total }} 筆）</a-button>
    </div>
  </a-space>

  <ProjectFollowUpFormModal :open="formOpen" :project-id="project.id" :record="editing" @close="formOpen = false" @saved="applyState" />
</template>

<style scoped>
.next.overdue {
  border-color: #ffa39e;
  background: #fff1f0;
}
.next.today {
  border-color: #ffd591;
  background: #fff7e6;
}
.next-head,
.row-head {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.next-action {
  margin-top: 4px;
}
.push {
  margin-left: auto;
}
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}
.summary {
  font-weight: 500;
  margin-top: 4px;
}
.content {
  white-space: pre-wrap;
  margin-top: 2px;
}
.muted {
  color: #888;
}
.more {
  text-align: center;
}
</style>
