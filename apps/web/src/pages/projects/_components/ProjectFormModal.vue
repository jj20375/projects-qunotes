<script setup lang="ts">
/**
 * @component ProjectFormModal
 * 專案新增／編輯。部門只列出自己資料範圍內可選的部門由後端把關（超出會回 400）；
 * 新增時可一併指定成員（需 projects:assign_member）。
 */
import { computed, reactive, ref, watch } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { message } from 'ant-design-vue';
import { useQuery } from '@tanstack/vue-query';
import ClientSelect from '@/app/components/ClientSelect.vue';
import UserSelect from '@/app/components/UserSelect.vue';
import { useMe } from '@/app/composables/use-me';
import {
  createProjectApi,
  listDirectoryDepartmentsApi,
  updateProjectApi,
  type ProjectDetail,
  type ProjectMember,
} from '@/shared/services/business-service';

const props = defineProps<{ open: boolean; project?: ProjectDetail | null; defaultClientId?: string }>();
const emit = defineEmits<{ close: []; saved: [id: string] }>();
const { me, canOption } = useMe();

const departments = useQuery({ queryKey: ['directory-departments'], queryFn: listDirectoryDepartmentsApi });
const departmentTree = computed(() => {
  const rows = departments.data.value ?? [];
  const build = (parentId: string | null): unknown[] => rows.filter((row) => row.parentId === parentId).map((row) => ({ value: row.id, title: row.name, children: build(row.id) }));
  return build(null);
});

const form = reactive({
  name: '',
  clientId: undefined as string | undefined,
  departmentId: undefined as string | undefined,
  ownerUserId: undefined as string | undefined,
  period: [] as Dayjs[] | [],
  expectedAmount: null as number | null,
  contractAmount: null as number | null,
  contractTotal: null as number | null,
  description: '',
});
const members = ref<ProjectMember[]>([]);
const saving = ref(false);
const isEdit = computed(() => !!props.project);
const canAssignMembers = computed(() => canOption('projects', 'assign_member'));

watch(
  () => props.open,
  (open) => {
    if (!open) return;
    const project = props.project;
    Object.assign(form, {
      name: project?.name ?? '',
      clientId: project?.client.id ?? props.defaultClientId,
      departmentId: project ? project.departmentId ?? undefined : me.value?.scope.departmentId ?? undefined,
      ownerUserId: project?.owner.id ?? me.value?.user.id,
      period: project?.startDate || project?.endDate ? [project.startDate ? dayjs(project.startDate) : undefined, project.endDate ? dayjs(project.endDate) : undefined] : [],
      expectedAmount: project?.expectedAmount ?? null,
      contractAmount: project?.contractAmount ?? null,
      contractTotal: project?.contractTotal ?? null,
      description: project?.description ?? '',
    });
    members.value = [];
  },
);

async function save() {
  if (!form.name.trim() || !form.clientId) {
    message.warning('請輸入專案名稱並選擇客戶');
    return;
  }
  saving.value = true;
  try {
    const [start, end] = form.period as (Dayjs | undefined)[];
    const body = {
      name: form.name,
      clientId: form.clientId,
      departmentId: form.departmentId,
      ownerUserId: form.ownerUserId,
      startDate: start ? start.format('YYYY-MM-DD') : null,
      endDate: end ? end.format('YYYY-MM-DD') : null,
      expectedAmount: form.expectedAmount,
      contractAmount: form.contractAmount,
      contractTotal: form.contractTotal,
      description: form.description,
      members: isEdit.value ? undefined : members.value.filter((member) => member.userId),
    };
    let id = props.project?.id;
    if (isEdit.value) await updateProjectApi(id!, body);
    else id = (await createProjectApi(body)).id;
    message.success(isEdit.value ? '專案已更新' : '專案已建立');
    emit('saved', id!);
    emit('close');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

const moneyFormatter = (value: number | string) => `${value}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
const moneyParser = (value: string) => value.replace(/,/g, '');
</script>

<template>
  <a-modal :open="open" :width="760" :title="isEdit ? `編輯專案 ${project?.projectNo}` : '新增專案'" :confirm-loading="saving" ok-text="儲存" @ok="save" @cancel="emit('close')">
    <a-form layout="vertical">
      <a-form-item label="專案名稱" required><a-input v-model:value="form.name" :maxlength="200" /></a-form-item>
      <a-form-item label="客戶" required>
        <ClientSelect v-model:value="form.clientId" :initial-label="project ? `${project.client.code} ${project.client.name}` : undefined" active-only />
      </a-form-item>
      <a-row :gutter="16">
        <a-col :xs="24" :md="12">
          <a-form-item label="所屬部門" extra="決定部門主管是否看得到此專案"><a-tree-select v-model:value="form.departmentId" :tree-data="departmentTree" tree-default-expand-all /></a-form-item>
        </a-col>
        <a-col :xs="24" :md="12">
          <a-form-item label="負責人"><UserSelect v-model:value="form.ownerUserId" /></a-form-item>
        </a-col>
        <a-col :xs="24" :md="12">
          <a-form-item label="工期"><a-range-picker v-model:value="form.period" :allow-empty="[true, true]" style="width: 100%" /></a-form-item>
        </a-col>
        <a-col :xs="24" :md="12">
          <a-form-item label="預估金額（未稅）">
            <a-input-number v-model:value="form.expectedAmount" :min="0" :precision="0" :formatter="moneyFormatter" :parser="moneyParser" style="width: 100%" />
          </a-form-item>
        </a-col>
        <template v-if="isEdit">
          <a-col :xs="24" :md="12">
            <a-form-item label="成交金額（未稅）" extra="報價單功能上線後由接受的報價自動帶入">
              <a-input-number v-model:value="form.contractAmount" :min="0" :precision="0" :formatter="moneyFormatter" :parser="moneyParser" style="width: 100%" />
            </a-form-item>
          </a-col>
          <a-col :xs="24" :md="12">
            <a-form-item label="成交金額（含稅）">
              <a-input-number v-model:value="form.contractTotal" :min="0" :precision="0" :formatter="moneyFormatter" :parser="moneyParser" style="width: 100%" />
            </a-form-item>
          </a-col>
        </template>
      </a-row>
      <a-form-item label="說明"><a-textarea v-model:value="form.description" :rows="3" /></a-form-item>

      <template v-if="!isEdit && canAssignMembers">
        <a-divider orientation="left" plain>專案成員（負責人不用加入）</a-divider>
        <div v-for="(member, index) in members" :key="index" class="member-row">
          <UserSelect :value="member.userId || undefined" :exclude-ids="form.ownerUserId ? [form.ownerUserId] : []" placeholder="選擇成員" @update:value="(value) => (member.userId = value ?? '')" />
          <a-select v-model:value="member.projectRole" :options="[{ value: 'manager', label: '管理' }, { value: 'member', label: '成員' }, { value: 'viewer', label: '檢視' }]" />
          <a-button type="text" danger @click="members.splice(index, 1)">移除</a-button>
        </div>
        <a-button type="dashed" block @click="members.push({ userId: '', projectRole: 'member' })">新增成員</a-button>
      </template>
    </a-form>
  </a-modal>
</template>

<style scoped>
.member-row {
  display: grid;
  grid-template-columns: 1fr 120px auto;
  gap: 8px;
  margin-bottom: 8px;
}
</style>
