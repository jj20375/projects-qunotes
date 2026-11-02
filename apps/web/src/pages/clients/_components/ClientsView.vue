<script setup lang="ts">
/**
 * @component ClientsView
 * 客戶列表：關鍵字（名稱／代碼／簡稱／統編）、狀態篩選與伺服器端分頁；點名稱開啟客戶資料。
 */
import { computed, reactive, ref } from 'vue';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { useMe } from '@/app/composables/use-me';
import { searchClientsApi, type ClientRow } from '@/shared/services/business-service';
import { dataColumns } from '@/shared/table/data-table';
import ClientFormModal from './ClientFormModal.vue';
import ClientImportModal from './ClientImportModal.vue';
import { message } from 'ant-design-vue';
import { downloadFile } from '@/shared/utils/download';

const { can, canOption } = useMe();
const importOpen = ref(false);

async function exportClients() {
  try {
    await downloadFile('/api/clients/export', '客戶清單.xlsx');
  } catch (error) {
    message.error((error as Error).message);
  }
}
const queryClient = useQueryClient();
const filters = reactive({ keyword: '', status: 'active', page: 1, pageSize: 20 });
const applied = reactive({ ...filters });
const clients = useQuery({
  queryKey: ['clients', applied],
  queryFn: () => searchClientsApi({ keyword: applied.keyword, status: applied.status || undefined, page: applied.page, pageSize: applied.pageSize }),
  placeholderData: keepPreviousData,
});

const modalOpen = ref(false);
const editingId = ref<string | null>(null);

function applySearch() {
  Object.assign(applied, { ...filters, page: 1 });
}

function openClient(id: string | null) {
  editingId.value = id;
  modalOpen.value = true;
}

const pagination = computed(() => ({
  page: applied.page,
  pageSize: applied.pageSize,
  total: clients.data.value?.total ?? 0,
  showSizeChanger: true,
  showTotal: (total: number) => `共 ${total} 筆`,
}));

function onTableChange(change: TableChange) {
  applied.page = change.page;
  applied.pageSize = change.pageSize;
}

const col = dataColumns<ClientRow>();
const columns = [
  col.accessor('code', { header: '代碼', meta: { width: 100 } }),
  col.accessor('name', { header: '客戶名稱' }),
  col.accessor('taxId', { header: '統編', meta: { width: 110 } }),
  col.accessor('primaryContact', { header: '主要聯絡人', meta: { width: 120 } }),
  col.accessor('phone', { header: '電話', meta: { width: 140 } }),
  col.accessor('departmentName', { header: '所屬部門', meta: { width: 120 } }),
  col.accessor('ownerName', { header: '業務負責人', meta: { width: 120 } }),
  col.accessor('projectCount', { header: '專案數', meta: { width: 80 } }),
  col.accessor('status', { header: '狀態', meta: { width: 80 } }),
];
</script>

<template>
  <a-card :bordered="false">
    <div class="toolbar">
      <a-space wrap>
        <a-input-search v-model:value="filters.keyword" placeholder="名稱、代碼、統編" allow-clear style="width: 240px" @search="applySearch" />
        <a-select
          v-model:value="filters.status"
          style="width: 120px"
          :options="[{ value: 'active', label: '往來中' }, { value: 'inactive', label: '停用' }, { value: '', label: '全部' }]"
          @change="applySearch"
        />
      </a-space>
      <a-space>
        <a-button v-if="canOption('clients', 'export')" @click="exportClients">匯出 Excel</a-button>
        <a-button v-if="canOption('clients', 'import')" @click="importOpen = true">匯入 Excel</a-button>
        <a-button v-if="can('clients', 'create')" type="primary" @click="openClient(null)">新增客戶</a-button>
      </a-space>
    </div>
    <DataTable :data="clients.data.value?.items ?? []" :columns="columns" row-key="id" :loading="clients.isFetching.value" :pagination="pagination" :scroll-x="1020" @change="onTableChange">
      <template #cell="{ columnId, record }">
        <template v-if="columnId === 'name'">
          <a @click="openClient(record.id)">{{ record.name }}</a>
          <span v-if="record.shortName" class="muted">（{{ record.shortName }}）</span>
        </template>
        <template v-else-if="columnId === 'status'">
          <a-tag :color="record.status === 'active' ? 'green' : 'default'">{{ record.status === 'active' ? '往來中' : '停用' }}</a-tag>
        </template>
        <template v-else-if="columnId === 'projectCount'">
          <a v-if="record.projectCount" :href="`/projects?clientId=${record.id}`">{{ record.projectCount }}</a>
          <span v-else class="muted">0</span>
        </template>
      </template>
    </DataTable>
  </a-card>

  <ClientImportModal :open="importOpen" @close="importOpen = false" @imported="queryClient.invalidateQueries({ queryKey: ['clients'] })" />
  <ClientFormModal :open="modalOpen" :client-id="editingId" @close="modalOpen = false" @saved="queryClient.invalidateQueries({ queryKey: ['clients'] })" />
</template>

<style scoped>
.toolbar {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}
.muted {
  color: #999;
}
</style>
