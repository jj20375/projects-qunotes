<script setup lang="ts">
/**
 * @component ProjectDetailView
 * 專案明細：基本資料、狀態流程、成員與狀態紀錄，以及跟進紀錄、報價單、訂單、出貨單、發票、保固單分頁（層級：定案報價 → 訂單 → 出貨／發票／保固）。
 * 看不到的專案後端回 404，這裡顯示「找不到專案」，不區分是否存在。
 */
import { computed, ref } from 'vue';
import dayjs from 'dayjs';
import { message, Modal } from 'ant-design-vue';
import { useQuery } from '@tanstack/vue-query';
import { ApiError } from '@/shared/api/http-client';
import { changeProjectStatusApi, deleteProjectApi, getProjectApi } from '@/shared/services/business-service';
import { formatDateTime, formatMoney, PROJECT_ROLES, projectStatusColor, projectStatusLabel } from '@/shared/types/business';
import ProjectFormModal from '../../_components/ProjectFormModal.vue';
import ProjectMembersModal from './ProjectMembersModal.vue';
import ProjectQuotesTab from './ProjectQuotesTab.vue';
import ProjectOrdersTab from './ProjectOrdersTab.vue';
import ProjectShipmentsTab from './ProjectShipmentsTab.vue';
import ProjectWarrantiesTab from './ProjectWarrantiesTab.vue';
import ProjectInvoicesTab from './ProjectInvoicesTab.vue';
import ProjectFollowUpsTab from './ProjectFollowUpsTab.vue';
import { followUpDue } from '@/shared/services/followup-service';
import AttachmentList from '@/app/components/AttachmentList.vue';
import { useMe } from '@/app/composables/use-me';

const projectId = new URLSearchParams(window.location.search).get('id') ?? '';
// 總表的連結會帶 tab=followups，直接開到跟進紀錄頁籤。
const activeTab = ref(new URLSearchParams(window.location.search).get('tab') ?? 'members');
const { can } = useMe();
const project = useQuery({ queryKey: ['project', projectId], queryFn: () => getProjectApi(projectId), enabled: !!projectId, retry: false });
const notFound = computed(() => !projectId || (project.error.value instanceof ApiError && project.error.value.status === 404));
const p = computed(() => project.data.value);
const due = computed(() => followUpDue(p.value?.nextFollowUpOn, p.value?.status));

const editOpen = ref(false);
const membersOpen = ref(false);
const statusTarget = ref<string | null>(null);
const statusReason = ref('');
const statusSaving = ref(false);
const reasonRequired = computed(() => statusTarget.value === 'lost' || statusTarget.value === 'cancelled');

function openStatus(target: string) {
  statusTarget.value = target;
  statusReason.value = '';
}

async function submitStatus() {
  if (reasonRequired.value && !statusReason.value.trim()) {
    message.warning('請填寫原因');
    return;
  }
  statusSaving.value = true;
  try {
    await changeProjectStatusApi(projectId, statusTarget.value!, statusReason.value || undefined);
    message.success(`已變更為「${projectStatusLabel(statusTarget.value!)}」`);
    statusTarget.value = null;
    await project.refetch();
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    statusSaving.value = false;
  }
}

