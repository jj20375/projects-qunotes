<script setup lang="ts">
/**
 * @component UserPermissionsDrawer
 * 個人權限：在角色之外對個別資源「加權」或「禁止」，可設到期日做臨時授權。
 * 右側「有效權限」顯示合併結果：(角色 ∪ 加權) − 禁止，再受公司管理員上限限制。
 */
import { computed, ref, watch } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { message } from 'ant-design-vue';
import DataTable from '@/app/components/DataTable.vue';
import PermissionMatrix from '@/app/components/PermissionMatrix.vue';
import { useMe } from '@/app/composables/use-me';
import {
  fromAllowanceMap,
  getMemberPermissionsApi,
  getPermissionCatalogApi,
  replaceMemberPermissionsApi,
  toAllowanceMap,
  type MemberPermissions,
  type MemberRow,
  type UserPermissionOverride,
} from '@/shared/services/admin-service';
import type { CatalogNode, PermissionAction, PermissionAllowance } from '@/shared/types/permissions';
import { dataColumns } from '@/shared/table/data-table';
import { ACTION_LABELS } from '@/shared/types/permissions';

const props = defineProps<{ member: MemberRow | null }>();
const emit = defineEmits<{ close: []; saved: [] }>();
const { me, canOption } = useMe();

const catalog = ref<CatalogNode[]>([]);
const data = ref<MemberPermissions | null>(null);
const grants = ref<Record<string, PermissionAllowance>>({});
const denies = ref<Record<string, PermissionAllowance>>({});
const reason = ref('');
const expiresAt = ref<Dayjs | undefined>();
const saving = ref(false);
const tab = ref<'grant' | 'deny' | 'effective'>('grant');
const editable = computed(() => canOption('company.users', 'assign_permission') && !props.member?.isSelf);

async function load(member: MemberRow) {
  const [catalogResult, permissionResult] = await Promise.all([catalog.value.length ? Promise.resolve(catalog.value) : getPermissionCatalogApi('company'), getMemberPermissionsApi(member.membershipId)]);
  catalog.value = catalogResult;
  data.value = permissionResult;
  grants.value = toAllowanceMap(permissionResult.overrides.filter((item) => item.effect === 'grant'));
  denies.value = toAllowanceMap(permissionResult.overrides.filter((item) => item.effect === 'deny'));
  const firstGrant = permissionResult.overrides.find((item) => item.effect === 'grant');
  reason.value = firstGrant?.reason ?? '';
  expiresAt.value = firstGrant?.expiresAt ? dayjs(firstGrant.expiresAt) : undefined;
  tab.value = 'grant';
}

watch(
  () => props.member,
  (member) => {
    if (member) load(member).catch((error: Error) => message.error(error.message));
  },
);

