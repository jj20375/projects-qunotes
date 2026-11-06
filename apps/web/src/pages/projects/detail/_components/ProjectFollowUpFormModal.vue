<script setup lang="ts">
/**
 * @component ProjectFollowUpFormModal
 * 新增／編輯一筆跟進紀錄。對象可從專案客戶的聯絡人選（由後端依關鍵字搜尋），也可直接輸入姓名。
 * 前端檢查只是提示，最終以 API 的驗證為準。
 */
import { reactive, ref, watch } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { message } from 'ant-design-vue';
import {
  createFollowUpApi,
  FOLLOW_UP_TYPES,
  listFollowUpContactsApi,
  updateFollowUpApi,
  type FollowUp,
  type FollowUpInput,
  type FollowUpProjectState,
  type FollowUpType,
} from '@/shared/services/followup-service';

const props = defineProps<{ open: boolean; projectId: string; record: FollowUp | null }>();
const emit = defineEmits<{ close: []; saved: [state: FollowUpProjectState] }>();

const form = reactive({
  type: 'call' as FollowUpType,
  occurredAt: dayjs() as Dayjs,
  contactId: null as string | null,
  contactName: '' as string | undefined,
  summary: '',
  content: '',
  nextFollowUpOn: null as Dayjs | null,
  nextAction: '',
});
const saving = ref(false);
const contactOptions = ref<{ value: string; label: string; contactId: string; name: string }[]>([]);
// 只看不帶關鍵字的那次查詢：打字查不到不代表客戶沒有聯絡人。
const noContacts = ref(false);
const quickNext = [
  { label: '明天', days: 1 },
  { label: '3 天後', days: 3 },
  { label: '下週', days: 7 },
  { label: '2 週後', days: 14 },
];
let contactTimer: ReturnType<typeof setTimeout> | undefined;

watch(
  () => props.open,
  (open) => {
    if (!open) return;
    const row = props.record;
    Object.assign(form, {
      type: row?.type ?? 'call',
      occurredAt: row ? dayjs(row.occurredAt) : dayjs(),
      contactId: row?.contactId ?? null,
      contactName: row?.contactName ?? '',
      summary: row?.summary ?? '',
      content: row?.content ?? '',
      nextFollowUpOn: row?.nextFollowUpOn ? dayjs(row.nextFollowUpOn) : null,
      nextAction: row?.nextAction ?? '',
    });
    noContacts.value = false;
    searchContacts('');
  }
);

// 聯絡人由後端依關鍵字查詢，不在前端過濾整份名單。
function searchContacts(keyword: string) {
  clearTimeout(contactTimer);
  contactTimer = setTimeout(async () => {
    try {
      const contacts = await listFollowUpContactsApi(props.projectId, keyword);
      // 選單以 value 對應選項，同名聯絡人若用姓名當 value 會選到另一位，所以 value 用 ID，選取後再把姓名寫回輸入框。
      contactOptions.value = contacts.map((contact) => ({
        value: contact.id,
        label: contact.title ? `${contact.name}（${contact.title}）` : contact.name,
        contactId: contact.id,
        name: contact.name,
      }));
      if (!keyword) noContacts.value = contacts.length === 0;
    } catch {
      contactOptions.value = [];
    }
  }, 250);
}

// 自己輸入的姓名就不是聯絡人；只有從選單選到的才帶 contactId。
function onContactSearch(keyword: string) {
  form.contactId = null;
  searchContacts(keyword);
}

// 選取時 v-model 會先收到 value（聯絡人 ID），這裡接著改成姓名。
function onContactSelect(_value: unknown, option: unknown) {
  const selected = option as { contactId: string; name: string };
  form.contactId = selected.contactId;
  form.contactName = selected.name;
}

function onContactChange(value: unknown) {
  if (!value) form.contactId = null;
}

function setNext(days: number) {
  form.nextFollowUpOn = dayjs().add(days, 'day');
}

async function save() {
  if (!form.summary.trim()) {
    message.warning('請輸入主旨');
    return;
  }
  saving.value = true;
  try {
    const input: FollowUpInput = {
      type: form.type,
      occurredAt: form.occurredAt.toISOString(),
      contactId: form.contactId,
      contactName: (form.contactName ?? '').trim() || null,
      summary: form.summary.trim(),
      content: form.content.trim() || null,
      nextFollowUpOn: form.nextFollowUpOn?.format('YYYY-MM-DD') ?? null,
      nextAction: form.nextFollowUpOn ? form.nextAction.trim() || null : null,
    };
    const result = props.record ? await updateFollowUpApi(props.record.id, input) : await createFollowUpApi(props.projectId, input);
    message.success('跟進紀錄已儲存');
    emit('saved', result.project);
    emit('close');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <a-modal :open="open" :title="record ? '編輯跟進紀錄' : '新增跟進紀錄'" :confirm-loading="saving" ok-text="儲存" width="600px" @ok="save" @cancel="emit('close')">
    <a-form layout="vertical">
      <a-form-item label="類型" required>
        <a-radio-group v-model:value="form.type" button-style="solid">
          <a-radio-button v-for="option in FOLLOW_UP_TYPES" :key="option.value" :value="option.value">{{ option.label }}</a-radio-button>
        </a-radio-group>
      </a-form-item>
      <a-row :gutter="12">
        <a-col :xs="24" :sm="12">
          <a-form-item label="發生時間" required>
            <a-date-picker
              v-model:value="form.occurredAt"
              show-time
              format="YYYY-MM-DD HH:mm"
              :allow-clear="false"
              :disabled-date="(day: Dayjs) => day.isAfter(dayjs(), 'day')"
              style="width: 100%"
            />
          </a-form-item>
        </a-col>
        <a-col :xs="24" :sm="12">
          <a-form-item label="對象" :extra="noContacts ? '此客戶尚未建立聯絡人，可直接輸入姓名' : undefined">
            <a-auto-complete
              v-model:value="form.contactName"
              :options="contactOptions"
              :filter-option="false"
              allow-clear
              placeholder="選聯絡人或輸入姓名"
              @search="onContactSearch"
              @select="onContactSelect"
              @change="onContactChange"
            />
          </a-form-item>
        </a-col>
      </a-row>
      <a-form-item label="主旨" required>
        <a-input v-model:value="form.summary" :maxlength="200" show-count placeholder="例如：確認規格、報價後追蹤" />
      </a-form-item>
      <a-form-item label="內容">
        <a-textarea v-model:value="form.content" :maxlength="4000" show-count :auto-size="{ minRows: 3, maxRows: 10 }" />
      </a-form-item>
      <a-form-item label="下次聯絡日">
        <a-space wrap>
          <a-date-picker v-model:value="form.nextFollowUpOn" :disabled-date="(day: Dayjs) => day.isBefore(form.occurredAt, 'day')" />
          <a-button v-for="item in quickNext" :key="item.days" size="small" @click="setNext(item.days)">{{ item.label }}</a-button>
        </a-space>
      </a-form-item>
      <a-form-item label="下次要做的事">
        <a-input v-model:value="form.nextAction" :maxlength="200" :disabled="!form.nextFollowUpOn" placeholder="例如：寄修正版報價、確認交期" />
      </a-form-item>
    </a-form>
  </a-modal>
</template>