function confirmDelete() {
  Modal.confirm({
    title: `刪除專案「${p.value?.name}」？`,
    okType: 'danger',
    onOk: async () => {
      try {
        await deleteProjectApi(projectId);
        window.location.assign('/projects');
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}

const roleLabel = computed(() => (p.value?.myRole ? PROJECT_ROLES[p.value.myRole] : p.value?.canWrite ? '部門範圍' : '檢視'));
</script>

<template>
  <a-result v-if="notFound" status="404" title="找不到專案" sub-title="專案不存在，或不在您的資料範圍內。">
    <template #extra><a-button type="primary" href="/projects">回專案列表</a-button></template>
  </a-result>
  <a-skeleton v-else-if="!p" active />
  <a-space v-else direction="vertical" :size="16" style="width: 100%">
    <a-card :bordered="false">
      <div class="header">
        <div>
          <div class="no">{{ p.projectNo }}</div>
          <h2>
            {{ p.name }}
            <a-tag :color="projectStatusColor(p.status)">{{ projectStatusLabel(p.status) }}</a-tag>
          </h2>
          <div class="sub">
            客戶：{{ p.client.name }} · 負責人：{{ p.owner.displayName }} · 我的身分：{{ roleLabel }}
          </div>
        </div>
        <a-space wrap>
          <a-dropdown v-if="p.nextStatuses.length">
            <a-button type="primary">變更狀態</a-button>
            <template #overlay>
              <a-menu @click="({ key }: { key: string | number }) => openStatus(String(key))">
                <a-menu-item v-for="status in p.nextStatuses" :key="status">{{ projectStatusLabel(status) }}</a-menu-item>
              </a-menu>
            </template>
          </a-dropdown>
          <a-button v-if="p.canWrite && p.status !== 'closed'" @click="editOpen = true">編輯</a-button>
          <a-button v-if="p.canDelete" danger @click="confirmDelete">刪除</a-button>
        </a-space>
      </div>
      <a-alert v-if="!p.canWrite" type="info" show-icon message="您對此專案只有檢視權限。" style="margin: 12px 0 0" />
      <a-descriptions :column="{ xs: 1, sm: 2, lg: 3 }" size="small" class="info">
        <a-descriptions-item label="客戶">{{ p.client.code }} {{ p.client.name }}</a-descriptions-item>
        <a-descriptions-item label="部門">{{ p.departmentName ?? '—' }}</a-descriptions-item>
        <a-descriptions-item label="工期">{{ p.startDate ?? '—' }} ~ {{ p.endDate ?? '—' }}</a-descriptions-item>
        <a-descriptions-item label="預估金額（未稅）">{{ formatMoney(p.expectedAmount) }}</a-descriptions-item>
        <a-descriptions-item label="成交金額（未稅）">{{ formatMoney(p.contractAmount) }}</a-descriptions-item>
        <a-descriptions-item label="成交金額（含稅）">{{ formatMoney(p.contractTotal) }}</a-descriptions-item>
        <a-descriptions-item label="得標時間">{{ formatDateTime(p.wonAt) }}</a-descriptions-item>
        <a-descriptions-item label="建立時間">{{ formatDateTime(p.createdAt) }}</a-descriptions-item>
        <a-descriptions-item label="最後更新">{{ formatDateTime(p.updatedAt) }}</a-descriptions-item>
        <a-descriptions-item v-if="can('followups', 'read')" label="下次聯絡">
          <template v-if="p.nextFollowUpOn">
            {{ p.nextFollowUpOn }}
            <a-tag v-if="due.state === 'overdue'" color="red">逾期 {{ -due.days }} 天</a-tag>
            <a-tag v-else-if="due.state === 'today'" color="orange">今天</a-tag>
          </template>
          <span v-else>—</span>
        </a-descriptions-item>
        <a-descriptions-item v-if="p.summary.orderTotal !== null" label="訂單金額（含稅）">{{ formatMoney(p.summary.orderTotal) }}</a-descriptions-item>
        <a-descriptions-item v-if="p.summary.invoicedTotal !== null" label="已開票 ／ 已收款">
          {{ formatMoney(p.summary.invoicedTotal) }} ／ {{ formatMoney(p.summary.paidTotal) }}
          <a-tag v-if="(p.summary.invoicedTotal ?? 0) > (p.summary.paidTotal ?? 0)" color="orange">未收 {{ formatMoney((p.summary.invoicedTotal ?? 0) - (p.summary.paidTotal ?? 0)) }}</a-tag>
        </a-descriptions-item>
        <a-descriptions-item v-if="p.summary.activeWarranties !== null" label="保固中">{{ p.summary.activeWarranties }} 件</a-descriptions-item>
        <a-descriptions-item v-if="p.description" label="說明" :span="3"><span class="pre">{{ p.description }}</span></a-descriptions-item>
      </a-descriptions>
    </a-card>

    <a-card :bordered="false">
      <a-tabs v-model:active-key="activeTab">
        <a-tab-pane key="members" :tab="`成員（${p.members.length + 1}）`">
          <div class="tab-toolbar">
            <a-button v-if="p.canManageMembers" @click="membersOpen = true">管理成員</a-button>
          </div>
          <a-list :data-source="[{ userId: p.owner.id, displayName: p.owner.displayName, projectRole: 'owner', addedAt: p.createdAt }, ...p.members]">
            <template #renderItem="{ item }">
              <a-list-item>
                <a-list-item-meta :title="item.displayName" :description="item.departmentName" />
                <a-tag :color="item.projectRole === 'owner' ? 'gold' : item.projectRole === 'viewer' ? 'default' : 'blue'">{{ PROJECT_ROLES[item.projectRole] }}</a-tag>
              </a-list-item>
            </template>
          </a-list>
        </a-tab-pane>
        <a-tab-pane key="history" tab="狀態紀錄">
          <a-timeline>
            <a-timeline-item v-for="(entry, index) in p.history" :key="index" :color="index === 0 ? 'blue' : 'gray'">
              <strong>{{ entry.fromStatus ? `${projectStatusLabel(entry.fromStatus)} → ` : '建立為 ' }}{{ projectStatusLabel(entry.toStatus) }}</strong>
              <div class="muted">{{ dayjs(entry.changedAt).format('YYYY-MM-DD HH:mm') }} · {{ entry.changedBy }}</div>
              <div v-if="entry.reason">原因：{{ entry.reason }}</div>
            </a-timeline-item>
          </a-timeline>
        </a-tab-pane>
        <a-tab-pane v-if="can('followups', 'read')" key="followups" tab="跟進紀錄">
          <ProjectFollowUpsTab :project="p" />
        </a-tab-pane>
        <a-tab-pane v-if="can('quotes', 'read')" key="quotes" tab="報價單">
          <ProjectQuotesTab :project="p" />
        </a-tab-pane>
        <a-tab-pane v-if="can('orders', 'read')" key="orders" tab="訂單">
          <ProjectOrdersTab :project="p" @changed="project.refetch()" />
        </a-tab-pane>
        <a-tab-pane v-if="can('orders', 'read')" key="shipments" tab="出貨單">
          <ProjectShipmentsTab :project="p" @changed="project.refetch()" />
        </a-tab-pane>
        <a-tab-pane v-if="can('invoices', 'read')" key="invoices" tab="發票">
          <ProjectInvoicesTab :project="p" @changed="project.refetch()" />
        </a-tab-pane>
        <a-tab-pane v-if="can('warranties', 'read')" key="warranties" tab="保固單">
          <ProjectWarrantiesTab :project="p" @changed="project.refetch()" />
        </a-tab-pane>
        <a-tab-pane key="attachments" tab="專案附件">
          <AttachmentList owner-type="project" :owner-id="p.id" />
        </a-tab-pane>
      </a-tabs>
    </a-card>
  </a-space>

  <template v-if="p">
    <ProjectFormModal :open="editOpen" :project="p" @close="editOpen = false" @saved="project.refetch()" />
    <ProjectMembersModal :open="membersOpen" :project="p" @close="membersOpen = false" @saved="project.refetch()" />
    <a-modal
      :open="!!statusTarget"
      :title="`變更狀態為「${statusTarget ? projectStatusLabel(statusTarget) : ''}」`"
      :confirm-loading="statusSaving"
      ok-text="確認"
      @ok="submitStatus"
      @cancel="statusTarget = null"
    >
      <a-textarea v-model:value="statusReason" :rows="3" :placeholder="reasonRequired ? '請填寫原因（必填）' : '備註（選填）'" />
    </a-modal>
  </template>
</template>

<style scoped>
.header {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}
.no {
  color: #888;
  font-size: 13px;
}
h2 {
  margin: 2px 0 4px;
  font-size: 20px;
}
.sub {
  color: #666;
}
.info {
  margin-top: 16px;
}
.tab-toolbar {
  display: flex;
  justify-content: flex-end;
}
.muted {
  color: #999;
  font-size: 12px;
}
.pre {
  white-space: pre-wrap;
}
</style>
