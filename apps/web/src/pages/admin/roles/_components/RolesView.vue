<script setup lang="ts">
/**
 * @component RolesView
 * 角色權限編輯。
 * - company：系統角色唯讀、本公司自訂角色可新增／修改／刪除；權限不得超過公司管理員與自己的權限。
 * - platform：調整系統角色基線（platform-admin、company-admin 由 Seeder 維護，不可改）。
 */
import { computed, reactive, ref, watch } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import PermissionMatrix from '@/app/components/PermissionMatrix.vue';
import { useMe } from '@/app/composables/use-me';
import {
  createRoleApi,
  deleteRoleApi,
  fromAllowanceMap,
  getPermissionCatalogApi,
  getRolePermissionsApi,
  listRolesApi,
  replaceRolePermissionsApi,
  toAllowanceMap,
  updateRoleApi,
  type RoleRow,
} from '@/shared/services/admin-service';
import type { PermissionAllowance, PermissionCode } from '@/shared/types/permissions';
import { DATA_SCOPE_LABELS, dataScopeOptions as buildDataScopeOptions } from '@/shared/types/permissions';

const props = withDefaults(defineProps<{ mode?: 'company' | 'platform' }>(), { mode: 'company' });
const { me, can } = useMe();
const resource: PermissionCode = props.mode === 'platform' ? 'platform.roles' : 'company.roles';
const queryClient = useQueryClient();

const roles = useQuery({ queryKey: ['roles', props.mode], queryFn: () => listRolesApi(props.mode) });
const companyCatalog = useQuery({ queryKey: ['catalog', 'company'], queryFn: () => getPermissionCatalogApi('company') });
const platformCatalog = useQuery({ queryKey: ['catalog', 'platform'], queryFn: () => getPermissionCatalogApi('platform'), enabled: props.mode === 'platform' });

const selectedId = ref<string | null>(null);
const selected = computed(() => roles.data.value?.find((role) => role.id === selectedId.value) ?? null);
const catalog = computed(() => ((selected.value?.scope ?? 'company') === 'platform' ? platformCatalog.data.value : companyCatalog.data.value) ?? []);
const draft = ref<Record<string, PermissionAllowance>>({});
const dirty = ref(false);
const editable = computed(() => !!selected.value?.isEditable && can(resource, 'update'));
/** 公司模式下不可勾選超出自己權限的格子；平台調整系統角色時由後端以 company-admin 上限檢查。 */
const limit = computed(() => (props.mode === 'company' ? me.value?.grants : undefined));

watch(
  () => roles.data.value,
  (rows) => {
    if (rows?.length && !rows.some((role) => role.id === selectedId.value)) selectedId.value = rows[0].id;
  },
  { immediate: true },
);

watch(selectedId, async (roleId) => {
  if (!roleId) return;
  try {
    draft.value = toAllowanceMap(await getRolePermissionsApi(props.mode, roleId));
    dirty.value = false;
  } catch (error) {
    message.error((error as Error).message);
  }
});

function onDraftChange(value: Record<string, PermissionAllowance>) {
  draft.value = value;
  dirty.value = true;
}

const refresh = () => queryClient.invalidateQueries({ queryKey: ['roles', props.mode] });
const onError = (error: Error) => message.error(error.message);

const savePermissions = useMutation({
  mutationFn: () => replaceRolePermissionsApi(props.mode, selectedId.value!, fromAllowanceMap(draft.value)),
  onSuccess: () => {
    message.success('角色權限已更新，使用此角色的成員需重新取得登入權杖');
    dirty.value = false;
  },
  onError,
});

const formOpen = ref(false);
const isCreate = ref(true);
const form = reactive({ code: '', name: '', dataScope: 'own' });
// 公司角色的資料範圍不可大於自己的；平台管理員調整系統角色不受限。
const dataScopeOptions = computed(() => buildDataScopeOptions(props.mode === 'company' ? me.value?.scope.dataScope : undefined));

const saveRole = useMutation({
  mutationFn: async () => {
    if (isCreate.value) {
      const created = await createRoleApi({ code: form.code, name: form.name, dataScope: form.dataScope, permissions: [] });
      selectedId.value = created.id;
      return;
    }
    await updateRoleApi(props.mode, selectedId.value!, { name: form.name, dataScope: form.dataScope });
  },
  onSuccess: () => {
    message.success('已儲存');
    formOpen.value = false;
    refresh();
  },
  onError,
});

