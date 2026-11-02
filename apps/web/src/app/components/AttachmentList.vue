<script setup lang="ts">
/**
 * @component AttachmentList
 * 附件清單：上傳（含進度）、預覽／下載、刪除。可否上傳、下載、刪除由 API 依所屬對象的權限回傳。
 * 下載連結由 API 簽發、幾分鐘內有效，因此每次點擊才向 API 取一次。
 */
import { ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import dayjs from 'dayjs';
import {
  ACCEPTED_FILE_TYPES,
  deleteAttachmentApi,
  formatFileSize,
  getDownloadUrlApi,
  listAttachmentsApi,
  MAX_FILE_BYTES,
  uploadAttachment,
  type AttachmentOwner,
  type AttachmentRow,
} from '@/shared/services/document-service';

const props = defineProps<{ ownerType: AttachmentOwner; ownerId: string; category?: string }>();
const emit = defineEmits<{ changed: [] }>();

const items = ref<AttachmentRow[]>([]);
const can = ref({ canUpload: false, canDownload: false, canDelete: false });
const loading = ref(false);
const uploading = ref<{ name: string; percent: number } | null>(null);

async function load() {
  loading.value = true;
  try {
    const result = await listAttachmentsApi(props.ownerType, props.ownerId);
    items.value = result.items;
    can.value = result;
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    loading.value = false;
  }
}

watch(() => props.ownerId, load, { immediate: true });

async function beforeUpload(file: File) {
  if (file.size > MAX_FILE_BYTES) {
    message.error('檔案不可超過 20 MB');
    return false;
  }
  uploading.value = { name: file.name, percent: 0 };
  try {
    await uploadAttachment(props.ownerType, props.ownerId, file, props.category, (percent) => (uploading.value = { name: file.name, percent }));
    message.success(`已上傳 ${file.name}`);
    await load();
    emit('changed');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    uploading.value = null;
  }
  return false;
}

const previewable = (row: AttachmentRow) => row.contentType === 'application/pdf' || /^image\/(png|jpeg|webp)$/.test(row.contentType);

/** 先在點擊當下開新分頁（避免被瀏覽器當成彈出視窗擋掉），取得簽章連結後再導過去。 */
async function open(row: AttachmentRow) {
  const tab = window.open('about:blank', '_blank');
  try {
    const { url } = await getDownloadUrlApi(row.id, previewable(row));
    if (tab) tab.location.href = url;
    else window.location.assign(url);
  } catch (error) {
    tab?.close();
    message.error((error as Error).message);
  }
}

async function remove(row: AttachmentRow) {
  try {
    await deleteAttachmentApi(row.id);
    await load();
    emit('changed');
  } catch (error) {
    message.error((error as Error).message);
  }
}
</script>

<template>
  <div class="attachments">
    <a-list size="small" :loading="loading" :data-source="items" :locale="{ emptyText: '沒有附件' }">
      <template #renderItem="{ item }">
        <a-list-item>
          <a-list-item-meta>
            <template #title>
              <a v-if="can.canDownload" @click="open(item)">{{ item.fileName }}</a>
              <span v-else>{{ item.fileName }}</span>
            </template>
            <template #description>
              {{ formatFileSize(item.size) }} · {{ item.createdBy }} · {{ dayjs(item.createdAt).format('YYYY-MM-DD HH:mm') }}
              <template v-if="item.category"> · {{ item.category }}</template>
            </template>
          </a-list-item-meta>
          <template #actions>
            <a-button v-if="can.canDownload" type="link" size="small" @click="open(item)">{{ previewable(item) ? '預覽' : '下載' }}</a-button>
            <a-popconfirm v-if="can.canDelete" title="刪除這個附件？" @confirm="remove(item)">
              <a-button type="link" size="small" danger>刪除</a-button>
            </a-popconfirm>
          </template>
        </a-list-item>
      </template>
    </a-list>
    <div v-if="can.canUpload" class="upload">
      <a-upload :before-upload="beforeUpload" :show-upload-list="false" :accept="ACCEPTED_FILE_TYPES" :disabled="!!uploading" multiple>
        <a-button :loading="!!uploading">上傳附件</a-button>
      </a-upload>
      <span class="hint">PDF、圖片、Excel、Word，單檔 20 MB 以內</span>
      <a-progress v-if="uploading" :percent="uploading.percent" size="small" :status="uploading.percent < 100 ? 'active' : 'normal'" class="progress" />
    </div>
  </div>
</template>

<style scoped>
.upload {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 8px;
}
.hint {
  color: #999;
  font-size: 12px;
}
.progress {
  width: 200px;
  margin: 0;
}
</style>
