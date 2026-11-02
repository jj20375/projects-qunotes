<script setup lang="ts">
/**
 * @component CompaniesView
 * 平台的公司列表、開通（同時建立第一位公司管理員）與停用／權限細項設定。
 * 搜尋與分頁由後端處理（GET /api/platform/companies?keyword=&page=&pageSize=）。
 */
import { reactive, ref } from 'vue';
import { message } from 'ant-design-vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { useMe } from '@/app/composables/use-me';
import { createCompanyApi, listCompaniesApi, updateCompanyApi, type CompanyRow } from '@/shared/services/admin-service';
import { dataColumns } from '@/shared/table/data-table';

const { can } = useMe();
const queryClient = useQueryClient();
const keywordInput = ref('');
const applied = reactive({ keyword: '', page: 1, pageSize: 20 });
const companies = useQuery({ queryKey: ['companies', applied], queryFn: () => listCompaniesApi({ ...applied }), placeholderData: keepPreviousData });

function search() {
  applied.keyword = keywordInput.value.trim();
  applied.page = 1;
}

function onTableChange(change: TableChange) {
  applied.page = change.page;
  applied.pageSize = change.pageSize;
}

const createOpen = ref(false);
const createForm = reactive({ name: '', slug: '', taxId: '', phone: '', address: '', adminAccount: '', adminDisplayName: '', adminEmail: '', adminPassword: '' });
const editOpen = ref(false);
const editing = ref<CompanyRow | null>(null);
const editForm = reactive({ name: '', taxId: '', phone: '', address: '', status: 'active', isRoleCrudConfigurationEnabled: true, isRoleOptionConfigurationEnabled: true });

const refresh = () => queryClient.invalidateQueries({ queryKey: ['companies'] });

const createMutation = useMutation({
  mutationFn: () => createCompanyApi({ ...createForm }),
  onSuccess: () => {
    message.success('公司已開通');
    createOpen.value = false;
    Object.assign(createForm, { name: '', slug: '', taxId: '', phone: '', address: '', adminAccount: '', adminDisplayName: '', adminEmail: '', adminPassword: '' });
    refresh();
  },
  onError: (error: Error) => message.error(error.message),
});

const updateMutation = useMutation({
  mutationFn: () => updateCompanyApi(editing.value!.id, { ...editForm }),
  onSuccess: () => {
    message.success('已儲存');
    editOpen.value = false;
    refresh();
  },
  onError: (error: Error) => message.error(error.message),
});

function openEdit(row: CompanyRow) {
  editing.value = row;
  Object.assign(editForm, {
    name: row.name,
    taxId: row.taxId ?? '',
    phone: row.phone ?? '',
    address: row.address ?? '',
    status: row.status,
    isRoleCrudConfigurationEnabled: row.isRoleCrudConfigurationEnabled,
    isRoleOptionConfigurationEnabled: row.isRoleOptionConfigurationEnabled,
  });
  editOpen.value = true;
}

const col = dataColumns<CompanyRow>();
const columns = [
  col.accessor('name', { header: '公司名稱' }),
  col.accessor('slug', { header: '代碼' }),
  col.accessor('taxId', { header: '統編' }),
  col.accessor('rootAdminAccount', { header: '主管理員' }),
  col.accessor('memberCount', { header: '成員數', meta: { width: 90 } }),
  col.accessor('status', { header: '狀態', meta: { width: 90 } }),
  col.display({ id: 'actions', header: '', meta: { width: 80 } }),
];
</script>

