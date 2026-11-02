<script setup lang="ts">
/**
 * @component ProjectMembersModal
 * 整批編輯專案成員。manager／member 可修改專案，viewer 只能檢視；負責人固定有完整權限，不在清單內。
 */
import { ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import UserSelect from '@/app/components/UserSelect.vue';
import { replaceProjectMembersApi, type ProjectDetail, type ProjectMember } from '@/shared/services/business-service';

const props = defineProps<{ open: boolean; project: ProjectDetail }>();
const emit = defineEmits<{ close: []; saved: [] }>();

const roleOptions = [
  { value: 'manager', label: '管理（可改專案與成員）' },
  { value: 'member', label: '成員（可改專案）' },
  { value: 'viewer', label: '檢視（只能看）' },
];

const rows = ref<ProjectMember[]>([]);
const saving = ref(false);

watch(
  () => props.open,
  (open) => {
    if (open) rows.value = props.project.members.map((member) => ({ userId: member.userId, projectRole: member.projectRole }));
  },
);

async function save() {
  saving.value = true;
  try {
    await replaceProjectMembersApi(props.project.id, rows.value.filter((row) => row.userId));
    message.success('成員已更新');
    emit('saved');
    emit('close');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <a-modal :open="open" title="專案成員" :confirm-loading="saving" ok-text="儲存" @ok="save" @cancel="emit('close')">
    <p class="hint">負責人：{{ project.owner.displayName }}（固定擁有完整權限）</p>
    <div v-for="(row, index) in rows" :key="index" class="row">
      <UserSelect :value="row.userId || undefined" :exclude-ids="[project.owner.id]" placeholder="選擇成員" @update:value="(value) => (row.userId = value ?? '')" />
      <a-select v-model:value="row.projectRole" :options="roleOptions" />
      <a-button type="text" danger @click="rows.splice(index, 1)">移除</a-button>
    </div>
    <a-button type="dashed" block @click="rows.push({ userId: '', projectRole: 'member' })">新增成員</a-button>
  </a-modal>
</template>

<style scoped>
.hint {
  color: #888;
}
.row {
  display: grid;
  grid-template-columns: 1fr 190px auto;
  gap: 8px;
  margin-bottom: 8px;
}
@media (max-width: 575px) {
  .row {
    grid-template-columns: 1fr auto;
  }
}
</style>
