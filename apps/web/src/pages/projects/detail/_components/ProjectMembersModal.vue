<script setup lang="ts">
/**
 * @component ProjectMembersModal
 * 整批編輯專案成員。manager／member 可修改專案，viewer 只能檢視；負責人固定有完整權限，不在清單內。
 * 新成員從名單（MemberPickerModal）依姓名、部門、角色篩選後勾選加入，其他部門（例如財務）的人也可以加。
 */
import { computed, ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import MemberPickerModal from '@/app/components/MemberPickerModal.vue';
import { replaceProjectMembersApi, type DirectoryMember, type ProjectDetail, type ProjectMember } from '@/shared/services/business-service';

const props = defineProps<{ open: boolean; project: ProjectDetail }>();
const emit = defineEmits<{ close: []; saved: [] }>();

const roleOptions = [
  { value: 'manager', label: '管理（可改專案與成員）' },
  { value: 'member', label: '成員（可改專案）' },
  { value: 'viewer', label: '檢視（只能看）' },
];

const rows = ref<ProjectMember[]>([]);
const saving = ref(false);
const pickerOpen = ref(false);
const excludeIds = computed(() => [props.project.owner.id, ...rows.value.map((row) => row.userId)]);

function addMembers(picked: DirectoryMember[]) {
  for (const user of picked) {
    if (!rows.value.some((row) => row.userId === user.userId)) rows.value.push({ userId: user.userId, displayName: user.displayName, departmentName: user.departmentName, projectRole: 'member' });
  }
}

watch(
  () => props.open,
  (open) => {
    if (open) rows.value = props.project.members.map((member) => ({ userId: member.userId, displayName: member.displayName, departmentName: member.departmentName, projectRole: member.projectRole }));
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
    <div v-for="(row, index) in rows" :key="row.userId" class="row">
      <span class="name">{{ row.displayName }}<span v-if="row.departmentName" class="dept">（{{ row.departmentName }}）</span></span>
      <a-select v-model:value="row.projectRole" :options="roleOptions" />
      <a-button type="text" danger @click="rows.splice(index, 1)">移除</a-button>
    </div>
    <a-empty v-if="!rows.length" description="還沒有成員" :image-style="{ height: '40px' }" />
    <a-button type="dashed" block @click="pickerOpen = true">從名單選擇成員</a-button>
    <MemberPickerModal :open="pickerOpen" :exclude-ids="excludeIds" @close="pickerOpen = false" @pick="addMembers" />
  </a-modal>
</template>

<style scoped>
.hint {
  color: #888;
}
.name {
  align-self: center;
}
.dept {
  color: #8c8c8c;
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
