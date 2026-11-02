<script setup lang="ts">
/**
 * @component DepartmentsView
 * 部門樹：新增下層、改名、移動（改上層）、停用、刪除。
 * 部門決定 department／department_tree 資料範圍可看到哪些專案。
 */
import { computed, reactive, ref } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable from '@/app/components/DataTable.vue';
import { useMe } from '@/app/composables/use-me';
import { dataColumns } from '@/shared/table/data-table';
import { createDepartmentApi, deleteDepartmentApi, listDepartmentsApi, updateDepartmentApi, type DepartmentRow } from '@/shared/services/admin-service';

interface TreeRow extends DepartmentRow {
  children?: TreeRow[];
}

const { can } = useMe();
const queryClient = useQueryClient();
const departments = useQuery({ queryKey: ['departments'], queryFn: listDepartmentsApi });

/** 平面清單轉樹；沒有子節點的列不給 children，表格才不會顯示展開鈕。 */
function buildTree(rows: DepartmentRow[], parentId: string | null): TreeRow[] {
  return rows
    .filter((row) => row.parentId === parentId)
    .map((row) => {
      const children = buildTree(rows, row.id);
      return children.length ? { ...row, children } : { ...row };
    });
}

const tree = computed(() => buildTree(departments.data.value ?? [], null));
const treeSelectData = computed(() => {
  const map = (rows: TreeRow[]): unknown[] => rows.map((row) => ({ value: row.id, title: row.name, children: row.children ? map(row.children) : [] }));
  return map(tree.value);
});

const formOpen = ref(false);
const editing = ref<DepartmentRow | null>(null);
const form = reactive({ parentId: undefined as string | undefined, name: '', code: '', sortOrder: 0, isActive: true });

const refresh = () => queryClient.invalidateQueries({ queryKey: ['departments'] });
const onError = (error: Error) => message.error(error.message);

const saveMutation = useMutation({
  mutationFn: () => (editing.value ? updateDepartmentApi(editing.value.id, { ...form }) : createDepartmentApi({ ...form })),
  onSuccess: () => {
    message.success('已儲存');
    formOpen.value = false;
    refresh();
  },
  onError,
});

const deleteMutation = useMutation({ mutationFn: deleteDepartmentApi, onSuccess: refresh, onError });

function openCreate(parent: DepartmentRow) {
  editing.value = null;
  Object.assign(form, { parentId: parent.id, name: '', code: '', sortOrder: 0, isActive: true });
  formOpen.value = true;
}

function openEdit(row: DepartmentRow) {
  editing.value = row;
  Object.assign(form, { parentId: row.parentId ?? undefined, name: row.name, code: row.code ?? '', sortOrder: row.sortOrder, isActive: row.isActive });
  formOpen.value = true;
}

function confirmDelete(row: DepartmentRow) {
  Modal.confirm({ title: `刪除部門「${row.name}」？`, content: '部門內不可有成員或下層部門。', okType: 'danger', onOk: () => deleteMutation.mutateAsync(row.id) });
}

const col = dataColumns<TreeRow>();
const columns = [
  col.accessor('name', { header: '部門' }),
  col.accessor('code', { header: '代碼', meta: { width: 120 } }),
  col.accessor('memberCount', { header: '成員數', meta: { width: 90 } }),
  col.accessor('isActive', { header: '狀態', meta: { width: 90 } }),
  col.display({ id: 'actions', header: '', meta: { width: 210 } }),
];
</script>

<template>
  <a-card :bordered="false">
    <DataTable v-if="tree.length" :data="tree" :columns="columns" row-key="id" :get-sub-rows="(row: TreeRow) => row.children" default-expand-all :scroll-x="640">
      <template #cell="{ columnId, record }">
        <template v-if="columnId === 'isActive'">
          <a-tag :color="record.isActive ? 'green' : 'default'">{{ record.isActive ? '啟用' : '停用' }}</a-tag>
        </template>
        <template v-else-if="columnId === 'actions'">
          <a-button v-if="can('company.departments', 'create')" type="link" size="small" @click="openCreate(record)">新增下層</a-button>
          <a-button v-if="can('company.departments', 'update')" type="link" size="small" @click="openEdit(record)">編輯</a-button>
          <a-button v-if="can('company.departments', 'delete') && record.parentId" type="link" size="small" danger @click="confirmDelete(record)">刪除</a-button>
        </template>
      </template>
    </DataTable>
    <a-skeleton v-else-if="departments.isLoading.value" active />
  </a-card>

  <a-modal v-model:open="formOpen" :title="editing ? '編輯部門' : '新增部門'" :confirm-loading="saveMutation.isPending.value" ok-text="儲存" @ok="saveMutation.mutate()">
    <a-form layout="vertical">
      <a-form-item v-if="!editing || editing.parentId" label="上層部門" required extra="修改上層即移動整個子樹">
        <a-tree-select v-model:value="form.parentId" :tree-data="treeSelectData" tree-default-expand-all />
      </a-form-item>
      <a-form-item label="名稱" required><a-input v-model:value="form.name" /></a-form-item>
      <a-form-item label="代碼"><a-input v-model:value="form.code" /></a-form-item>
      <a-form-item label="排序"><a-input-number v-model:value="form.sortOrder" :min="0" /></a-form-item>
      <a-form-item v-if="editing && editing.parentId" label="啟用"><a-switch v-model:checked="form.isActive" /></a-form-item>
    </a-form>
  </a-modal>
</template>
