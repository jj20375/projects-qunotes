<script setup lang="ts">
/**
 * @component UsersView
 * 公司成員：建立帳號、指派部門／角色／資料範圍、停用、重設密碼與個人權限。
 * 主管理員與自己的角色、範圍、啟用狀態不可在此變更（後端同樣阻擋）。
 * 搜尋與分頁都由後端處理（GET /api/admin/users?keyword=&page=&pageSize=）。
 */
import { computed, reactive, ref } from 'vue';
import { message } from 'ant-design-vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { useMe } from '@/app/composables/use-me';
import {
  createMemberApi,
  listDepartmentsApi,
  listMembersApi,
  listRolesApi,
  resetMemberPasswordApi,
  updateMemberApi,
  type DepartmentRow,
  type MemberRow,
} from '@/shared/services/admin-service';
import { dataColumns } from '@/shared/table/data-table';
import { DATA_SCOPE_LABELS, dataScopeOptions as buildDataScopeOptions, exceedsDataScope } from '@/shared/types/permissions';
import UserPermissionsDrawer from './UserPermissionsDrawer.vue';

const { me, can, canOption } = useMe();
const canAssignRole = computed(() => canOption('company.users', 'assign_role'));
const queryClient = useQueryClient();
const keywordInput = ref('');
const applied = reactive({ keyword: '', page: 1, pageSize: 20 });
const members = useQuery({ queryKey: ['members', applied], queryFn: () => listMembersApi({ ...applied }), placeholderData: keepPreviousData });
const departments = useQuery({ queryKey: ['departments'], queryFn: listDepartmentsApi });
const roles = useQuery({ queryKey: ['roles', 'company'], queryFn: () => listRolesApi('company', { forAssignment: true }), enabled: canAssignRole });

function search() {
  applied.keyword = keywordInput.value.trim();
  applied.page = 1;
}

function onTableChange(change: TableChange) {
  applied.page = change.page;
  applied.pageSize = change.pageSize;
}

const departmentTree = computed(() => {
  const rows = departments.data.value ?? [];
  const build = (parentId: string | null): unknown[] =>
    rows.filter((row: DepartmentRow) => row.parentId === parentId && row.isActive).map((row) => ({ value: row.id, title: row.name, children: build(row.id) }));
  return build(null);
});
// 自訂角色只列出自己建立的；編輯成員時，若成員目前的角色是別人建立的自訂角色，仍要顯示出來才不會變成一串代碼。
const roleOptions = computed(() => {
  // 資料範圍比自己大的角色不能指派（停用顯示，後端也會檢查）。
  const options = (roles.data.value ?? []).map((role) => ({ value: role.id, label: role.name, disabled: exceedsDataScope(role.dataScope, me.value?.scope.dataScope) }));
  const current = editing.value;
  if (current && !options.some((option) => option.value === current.roleId)) options.push({ value: current.roleId, label: `${current.roleName}（其他人建立的自訂角色）`, disabled: false });
  return options;
});
// 個別資料範圍不可大於自己的（後端也會檢查）。
const dataScopeOptions = computed(() => buildDataScopeOptions(me.value?.scope.dataScope, [{ value: '', label: '依角色預設' }]));

const formOpen = ref(false);
const editing = ref<MemberRow | null>(null);
const form = reactive({ account: '', displayName: '', email: '', password: '', departmentId: undefined as string | undefined, roleId: undefined as string | undefined, dataScope: '', isActive: true });
const locked = computed(() => !!editing.value && (editing.value.isRootAdmin || editing.value.isSelf));

const passwordTarget = ref<MemberRow | null>(null);
const newPassword = ref('');
const permissionTarget = ref<MemberRow | null>(null);

const refresh = () => queryClient.invalidateQueries({ queryKey: ['members'] });
const onError = (error: Error) => message.error(error.message);

const saveMutation = useMutation({
  mutationFn: () => {
    if (!editing.value) {
      return createMemberApi({
        account: form.account,
        displayName: form.displayName,
        email: form.email,
        password: form.password,
        departmentId: form.departmentId,
        roleId: canAssignRole.value ? form.roleId : undefined,
        dataScopeOverride: canAssignRole.value && form.dataScope ? form.dataScope : undefined,
      });
    }
    const body: Record<string, unknown> = { displayName: form.displayName, email: form.email, departmentId: form.departmentId };
    if (!locked.value) {
      body.isActive = form.isActive;
      if (canAssignRole.value) {
        body.roleId = form.roleId;
        if (form.dataScope) body.dataScopeOverride = form.dataScope;
        else body.clearDataScopeOverride = true;
      }
    }
    return updateMemberApi(editing.value.membershipId, body);
  },
  onSuccess: () => {
    message.success('已儲存');
    formOpen.value = false;
    refresh();
  },
  onError,
});

const resetMutation = useMutation({
  mutationFn: () => resetMemberPasswordApi(passwordTarget.value!.membershipId, newPassword.value),
  onSuccess: () => {
    message.success('密碼已重設，該帳號需重新登入');
    passwordTarget.value = null;
  },
  onError,
});

function openCreate() {
  editing.value = null;
  const firstRole = roleOptions.value.find((option) => option.label === '業務') ?? roleOptions.value[0];
  Object.assign(form, { account: '', displayName: '', email: '', password: '', departmentId: undefined, roleId: firstRole?.value, dataScope: '', isActive: true });
  formOpen.value = true;
}

function openEdit(row: MemberRow) {
  editing.value = row;
  Object.assign(form, {
    account: row.account,
    displayName: row.displayName,
    email: row.email ?? '',
    password: '',
    departmentId: row.departmentId ?? undefined,
    roleId: row.roleId,
    dataScope: row.dataScopeOverride ?? '',
    isActive: row.isActive,
  });
  formOpen.value = true;
}

