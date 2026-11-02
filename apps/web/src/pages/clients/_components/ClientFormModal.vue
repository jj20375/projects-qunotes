<script setup lang="ts">
/**
 * @component ClientFormModal
 * 客戶新增／編輯／檢視：基本資料、發票資訊與聯絡人清單。
 * 沒有修改權限（canEdit=false）時整份表單唯讀。
 * 客戶所屬部門決定誰看得到這個客戶：新增時預設為自己的部門，只有全公司範圍的管理者可以選擇或移動。
 */
import { computed, reactive, ref, watch } from 'vue';
import { useQuery } from '@tanstack/vue-query';
import { message, Modal } from 'ant-design-vue';
import UserSelect from '@/app/components/UserSelect.vue';
import { useMe } from '@/app/composables/use-me';
import {
  createClientApi,
  deleteClientApi,
  getClientApi,
  listDirectoryDepartmentsApi,
  updateClientApi,
  type ClientContact,
  type ClientDetail,
} from '@/shared/services/business-service';

const props = defineProps<{ open: boolean; clientId: string | null }>();
const emit = defineEmits<{ close: []; saved: [] }>();
const { me, can } = useMe();


const empty = () => ({
  code: '',
  name: '',
  shortName: '',
  taxId: '',
  industry: '',
  phone: '',
  fax: '',
  email: '',
  address: '',
  invoiceTitle: '',
  invoiceAddress: '',
  paymentTerms: '',
  ownerUserId: undefined as string | undefined,
  departmentId: undefined as string | undefined,
  status: 'active' as 'active' | 'inactive',
  note: '',
});
const form = reactive(empty());
const contacts = ref<ClientContact[]>([]);
const detail = ref<ClientDetail | null>(null);
const loading = ref(false);
const saving = ref(false);
const readonly = computed(() => (props.clientId ? !detail.value?.canEdit : !can('clients', 'create')));
// 全公司範圍的管理者才能選部門（後端也會檢查）；其他人新增時自動歸在自己的部門。
const canPickDepartment = computed(() => (props.clientId ? !!detail.value?.canChangeDepartment : me.value?.scope.dataScope === 'company'));
const departments = useQuery({ queryKey: ['directory', 'departments'], queryFn: listDirectoryDepartmentsApi, enabled: canPickDepartment });
const departmentTree = computed(() => {
  const rows = departments.data.value ?? [];
  const build = (parentId: string | null): unknown[] => rows.filter((row) => row.parentId === parentId).map((row) => ({ value: row.id, title: row.name, children: build(row.id) }));
  return build(null);
});

watch(
  () => [props.open, props.clientId] as const,
  async ([open, clientId]) => {
    if (!open) return;
    detail.value = null;
    Object.assign(form, empty(), { ownerUserId: me.value?.user.id });
    contacts.value = [];
    if (!clientId) return;
    loading.value = true;
    try {
      const result = await getClientApi(clientId);
      detail.value = result;
      Object.assign(form, {
        ...Object.fromEntries(Object.keys(empty()).map((key) => [key, (result as unknown as Record<string, unknown>)[key] ?? ''])),
        ownerUserId: result.ownerUserId ?? undefined,
        departmentId: result.departmentId ?? undefined,
        status: result.status,
      });
      contacts.value = result.contacts.map((contact) => ({ ...contact }));
    } catch (error) {
      message.error((error as Error).message);
    } finally {
      loading.value = false;
    }
  },
);

function addContact() {
  contacts.value.push({ name: '', title: '', phone: '', mobile: '', email: '', isPrimary: contacts.value.length === 0 });
}

function setPrimary(index: number) {
  contacts.value.forEach((contact, i) => (contact.isPrimary = i === index));
}

function removeContact(index: number) {
  const wasPrimary = contacts.value[index].isPrimary;
  contacts.value.splice(index, 1);
  if (wasPrimary && contacts.value.length) contacts.value[0].isPrimary = true;
}