<template>
  <a-card :bordered="false">
    <template #title><a-input-search v-model:value="keywordInput" placeholder="搜尋公司名稱、代碼、統編" allow-clear style="max-width: 280px" @search="search" /></template>
    <template #extra>
      <a-button v-if="can('platform.companies', 'create')" type="primary" @click="createOpen = true">開通公司</a-button>
    </template>
    <DataTable
      :data="companies.data.value?.items ?? []"
      :columns="columns"
      row-key="id"
      :loading="companies.isFetching.value"
      :scroll-x="760"
      :pagination="{ page: applied.page, pageSize: applied.pageSize, total: companies.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 間` }"
      @change="onTableChange"
    >
      <template #cell="{ columnId, record }">
        <template v-if="columnId === 'rootAdminAccount'">
          {{ record.rootAdminName }} <span class="muted">{{ record.rootAdminAccount }}</span>
        </template>
        <template v-else-if="columnId === 'status'">
          <a-tag :color="record.status === 'active' ? 'green' : 'red'">{{ record.status === 'active' ? '啟用' : '停用' }}</a-tag>
        </template>
        <template v-else-if="columnId === 'actions'">
          <a-button v-if="can('platform.companies', 'update')" type="link" size="small" @click="openEdit(record)">編輯</a-button>
        </template>
      </template>
    </DataTable>
  </a-card>

  <a-modal v-model:open="createOpen" title="開通公司" :confirm-loading="createMutation.isPending.value" ok-text="開通" @ok="createMutation.mutate()">
    <a-form layout="vertical">
      <a-divider orientation="left" plain>公司資料</a-divider>
      <a-form-item label="公司名稱" required><a-input v-model:value="createForm.name" /></a-form-item>
      <a-form-item label="公司代碼" required extra="小寫英數與連字號，建立後不可修改"><a-input v-model:value="createForm.slug" /></a-form-item>
      <a-form-item label="統一編號"><a-input v-model:value="createForm.taxId" /></a-form-item>
      <a-form-item label="電話"><a-input v-model:value="createForm.phone" /></a-form-item>
      <a-form-item label="地址"><a-input v-model:value="createForm.address" /></a-form-item>
      <a-divider orientation="left" plain>第一位公司管理員（受保護的主管理員）</a-divider>
      <a-form-item label="登入帳號" required><a-input v-model:value="createForm.adminAccount" autocomplete="off" /></a-form-item>
      <a-form-item label="姓名" required><a-input v-model:value="createForm.adminDisplayName" /></a-form-item>
      <a-form-item label="Email"><a-input v-model:value="createForm.adminEmail" /></a-form-item>
      <a-form-item label="初始密碼" required extra="至少 8 碼"><a-input-password v-model:value="createForm.adminPassword" autocomplete="new-password" /></a-form-item>
    </a-form>
  </a-modal>

  <a-modal v-model:open="editOpen" :title="`編輯公司：${editing?.name ?? ''}`" :confirm-loading="updateMutation.isPending.value" ok-text="儲存" @ok="updateMutation.mutate()">
    <a-form layout="vertical">
      <a-form-item label="公司名稱" required><a-input v-model:value="editForm.name" /></a-form-item>
      <a-form-item label="統一編號"><a-input v-model:value="editForm.taxId" /></a-form-item>
      <a-form-item label="電話"><a-input v-model:value="editForm.phone" /></a-form-item>
      <a-form-item label="地址"><a-input v-model:value="editForm.address" /></a-form-item>
      <a-form-item label="狀態" extra="停用後該公司所有成員立即登出">
        <a-radio-group v-model:value="editForm.status">
          <a-radio-button value="active">啟用</a-radio-button>
          <a-radio-button value="suspended">停用</a-radio-button>
        </a-radio-group>
      </a-form-item>
      <a-form-item label="角色 CRUD 細項設定" extra="關閉後，已授予的資源一律展開為完整 CRUD">
        <a-switch v-model:checked="editForm.isRoleCrudConfigurationEnabled" />
      </a-form-item>
      <a-form-item label="子功能細項設定" extra="關閉後，已授予的資源一律開放全部子功能">
        <a-switch v-model:checked="editForm.isRoleOptionConfigurationEnabled" />
      </a-form-item>
    </a-form>
  </a-modal>
</template>

<style scoped>
.muted {
  color: #999;
  font-size: 12px;
}
</style>
