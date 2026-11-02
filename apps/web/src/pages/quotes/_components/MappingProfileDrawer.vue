<script setup lang="ts">
/**
 * @component MappingProfileDrawer
 * 舊報價 Excel 的對照設定：指定工作表、表頭列、資料起始列、各欄位的欄字母與表頭儲存格。
 * 大多數表格式報價不需要設定（系統會依表頭文字自動辨識）；只有格式特殊時才建立。
 */
import { reactive, ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import { deleteMappingProfileApi, listMappingProfilesApi, saveMappingProfileApi, type MappingProfile } from '@/shared/services/quote-service';

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ close: []; changed: [] }>();

const columnFields = [
  ['Name', '品名（必填）'],
  ['ItemCode', '品號'],
  ['Spec', '規格'],
  ['Unit', '單位'],
  ['Quantity', '數量'],
  ['UnitPrice', '單價'],
  ['DiscountRate', '折扣%'],
  ['Amount', '金額（只用來核對）'],
  ['UnitCost', '成本'],
  ['Remark', '備註'],
] as const;
const cellFields = [
  ['ClientName', '客戶名稱'],
  ['ClientTaxId', '統一編號'],
  ['ContactName', '聯絡人'],
  ['ContactPhone', '電話'],
  ['QuoteDate', '報價日期'],
  ['ValidUntil', '有效期限'],
  ['Title', '案名'],
  ['PaymentTerms', '付款條件'],
  ['Terms', '備註條款'],
] as const;

const profiles = ref<MappingProfile[]>([]);
const editing = ref<MappingProfile | null>(null);
const form = reactive<MappingProfile>({ name: '', sheetName: '', headerRow: 1, dataStartRow: 2, columns: {}, cells: {} });

async function load() {
  profiles.value = await listMappingProfilesApi();
}

watch(
  () => props.open,
  (open) => open && load().catch((error: Error) => message.error(error.message)),
);

function edit(profile: MappingProfile | null) {
  editing.value = profile ?? { name: '', headerRow: 1, dataStartRow: 2, columns: {}, cells: {} };
  Object.assign(form, JSON.parse(JSON.stringify(editing.value)));
}

async function save() {
  try {
    await saveMappingProfileApi({ ...form, id: editing.value?.id });
    message.success('對照設定已儲存');
    editing.value = null;
    await load();
    emit('changed');
  } catch (error) {
    message.error((error as Error).message);
  }
}

async function remove(profile: MappingProfile) {
  await deleteMappingProfileApi(profile.id!);
  await load();
  emit('changed');
}
</script>

<template>
  <a-drawer :open="open" title="舊報價對照設定" :width="560" @close="emit('close')">
    <template v-if="!editing">
      <a-alert type="info" show-icon message="一般表格式的舊報價不需要設定，系統會依「品名、數量、單價」等表頭文字自動辨識；辨識不到時再建立對照設定。" style="margin-bottom: 16px" />
      <a-list :data-source="profiles" bordered>
        <template #renderItem="{ item }">
          <a-list-item>
            <a-list-item-meta :title="item.name" :description="`表頭第 ${item.headerRow} 列，品名在 ${item.columns.Name} 欄`" />
            <a-button type="link" @click="edit(item)">編輯</a-button>
            <a-popconfirm title="刪除這個對照設定？" @confirm="remove(item)"><a-button type="link" danger>刪除</a-button></a-popconfirm>
          </a-list-item>
        </template>
      </a-list>
      <a-button type="dashed" block style="margin-top: 12px" @click="edit(null)">新增對照設定</a-button>
    </template>
    <a-form v-else layout="vertical">
      <a-form-item label="名稱" required><a-input v-model:value="form.name" placeholder="例如：舊系統 2019 版報價" /></a-form-item>
      <a-row :gutter="12">
        <a-col :span="8"><a-form-item label="工作表名稱"><a-input v-model:value="form.sheetName" placeholder="空白＝第一張" /></a-form-item></a-col>
        <a-col :span="8"><a-form-item label="表頭列"><a-input-number v-model:value="form.headerRow" :min="1" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="8"><a-form-item label="資料起始列"><a-input-number v-model:value="form.dataStartRow" :min="2" style="width: 100%" /></a-form-item></a-col>
      </a-row>
      <a-divider orientation="left" plain>明細欄位（填欄字母，例如 C）</a-divider>
      <a-row :gutter="12">
        <a-col v-for="[field, label] in columnFields" :key="field" :span="8">
          <a-form-item :label="label"><a-input v-model:value="form.columns[field]" :maxlength="3" /></a-form-item>
        </a-col>
      </a-row>
      <a-divider orientation="left" plain>表頭欄位（填儲存格，例如 B5）</a-divider>
      <a-row :gutter="12">
        <a-col v-for="[field, label] in cellFields" :key="field" :span="8">
          <a-form-item :label="label"><a-input v-model:value="form.cells[field]" :maxlength="9" /></a-form-item>
        </a-col>
      </a-row>
      <a-space>
        <a-button @click="editing = null">取消</a-button>
        <a-button type="primary" @click="save">儲存</a-button>
      </a-space>
    </a-form>
  </a-drawer>
</template>
