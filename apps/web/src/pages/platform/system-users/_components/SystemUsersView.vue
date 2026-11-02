<script setup lang="ts">
/**
 * @component SystemUsersView
 * 平台系統帳號：建立、改角色／停用、重設密碼。不可變更自己的角色與啟用狀態（後端同樣檢查）。
 * 搜尋與分頁由後端處理（GET /api/platform/system-users?keyword=&page=&pageSize=）。
 */
import { computed, reactive, ref } from 'vue';
import { message } from 'ant-design-vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { useMe } from '@/app/composables/use-me';
import {
  createSystemUserApi,
  listRolesApi,
  listSystemUsersApi,
  resetSystemUserPasswordApi,
  updateSystemUserApi,
  type SystemUserRow,
} from '@/shared/services/admin-service';
import { dataColumns } from '@/shared/table/data-table';

const { me, can, canOption } = useMe();
const queryClient = useQueryClient();
const keywordInput = ref('');
const applied = reactive({ keyword: '', page: 1, pageSize: 20 });
const users = useQuery({ queryKey: ['system-users', applied], queryFn: () => listSystemUsersApi({ ...applied }), placeholderData: keepPreviousData });

function search() {
  applied.keyword = keywordInput.value.trim();
  applied.page = 1;
}

function onTableChange(change: TableChange) {
  applied.page = change.page;
  applied.pageSize = change.pageSize;
}
const roles = useQuery({ queryKey: ['roles', 'platform'], queryFn: () => listRolesApi('platform') });
const roleOptions = computed(() => (roles.data.value ?? []).filter((role) => role.scope === 'platform').map((role) => ({ value: role.id, label: role.name })));

const formOpen = ref(false);
const editing = ref<SystemUserRow | null>(null);
const form = reactive({ account: '', displayName: '', email: '', password: '', roleId: undefined as string | undefined, status: 'active' });
const passwordOpen = ref(false);
const newPassword = ref('');

const refresh = () => queryClient.invalidateQueries({ queryKey: ['system-users'] });
const onError = (error: Error) => message.error(error.message);

const saveMutation = useMutation({
  mutationFn: () =>
    editing.value
      ? updateSystemUserApi(editing.value.id, { displayName: form.displayName, email: form.email, roleId: form.roleId, status: form.status })
      : createSystemUserApi({ account: form.account, displayName: form.displayName, email: form.email, password: form.password, roleId: form.roleId }),
  onSuccess: () => {
    message.success('已儲存');
    formOpen.value = false;
    refresh();
  },
  onError,
});

const resetMutation = useMutation({
  mutationFn: () => resetSystemUserPasswordApi(editing.value!.id, newPassword.value),
  onSuccess: () => {
    message.success('密碼已重設，該帳號需重新登入');
    passwordOpen.value = false;
  },
  onError,
});

function openCreate() {
  editing.value = null;
  Object.assign(form, { account: '', displayName: '', email: '', password: '', roleId: roleOptions.value[0]?.value, status: 'active' });
  formOpen.value = true;
}

function openEdit(row: SystemUserRow) {
  editing.value = row;
  Object.assign(form, { account: row.account, displayName: row.displayName, email: row.email ?? '', password: '', roleId: row.systemRoleId, status: row.status });
  formOpen.value = true;
}

function openReset(row: SystemUserRow) {
  editing.value = row;
  newPassword.value = '';
  passwordOpen.value = true;
}

const isSelf = computed(() => editing.value?.id === me.value?.user.id);
const col = dataColumns<SystemUserRow>();
const columns = [
  col.accessor('account', { header: '帳號' }),
  col.accessor('displayName', { header: '姓名' }),
  col.accessor('roleName', { header: '角色' }),
  col.accessor('status', { header: '狀態', meta: { width: 90 } }),
  col.accessor('lastLoginAt', { header: '最後登入' }),
  col.display({ id: 'actions', header: '', meta: { width: 150 } }),
];
</script>

<template>
  <a-card :bordered="false">
    <template #title><a-input-search v-model:value="keywordInput" placeholder="搜尋帳號、姓名、Email" allow-clear style="max-width: 280px" @search="search" /></template>
    <template #extra><a-button v-if="can('platform.system_users', 'create')" type="primary" @click="openCreate">新增系統帳號</a-button></template>
    <DataTable
      :data="users.data.value?.items ?? []"
      :columns="columns"
      row-key="id"
      :loading="users.isFetching.value"
      :scroll-x="720"
      :pagination="{ page: applied.page, pageSize: applied.pageSize, total: users.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 個` }"
      @change="onTableChange"
    >
      <template #cell="{ columnId, record }">
        <template v-if="columnId === 'status'">
          <a-tag :color="record.status === 'active' ? 'green' : 'default'">{{ record.status === 'active' ? '啟用' : '停用' }}</a-tag>
        </template>
        <template v-else-if="columnId === 'lastLoginAt'">{{ record.lastLoginAt ? new Date(record.lastLoginAt).toLocaleString('zh-TW') : '—' }}</template>
        <template v-else-if="columnId === 'actions'">
          <a-button v-if="can('platform.system_users', 'update')" type="link" size="small" @click="openEdit(record)">編輯</a-button>
          <a-button v-if="canOption('platform.system_users', 'reset_password')" type="link" size="small" @click="openReset(record)">重設密碼</a-button>
        </template>
      </template>
    </DataTable>
  </a-card>

  <a-modal v-model:open="formOpen" :title="editing ? '編輯系統帳號' : '新增系統帳號'" :confirm-loading="saveMutation.isPending.value" ok-text="儲存" @ok="saveMutation.mutate()">
    <a-form layout="vertical">
      <a-form-item label="帳號" required><a-input v-model:value="form.account" :disabled="!!editing" autocomplete="off" /></a-form-item>
      <a-form-item label="姓名" required><a-input v-model:value="form.displayName" /></a-form-item>
      <a-form-item label="Email"><a-input v-model:value="form.email" /></a-form-item>
      <a-form-item v-if="!editing" label="初始密碼" required extra="至少 8 碼"><a-input-password v-model:value="form.password" autocomplete="new-password" /></a-form-item>
      <a-form-item label="平台角色" required><a-select v-model:value="form.roleId" :options="roleOptions" :disabled="isSelf" /></a-form-item>
      <a-form-item v-if="editing" label="狀態">
        <a-radio-group v-model:value="form.status" :disabled="isSelf">
          <a-radio-button value="active">啟用</a-radio-button>
          <a-radio-button value="disabled">停用</a-radio-button>
        </a-radio-group>
      </a-form-item>
    </a-form>
  </a-modal>

  <a-modal v-model:open="passwordOpen" :title="`重設密碼：${editing?.account ?? ''}`" :confirm-loading="resetMutation.isPending.value" ok-text="重設" @ok="resetMutation.mutate()">
    <a-input-password v-model:value="newPassword" placeholder="新密碼（至少 8 碼）" autocomplete="new-password" />
  </a-modal>
</template>