function openPassword(row: MemberRow) {
  passwordTarget.value = row;
  newPassword.value = '';
}

const col = dataColumns<MemberRow>();
const columns = [
  col.accessor('displayName', { header: '姓名' }),
  col.accessor('account', { header: '帳號' }),
  col.accessor('departmentName', { header: '部門' }),
  col.accessor('roleName', { header: '角色' }),
  col.accessor('effectiveDataScope', { header: '資料範圍' }),
  col.accessor('personalPermissionCount', { header: '個人權限', meta: { width: 90 } }),
  col.accessor('isActive', { header: '狀態', meta: { width: 80 } }),
  col.display({ id: 'actions', header: '', meta: { width: 220, fixed: 'right' } }),
];
</script>

<template>
  <a-card :bordered="false">
    <template #title><a-input-search v-model:value="keywordInput" placeholder="搜尋姓名、帳號、Email" allow-clear style="max-width: 280px" @search="search" /></template>
    <template #extra><a-button v-if="can('company.users', 'create')" type="primary" @click="openCreate">新增成員</a-button></template>
    <DataTable
      :data="members.data.value?.items ?? []"
      :columns="columns"
      row-key="membershipId"
      :loading="members.isFetching.value"
      :scroll-x="960"
      :pagination="{ page: applied.page, pageSize: applied.pageSize, total: members.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 人` }"
      @change="onTableChange"
    >
      <template #cell="{ columnId, record }">
        <template v-if="columnId === 'displayName'">
          {{ record.displayName }}
          <a-tag v-if="record.isRootAdmin" color="gold">主管理員</a-tag>
          <a-tag v-if="record.isSelf" color="blue">我</a-tag>
        </template>
        <template v-else-if="columnId === 'effectiveDataScope'">
          {{ DATA_SCOPE_LABELS[record.effectiveDataScope] }}
          <a-tag v-if="record.dataScopeOverride" color="purple">個別設定</a-tag>
        </template>
        <template v-else-if="columnId === 'personalPermissionCount'">
          <a-badge v-if="record.personalPermissionCount" :count="record.personalPermissionCount" :number-style="{ backgroundColor: '#722ed1' }" />
          <span v-else class="muted">—</span>
        </template>
        <template v-else-if="columnId === 'isActive'">
          <a-tag :color="record.isActive ? 'green' : 'default'">{{ record.isActive ? '啟用' : '停用' }}</a-tag>
        </template>
        <template v-else-if="columnId === 'actions'">
          <a-button v-if="can('company.users', 'update')" type="link" size="small" @click="openEdit(record)">編輯</a-button>
          <a-button type="link" size="small" @click="permissionTarget = record">個人權限</a-button>
          <a-button
            v-if="canOption('company.users', 'reset_password') && (!record.isRootAdmin || record.isSelf)"
            type="link"
            size="small"
            @click="openPassword(record)"
          >
            重設密碼
          </a-button>
        </template>
      </template>
    </DataTable>
  </a-card>

  <a-modal v-model:open="formOpen" :title="editing ? `編輯成員：${editing.displayName}` : '新增成員'" :confirm-loading="saveMutation.isPending.value" ok-text="儲存" @ok="saveMutation.mutate()">
    <a-alert v-if="locked" type="info" show-icon style="margin-bottom: 16px" :message="editing?.isRootAdmin ? '主管理員的角色、資料範圍與啟用狀態不可變更。' : '不可變更自己的角色、資料範圍與啟用狀態。'" />
    <a-form layout="vertical">
      <a-form-item label="帳號" required><a-input v-model:value="form.account" :disabled="!!editing" autocomplete="off" /></a-form-item>
      <a-form-item label="姓名" required><a-input v-model:value="form.displayName" /></a-form-item>
      <a-form-item label="Email"><a-input v-model:value="form.email" /></a-form-item>
      <a-form-item v-if="!editing" label="初始密碼" required extra="至少 8 碼"><a-input-password v-model:value="form.password" autocomplete="new-password" /></a-form-item>
      <a-form-item label="部門" required><a-tree-select v-model:value="form.departmentId" :tree-data="departmentTree" tree-default-expand-all /></a-form-item>
      <template v-if="canAssignRole">
        <a-form-item label="角色" required><a-select v-model:value="form.roleId" :options="roleOptions" :disabled="locked" /></a-form-item>
        <a-form-item label="資料範圍" extra="決定可以看到哪些專案；預設跟著角色"><a-select v-model:value="form.dataScope" :options="dataScopeOptions" :disabled="locked" /></a-form-item>
      </template>
      <a-alert v-else-if="!editing" type="info" message="您沒有指派角色的權限，新成員會是唯讀角色。" />
      <a-form-item v-if="editing" label="啟用"><a-switch v-model:checked="form.isActive" :disabled="locked" /></a-form-item>
    </a-form>
  </a-modal>

  <a-modal :open="!!passwordTarget" :title="`重設密碼：${passwordTarget?.displayName ?? ''}`" :confirm-loading="resetMutation.isPending.value" ok-text="重設" @ok="resetMutation.mutate()" @cancel="passwordTarget = null">
    <a-input-password v-model:value="newPassword" placeholder="新密碼（至少 8 碼）" autocomplete="new-password" />
  </a-modal>

  <UserPermissionsDrawer :member="permissionTarget" @close="permissionTarget = null" @saved="refresh" />
</template>

<style scoped>
.muted {
  color: #bbb;
}
</style>
