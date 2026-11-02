<script setup lang="ts">
/**
 * @component MemberPickerModal
 * 從公司名單勾選專案成員：表格列出姓名、部門、角色，可用姓名、部門（含下層部門）、角色篩選，
 * 搜尋與分頁都由後端處理（/api/directory/members）。可以跨頁勾選，按「加入」後把勾選的人交給呼叫端。
 *
 * - `exclude-ids`：已經在專案裡的人（含負責人），名單上不會出現。
 * - `pick` 事件帶出勾選的人（含姓名、部門），呼叫端再決定專案角色。
 */
import { computed, reactive, ref, watch } from 'vue';
import { keepPreviousData, useQuery } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { dataColumns } from '@/shared/table/data-table';
import { listDirectoryDepartmentsApi, listDirectoryMembersApi, listDirectoryRolesApi, type DirectoryMember } from '@/shared/services/business-service';

const props = defineProps<{ open: boolean; excludeIds: string[] }>();
const emit = defineEmits<{ close: []; pick: [members: DirectoryMember[]] }>();

const keywordInput = ref('');
const filters = reactive({ keyword: '', departmentId: undefined as string | undefined, roleId: undefined as string | undefined, page: 1, pageSize: 10 });
const selectedKeys = ref<string[]>([]);
/** 勾選過的人：換頁後表格只有這一頁的資料，所以另外記住勾選者的姓名與部門。 */
const picked = new Map<string, DirectoryMember>();

const members = useQuery({
  queryKey: ['directory-members', filters, computed(() => props.excludeIds.join(','))],
  queryFn: () => listDirectoryMembersApi({ ...filters, includeSubDepartments: true, excludeIds: props.excludeIds }),
  enabled: computed(() => props.open),
  placeholderData: keepPreviousData,
});
const departments = useQuery({ queryKey: ['directory', 'departments'], queryFn: listDirectoryDepartmentsApi, enabled: computed(() => props.open) });
const roles = useQuery({ queryKey: ['directory', 'roles'], queryFn: listDirectoryRolesApi, enabled: computed(() => props.open) });

const departmentTree = computed(() => {
  const rows = departments.data.value ?? [];
  const build = (parentId: string | null): unknown[] => rows.filter((row) => row.parentId === parentId).map((row) => ({ value: row.id, title: row.name, children: build(row.id) }));
  return build(null);
});
const roleOptions = computed(() => (roles.data.value ?? []).map((role) => ({ value: role.id, label: role.name })));

watch(
  () => props.open,
  (open) => {
    if (!open) return;
    keywordInput.value = '';
    Object.assign(filters, { keyword: '', departmentId: undefined, roleId: undefined, page: 1 });
    selectedKeys.value = [];
    picked.clear();
  },
);
// 篩選條件改變時回到第一頁（勾選保留）
watch(
  () => [filters.departmentId, filters.roleId],
  () => (filters.page = 1),
);
watch(
  () => members.data.value?.items,
  (items) => items?.forEach((item) => selectedKeys.value.includes(item.userId) && picked.set(item.userId, item)),
);

function search() {
  filters.keyword = keywordInput.value.trim();
  filters.page = 1;
}

function onTableChange(change: TableChange) {
  Object.assign(filters, { page: change.page, pageSize: change.pageSize });
}

function onSelect(keys: string[]) {
  selectedKeys.value = keys;
  for (const item of members.data.value?.items ?? []) if (keys.includes(item.userId)) picked.set(item.userId, item);
  for (const key of [...picked.keys()]) if (!keys.includes(key)) picked.delete(key);
}

function confirm() {
  emit('pick', selectedKeys.value.map((key) => picked.get(key)).filter((value): value is DirectoryMember => !!value));
  emit('close');
}

const col = dataColumns<DirectoryMember>();
const columns = [
  col.accessor('displayName', { header: '姓名' }),
  col.accessor('departmentName', { header: '部門', meta: { width: 180 } }),
  col.accessor('roleName', { header: '角色', meta: { width: 140 } }),
];
</script>

<template>
  <a-modal :open="open" title="選擇專案成員" :width="760" @cancel="emit('close')">
    <div class="filters">
      <a-input-search v-model:value="keywordInput" placeholder="搜尋姓名" allow-clear class="keyword" @search="search" />
      <a-tree-select v-model:value="filters.departmentId" :tree-data="departmentTree" tree-default-expand-all allow-clear show-search tree-node-filter-prop="title" placeholder="全部部門（含下層）" class="filter" />
      <a-select v-model:value="filters.roleId" :options="roleOptions" allow-clear placeholder="全部角色" class="filter" />
    </div>
    <DataTable
      :selected-keys="selectedKeys"
      :data="members.data.value?.items ?? []"
      :columns="columns"
      row-key="userId"
      size="small"
      :loading="members.isFetching.value"
      :pagination="{ page: filters.page, pageSize: filters.pageSize, total: members.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 人` }"
      empty-text="沒有符合條件的人"
      @update:selected-keys="onSelect"
      @change="onTableChange"
    />
    <template #footer>
      <span class="count">已勾選 {{ selectedKeys.length }} 人</span>
      <a-button @click="emit('close')">取消</a-button>
      <a-button type="primary" :disabled="!selectedKeys.length" @click="confirm">加入</a-button>
    </template>
  </a-modal>
</template>

<style scoped>
.filters {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 12px;
}
.keyword {
  width: 220px;
}
.filter {
  width: 200px;
}
.count {
  float: left;
  line-height: 32px;
  color: #666;
}
@media (max-width: 575px) {
  .keyword,
  .filter {
    width: 100%;
  }
}
</style>