async function save() {
  saving.value = true;
  try {
    const body = { ...form, contacts: contacts.value };
    if (props.clientId) await updateClientApi(props.clientId, body);
    else await createClientApi(body);
    message.success('客戶已儲存');
    emit('saved');
    emit('close');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

function confirmDelete() {
  Modal.confirm({
    title: `刪除客戶「${detail.value?.name}」？`,
    content: '已有專案的客戶不能刪除，請改為停用。',
    okType: 'danger',
    onOk: async () => {
      try {
        await deleteClientApi(props.clientId!);
        message.success('已刪除');
        emit('saved');
        emit('close');
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}
</script>

<template>
  <a-modal :open="open" :width="860" :title="clientId ? `客戶：${detail?.name ?? ''}` : '新增客戶'" @cancel="emit('close')">
    <a-spin :spinning="loading">
      <a-alert v-if="clientId && detail && !detail.canEdit" type="info" show-icon message="這位客戶由其他人負責，您只能檢視。" style="margin-bottom: 12px" />
      <a-form layout="vertical" :disabled="readonly">
        <a-row :gutter="16">
          <a-col :xs="24" :md="8"><a-form-item label="客戶代碼" extra="留空自動產生"><a-input v-model:value="form.code" /></a-form-item></a-col>
          <a-col :xs="24" :md="16"><a-form-item label="客戶名稱" required><a-input v-model:value="form.name" /></a-form-item></a-col>
          <a-col :xs="24" :md="8"><a-form-item label="簡稱"><a-input v-model:value="form.shortName" /></a-form-item></a-col>
          <a-col :xs="24" :md="8"><a-form-item label="統一編號"><a-input v-model:value="form.taxId" :maxlength="8" /></a-form-item></a-col>
          <a-col :xs="24" :md="8"><a-form-item label="產業"><a-input v-model:value="form.industry" /></a-form-item></a-col>
          <a-col :xs="24" :md="8"><a-form-item label="電話"><a-input v-model:value="form.phone" /></a-form-item></a-col>
          <a-col :xs="24" :md="8"><a-form-item label="傳真"><a-input v-model:value="form.fax" /></a-form-item></a-col>
          <a-col :xs="24" :md="8"><a-form-item label="Email"><a-input v-model:value="form.email" /></a-form-item></a-col>
          <a-col :span="24"><a-form-item label="地址"><a-input v-model:value="form.address" /></a-form-item></a-col>
          <a-col :xs="24" :md="12"><a-form-item label="發票抬頭"><a-input v-model:value="form.invoiceTitle" /></a-form-item></a-col>
          <a-col :xs="24" :md="12"><a-form-item label="付款條件"><a-input v-model:value="form.paymentTerms" placeholder="例如：月結 30 天" /></a-form-item></a-col>
          <a-col :span="24"><a-form-item label="發票地址"><a-input v-model:value="form.invoiceAddress" /></a-form-item></a-col>
          <a-col :xs="24" :md="12">
            <a-form-item label="業務負責人"><UserSelect v-model:value="form.ownerUserId" /></a-form-item>
          </a-col>
          <a-col :xs="24" :md="12">
            <a-form-item label="所屬部門" extra="同部門與上層部門的人看得到這位客戶">
              <a-tree-select v-if="canPickDepartment" v-model:value="form.departmentId" :tree-data="departmentTree" tree-default-expand-all placeholder="預設為自己的部門" />
              <span v-else>{{ detail?.departmentName ?? '自己的部門' }}</span>
            </a-form-item>
          </a-col>
          <a-col :xs="24" :md="12">
            <a-form-item label="狀態">
              <a-radio-group v-model:value="form.status">
                <a-radio-button value="active">往來中</a-radio-button>
                <a-radio-button value="inactive">停用</a-radio-button>
              </a-radio-group>
            </a-form-item>
          </a-col>
          <a-col :span="24"><a-form-item label="備註"><a-textarea v-model:value="form.note" :rows="2" /></a-form-item></a-col>
        </a-row>

        <a-divider orientation="left" plain>聯絡人</a-divider>
        <div class="contacts">
          <div v-for="(contact, index) in contacts" :key="contact.id ?? index" class="contact-row">
            <a-input v-model:value="contact.name" placeholder="姓名" />
            <a-input v-model:value="contact.title" placeholder="職稱" />
            <a-input v-model:value="contact.phone" placeholder="電話" />
            <a-input v-model:value="contact.mobile" placeholder="手機" />
            <a-input v-model:value="contact.email" placeholder="Email" />
            <a-radio :checked="contact.isPrimary" @change="setPrimary(index)">主要</a-radio>
            <a-button type="text" danger @click="removeContact(index)">移除</a-button>
          </div>
          <a-button type="dashed" block @click="addContact">新增聯絡人</a-button>
        </div>
      </a-form>
    </a-spin>
    <template #footer>
      <a-button v-if="detail?.canDelete" danger style="float: left" @click="confirmDelete">刪除</a-button>
      <a-button @click="emit('close')">{{ readonly ? '關閉' : '取消' }}</a-button>
      <a-button v-if="!readonly" type="primary" :loading="saving" @click="save">儲存</a-button>
    </template>
  </a-modal>
</template>

<style scoped>
.contacts {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.contact-row {
  display: grid;
  grid-template-columns: 1.1fr 1fr 1fr 1fr 1.6fr auto auto;
  gap: 8px;
  align-items: center;
}
@media (max-width: 767px) {
  .contact-row {
    grid-template-columns: 1fr 1fr;
    padding-bottom: 8px;
    border-bottom: 1px dashed #eee;
  }
}
</style>
