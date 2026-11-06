<script setup lang="ts">
/**
 * @component CompanySettingsView
 * 公司參數：
 * - 報價單抬頭：英文名稱、電話、地址、Logo（公司名稱與統編屬於公司身分，由平台管理員維護，這裡只顯示）。
 * - 報價參數：預設稅率、幣別、有效天數、報價單號規則、預設條款（條列編輯），以及 Dashboard 的會計年度起始月。
 */
import { computed, reactive, ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import { useQuery, useQueryClient } from '@tanstack/vue-query';
import TermsEditor from '@/app/components/TermsEditor.vue';
import { useMe } from '@/app/composables/use-me';
import { getCompanySettingsApi, previewNumberPattern, updateCompanySettingsApi, uploadCompanyLogoApi } from '@/shared/services/dashboard-service';

const { can } = useMe();
const editable = computed(() => can('company.settings', 'update'));
const queryClient = useQueryClient();
const settings = useQuery({ queryKey: ['company-settings'], queryFn: getCompanySettingsApi });
const form = reactive({
  defaultTaxRate: 5,
  defaultCurrency: 'TWD',
  quoteValidDays: 30,
  defaultTerms: null as string | null,
  fiscalYearStartMonth: 1,
  quoteNumberPattern: '',
  englishName: '',
  phone: '',
  address: '',
});
const saving = ref(false);
const uploadingLogo = ref(false);
const monthOptions = Array.from({ length: 12 }, (_, index) => ({ value: index + 1, label: `${index + 1} 月` }));
const patternExamples = [
  { pattern: 'Q{yyyy}{MM}-{seq:4}', note: '每月重新編號' },
  { pattern: 'DB-{yyyy}{MM}{dd}-{seq:2}', note: '每天重新編號' },
  { pattern: 'QT{yy}-{seq:5}', note: '每年重新編號' },
];
const numberPreview = computed(() => (form.quoteNumberPattern.trim() ? previewNumberPattern(form.quoteNumberPattern.trim()) : '—'));

watch(
  () => settings.data.value,
  (value) => {
    if (!value) return;
    const { defaultTaxRate, defaultCurrency, quoteValidDays, defaultTerms, fiscalYearStartMonth, quoteNumberPattern, company } = value;
    Object.assign(form, {
      defaultTaxRate,
      defaultCurrency,
      quoteValidDays,
      defaultTerms,
      fiscalYearStartMonth,
      quoteNumberPattern,
      englishName: company.englishName ?? '',
      phone: company.phone ?? '',
      address: company.address ?? '',
    });
  },
  { immediate: true },
);

const refresh = () => queryClient.invalidateQueries({ queryKey: ['company-settings'] });

async function save() {
  saving.value = true;
  try {
    await updateCompanySettingsApi({ ...form });
    message.success('已儲存');
    refresh();
    queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

/** 選好圖片就上傳並套用（不必再按儲存）。 */
async function onLogoSelected(file: File) {
  if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
    message.error('Logo 只接受 PNG、JPG 或 WebP 圖片。');
    return false;
  }
  if (file.size > 2 * 1024 * 1024) {
    message.error('Logo 圖片不可超過 2 MB。');
    return false;
  }
  uploadingLogo.value = true;
  try {
    const logoFileId = await uploadCompanyLogoApi(file);
    await updateCompanySettingsApi({ ...form, logoFileId });
    message.success('Logo 已更新');
    refresh();
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    uploadingLogo.value = false;
  }
  return false;
}

async function removeLogo() {
  try {
    await updateCompanySettingsApi({ ...form, removeLogo: true });
    message.success('已移除 Logo');
    refresh();
  } catch (error) {
    message.error((error as Error).message);
  }
}
</script>

<template>
  <a-form :model="form" layout="vertical" :disabled="!editable" @finish="save">
    <a-space direction="vertical" :size="16" style="width: 100%">
      <a-card :bordered="false" title="報價單抬頭" :loading="settings.isLoading.value">
        <a-row :gutter="24">
          <a-col :xs="24" :md="14">
            <a-descriptions v-if="settings.data.value" :column="1" size="small" style="margin-bottom: 12px">
              <a-descriptions-item label="公司名稱">{{ settings.data.value.company.name }}</a-descriptions-item>
              <a-descriptions-item label="統一編號">{{ settings.data.value.company.taxId ?? '—' }}</a-descriptions-item>
            </a-descriptions>
            <div class="hint" style="margin-bottom: 12px">公司名稱與統編由平台管理員維護；以下欄位會印在報價單抬頭。</div>
            <a-form-item label="英文名稱" name="englishName">
              <a-input v-model:value="form.englishName" :maxlength="200" placeholder="例如 EMMT Systems Corporation" />
            </a-form-item>
            <a-row :gutter="16">
              <a-col :xs="24" :sm="10">
                <a-form-item label="電話" name="phone"><a-input v-model:value="form.phone" :maxlength="50" /></a-form-item>
              </a-col>
              <a-col :xs="24" :sm="14">
                <a-form-item label="地址" name="address"><a-input v-model:value="form.address" :maxlength="500" /></a-form-item>
              </a-col>
            </a-row>
          </a-col>
          <a-col :xs="24" :md="10">
            <div class="logo-label">Logo</div>
            <div class="logo-box">
              <img v-if="settings.data.value?.company.logoUrl" :src="settings.data.value.company.logoUrl" alt="目前的公司 Logo" />
              <span v-else class="hint">尚未設定 Logo</span>
            </div>
            <a-space v-if="editable" style="margin-top: 8px">
              <a-upload :before-upload="onLogoSelected" :show-upload-list="false" accept=".png,.jpg,.jpeg,.webp">
                <a-button :loading="uploadingLogo">{{ settings.data.value?.company.logoUrl ? '更換 Logo' : '上傳 Logo' }}</a-button>
              </a-upload>
              <a-popconfirm v-if="settings.data.value?.company.logoUrl" title="移除 Logo？報價單抬頭將不再顯示。" @confirm="removeLogo">
                <a-button danger>移除</a-button>
              </a-popconfirm>
            </a-space>
            <div class="hint" style="margin-top: 6px">PNG、JPG 或 WebP，2 MB 以內；建議橫式、背景透明。</div>
          </a-col>
        </a-row>
      </a-card>

      <a-card :bordered="false" title="報價參數" :loading="settings.isLoading.value">
        <a-row :gutter="16" style="max-width: 760px">
          <a-col :xs="24" :sm="12" :md="6">
            <a-form-item label="預設稅率（%）" name="defaultTaxRate">
              <a-input-number v-model:value="form.defaultTaxRate" :min="0" :max="100" :precision="2" style="width: 100%" />
            </a-form-item>
          </a-col>
          <a-col :xs="24" :sm="12" :md="6">
            <a-form-item label="預設幣別" name="defaultCurrency">
              <a-input v-model:value="form.defaultCurrency" :maxlength="3" placeholder="TWD" />
            </a-form-item>
          </a-col>
          <a-col :xs="24" :sm="12" :md="6">
            <a-form-item label="報價有效天數" name="quoteValidDays">
              <a-input-number v-model:value="form.quoteValidDays" :min="1" :max="365" style="width: 100%" />
            </a-form-item>
          </a-col>
          <a-col :xs="24" :sm="12" :md="6">
            <a-form-item label="會計年度起始月" name="fiscalYearStartMonth" extra="Dashboard 的年度與季度依此計算">
              <a-select v-model:value="form.fiscalYearStartMonth" :options="monthOptions" />
            </a-form-item>
          </a-col>
          <a-col :span="24">
            <a-form-item label="報價單號規則" name="quoteNumberPattern">
              <a-input v-model:value="form.quoteNumberPattern" :maxlength="40" style="max-width: 320px" />
              <div class="pattern-preview">格式範例（流水號以 1 示意）：<strong>{{ numberPreview }}</strong></div>
              <div class="hint">
                代號：{yyyy} 西元年、{yy} 年後兩碼、{MM} 月、{dd} 日、{seq:4} 流水號（補到 4 碼）。一定要有流水號；有 {dd} 每天重新編號、有 {MM} 每月、只有年份每年。
              </div>
              <a-space wrap size="small" style="margin-top: 4px">
                <a-tag v-for="example in patternExamples" :key="example.pattern" class="pattern-tag" @click="editable && (form.quoteNumberPattern = example.pattern)">
                  {{ example.pattern }}（{{ example.note }}）
                </a-tag>
              </a-space>
            </a-form-item>
          </a-col>
        </a-row>
        <a-form-item label="報價預設條款（新報價自動帶入，編號自動產生）" name="defaultTerms">
          <TermsEditor v-model="form.defaultTerms" :readonly="!editable" empty-text="沒有預設條款" />
        </a-form-item>
      </a-card>

      <a-button v-if="editable" type="primary" html-type="submit" :loading="saving">儲存</a-button>
    </a-space>
  </a-form>
</template>

<style scoped>
.hint {
  color: #8c8c8c;
  font-size: 12px;
}
.logo-label {
  margin-bottom: 8px;
}
.logo-box {
  height: 96px;
  border: 1px dashed #d9d9d9;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #fafafa;
  padding: 8px;
}
.logo-box img {
  max-height: 100%;
  max-width: 100%;
  object-fit: contain;
}
.pattern-preview {
  margin-top: 6px;
}
.pattern-tag {
  cursor: pointer;
}
</style>