const deleteRole = useMutation({
  mutationFn: deleteRoleApi,
  onSuccess: () => {
    selectedId.value = null;
    refresh();
  },
  onError,
});

function openCreate() {
  isCreate.value = true;
  Object.assign(form, { code: '', name: '', dataScope: 'own' });
  formOpen.value = true;
}

function openEdit(role: RoleRow) {
  isCreate.value = false;
  Object.assign(form, { code: role.code, name: role.name, dataScope: role.dataScope });
  formOpen.value = true;
}

function confirmDelete(role: RoleRow) {
  Modal.confirm({ title: `刪除角色「${role.name}」？`, content: '仍有成員使用的角色不可刪除。', okType: 'danger', onOk: () => deleteRole.mutateAsync(role.id) });
}
</script>

<template>
  <a-row :gutter="[16, 16]">
    <a-col :xs="24" :lg="7">
      <a-card :bordered="false" title="角色" :body-style="{ padding: '8px' }">
        <template #extra>
          <a-button v-if="mode === 'company' && can('company.roles', 'create')" size="small" type="primary" @click="openCreate">新增角色</a-button>
        </template>
        <a-list :loading="roles.isLoading.value" :data-source="roles.data.value ?? []">
          <template #renderItem="{ item }">
            <a-list-item class="role-item" :class="{ active: item.id === selectedId }" @click="selectedId = item.id">
              <a-list-item-meta :description="`${DATA_SCOPE_LABELS[item.dataScope]} · ${item.memberCount} 人`">
                <template #title>
                  {{ item.name }}
                  <a-tag v-if="item.isSystem" :bordered="false">系統</a-tag>
                  <a-tag v-if="mode === 'platform'" :bordered="false" color="blue">{{ item.scope === 'platform' ? '平台' : '公司' }}</a-tag>
                </template>
              </a-list-item-meta>
            </a-list-item>
          </template>
        </a-list>
      </a-card>
    </a-col>
    <a-col :xs="24" :lg="17">
      <a-card v-if="selected" :bordered="false">
        <template #title>
          {{ selected.name }} <span class="muted">{{ selected.code }}</span>
        </template>
        <template #extra>
          <a-space>
            <template v-if="selected.isEditable">
              <a-button v-if="can(resource, 'update')" size="small" @click="openEdit(selected)">名稱與資料範圍</a-button>
              <a-button v-if="mode === 'company' && can('company.roles', 'delete')" size="small" danger @click="confirmDelete(selected)">刪除</a-button>
            </template>
            <a-button v-if="editable" type="primary" size="small" :disabled="!dirty" :loading="savePermissions.isPending.value" @click="savePermissions.mutate()">儲存權限</a-button>
          </a-space>
        </template>
        <a-alert
          v-if="!selected.isEditable"
          type="info"
          show-icon
          style="margin-bottom: 12px"
          :message="selected.isFullAccess ? '完整存取角色由系統自動維護，不可修改。' : '系統角色由平台管理員維護；需要不同權限時請新增自訂角色。'"
        />
        <PermissionMatrix :model-value="draft" :catalog="catalog" :disabled="!editable" :limit="limit" @update:model-value="onDraftChange" />
      </a-card>
    </a-col>
  </a-row>

  <a-modal v-model:open="formOpen" :title="isCreate ? '新增角色' : '編輯角色'" :confirm-loading="saveRole.isPending.value" ok-text="儲存" @ok="saveRole.mutate()">
    <a-form layout="vertical">
      <a-form-item label="代碼" required extra="小寫英數與連字號，建立後不可修改"><a-input v-model:value="form.code" :disabled="!isCreate" /></a-form-item>
      <a-form-item label="名稱" required><a-input v-model:value="form.name" /></a-form-item>
      <a-form-item label="資料範圍" required extra="此角色的成員可以看到哪些專案"><a-select v-model:value="form.dataScope" :options="dataScopeOptions" /></a-form-item>
    </a-form>
  </a-modal>
</template>

<style scoped>
.role-item {
  cursor: pointer;
  padding: 8px 12px !important;
  border-radius: 6px;
}
.role-item.active {
  background: #eaf0f7;
}
.muted {
  color: #999;
  font-size: 12px;
  font-weight: normal;
}
</style>