async function save() {
  if (!props.member) return;
  saving.value = true;
  try {
    // 加權套用同一組原因與到期日（臨時代理多半是一起給、一起收回）；禁止不設到期。
    const items: UserPermissionOverride[] = [
      ...fromAllowanceMap(grants.value).map((item) => ({ ...item, effect: 'grant' as const, reason: reason.value || null, expiresAt: expiresAt.value ? expiresAt.value.endOf('day').toISOString() : null })),
      ...fromAllowanceMap(denies.value).map((item) => ({ ...item, effect: 'deny' as const, reason: null, expiresAt: null })),
    ];
    await replaceMemberPermissionsApi(props.member.membershipId, items);
    message.success('個人權限已更新，該成員需重新取得登入權杖');
    await load(props.member);
    tab.value = 'effective';
    emit('saved');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

/** 有效權限比較表：標出個人加權新增與被禁止移除的動作。 */
const effectiveRows = computed(() => {
  if (!data.value) return [];
  const names = new Map<string, string>();
  const optionNames = new Map<string, string>();
  const walk = (nodes: CatalogNode[]) =>
    nodes.forEach((node) => {
      names.set(node.code, node.name);
      node.subFeatures.forEach((feature) => optionNames.set(`${node.code}:${feature.key}`, feature.name));
      walk(node.children);
    });
  walk(catalog.value);
  const codes = new Set([...data.value.roleGrants.map((grant) => grant.code), ...data.value.effective.map((grant) => grant.code)]);
  return [...codes].map((code) => {
    const role = data.value!.roleGrants.find((grant) => grant.code === code);
    const effective = data.value!.effective.find((grant) => grant.code === code);
    const items = (key: 'actions' | 'options') => {
      const all = new Set([...(role?.[key] ?? []), ...(effective?.[key] ?? [])]);
      return [...all].map((value) => ({
        value,
        label: key === 'actions' ? ACTION_LABELS[value as PermissionAction] : optionNames.get(`${code}:${value}`) ?? value,
        state: !effective?.[key].includes(value as never) ? 'removed' : !role?.[key].includes(value as never) ? 'added' : 'role',
      }));
    };
    return { code, name: names.get(code) ?? code, actions: items('actions'), options: items('options') };
  });
});

const effectiveCol = dataColumns<(typeof effectiveRows)['value'][number]>();
const effectiveColumns = [
  effectiveCol.accessor('name', { header: '資源', meta: { width: 140 } }),
  effectiveCol.accessor('actions', { header: '動作' }),
  effectiveCol.accessor('options', { header: '子功能' }),
];

const tagColor = (state: string) => (state === 'added' ? 'green' : state === 'removed' ? 'red' : 'default');
</script>

<template>
  <a-drawer :open="!!member" :width="880" :title="`個人權限：${member?.displayName ?? ''}（${member?.roleName ?? ''}）`" @close="emit('close')">
    <a-alert v-if="member?.isSelf" type="info" show-icon message="不可設定自己的個人權限。" style="margin-bottom: 16px" />
    <a-tabs v-model:active-key="tab">
      <a-tab-pane key="grant" tab="加權（額外開放）">
        <a-space direction="vertical" style="width: 100%" :size="12">
          <a-space wrap>
            <a-input v-model:value="reason" placeholder="原因，例如：代理主管核准" style="width: 280px" :disabled="!editable" />
            <a-date-picker v-model:value="expiresAt" placeholder="到期日（不填為永久）" :disabled="!editable" :disabled-date="(day: Dayjs) => day.isBefore(dayjs(), 'day')" />
          </a-space>
          <PermissionMatrix v-model="grants" :catalog="catalog" :disabled="!editable" :limit="me?.grants" tone="grant" />
        </a-space>
      </a-tab-pane>
      <a-tab-pane key="deny" tab="禁止（即使角色有也拿掉）">
        <PermissionMatrix v-model="denies" :catalog="catalog" :disabled="!editable" :limit="me?.grants" tone="deny" />
      </a-tab-pane>
      <a-tab-pane key="effective" tab="有效權限">
        <p class="legend"><a-tag>角色</a-tag><a-tag color="green">個人加權</a-tag><a-tag color="red">已禁止</a-tag></p>
        <DataTable :data="effectiveRows" :columns="effectiveColumns" row-key="code" size="small" :scroll-x="600">
          <template #cell="{ columnId, record }">
            <template v-if="columnId === 'actions' || columnId === 'options'">
              <a-tag v-for="item in record[columnId as 'actions' | 'options']" :key="item.value" :color="tagColor(item.state)" :class="{ removed: item.state === 'removed' }">
                {{ item.label }}
              </a-tag>
            </template>
          </template>
        </DataTable>
      </a-tab-pane>
    </a-tabs>
    <template v-if="editable && tab !== 'effective'" #footer>
      <a-space style="float: right">
        <a-button @click="emit('close')">取消</a-button>
        <a-button type="primary" :loading="saving" @click="save">儲存個人權限</a-button>
      </a-space>
    </template>
  </a-drawer>
</template>

<style scoped>
.legend {
  margin: 0 0 12px;
}
.removed {
  text-decoration: line-through;
}
</style>
