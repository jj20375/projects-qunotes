<script setup lang="ts">
/**
 * @component PermissionMatrix
 * 權限目錄 × CRUD × 子功能的勾選表；角色權限與個人權限（加權／禁止）共用。
 *
 * `limit` 傳入操作者自己的有效權限時，超出範圍的格子會停用（已勾選的仍可取消），
 * 與後端「不可給出自己沒有的權限」規則一致；後端仍會再驗證一次。
 */
import { computed } from 'vue';
import type { CatalogNode, PermissionAction, PermissionAllowance, PermissionGrant } from '@/shared/types/permissions';
import { ACTION_LABELS } from '@/shared/types/permissions';

const props = defineProps<{
  catalog: CatalogNode[];
  modelValue: Record<string, PermissionAllowance>;
  disabled?: boolean;
  limit?: PermissionGrant[];
  tone?: 'grant' | 'deny';
}>();
const emit = defineEmits<{ 'update:modelValue': [value: Record<string, PermissionAllowance>] }>();

const actions: PermissionAction[] = ['create', 'read', 'update', 'delete'];
const fieldOf: Record<PermissionAction, 'perCreate' | 'perRead' | 'perUpdate' | 'perDelete'> = {
  create: 'perCreate',
  read: 'perRead',
  update: 'perUpdate',
  delete: 'perDelete',
};

const rows = computed(() => {
  const result: { node: CatalogNode; depth: number }[] = [];
  const walk = (nodes: CatalogNode[], depth: number) =>
    nodes.forEach((node) => {
      result.push({ node, depth });
      walk(node.children, depth + 1);
    });
  walk(props.catalog, 0);
  return result;
});

function current(code: string): PermissionAllowance {
  return props.modelValue[code] ?? { code, perCreate: false, perRead: false, perUpdate: false, perDelete: false, options: [] };
}

function allowedAction(code: string, action: PermissionAction): boolean {
  return !props.limit || !!props.limit.find((grant) => grant.code === code)?.actions.includes(action);
}

function allowedOption(code: string, option: string): boolean {
  return !props.limit || !!props.limit.find((grant) => grant.code === code)?.options.includes(option);
}

function update(code: string, patch: Partial<PermissionAllowance>) {
  emit('update:modelValue', { ...props.modelValue, [code]: { ...current(code), ...patch } });
}

function toggleOption(code: string, option: string, checked: boolean) {
  const options = new Set(current(code).options);
  if (checked) options.add(option);
  else options.delete(option);
  update(code, { options: [...options] });
}
</script>

<template>
  <div class="matrix-wrap">
    <table class="matrix" :class="tone">
      <thead>
        <tr>
          <th class="name">資源</th>
          <th v-for="action in actions" :key="action" class="action">{{ ACTION_LABELS[action] }}</th>
          <th>子功能</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="{ node, depth } in rows" :key="node.code" :class="{ group: node.isGroup }">
          <td class="name" :style="{ paddingLeft: `${12 + depth * 20}px` }">{{ node.name }}</td>
          <template v-if="node.isGroup">
            <td :colspan="5" />
          </template>
          <template v-else>
            <td v-for="action in actions" :key="action" class="action">
              <a-checkbox
                :checked="current(node.code)[fieldOf[action]]"
                :disabled="disabled || (!current(node.code)[fieldOf[action]] && !allowedAction(node.code, action))"
                @change="(event: { target: { checked: boolean } }) => update(node.code, { [fieldOf[action]]: event.target.checked })"
              />
            </td>
            <td class="options">
              <a-checkbox
                v-for="feature in node.subFeatures"
                :key="feature.key"
                :checked="current(node.code).options.includes(feature.key)"
                :disabled="disabled || (!current(node.code).options.includes(feature.key) && !allowedOption(node.code, feature.key))"
                @change="(event: { target: { checked: boolean } }) => toggleOption(node.code, feature.key, event.target.checked)"
              >
                {{ feature.name }}
              </a-checkbox>
            </td>
          </template>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.matrix-wrap {
  overflow-x: auto;
}
.matrix {
  width: 100%;
  min-width: 640px;
  border-collapse: collapse;
  font-size: 14px;
}
.matrix th,
.matrix td {
  border-bottom: 1px solid #f0f0f0;
  padding: 8px 12px;
  text-align: left;
  vertical-align: top;
}
.matrix th {
  background: #fafafa;
  font-weight: 500;
}
.matrix .action {
  width: 56px;
  text-align: center;
}
.matrix .name {
  white-space: nowrap;
}
.matrix tr.group td {
  background: #f7f9fc;
  font-weight: 600;
}
.matrix .options {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 12px;
}
.matrix.deny :deep(.ant-checkbox-checked .ant-checkbox-inner) {
  background-color: #cf1322;
  border-color: #cf1322;
}
</style>
