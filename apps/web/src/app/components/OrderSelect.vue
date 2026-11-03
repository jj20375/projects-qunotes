<script setup lang="ts">
/**
 * @component OrderSelect
 * 「對應訂單」選擇：遠端搜尋下拉（RemoteSelect）加「查看訂單」按鈕，選定後可在抽屜看訂單內容（OrderPreviewDrawer）。
 * 開立發票、出貨單、保固單共用；選項與搜尋由後端處理，fetch 由各頁決定（例如排除已取消的訂單）。
 */
import { ref } from 'vue';
import OrderPreviewDrawer from '@/app/components/OrderPreviewDrawer.vue';
import RemoteSelect, { type RemoteOption } from '@/app/components/RemoteSelect.vue';

defineProps<{ value?: string | null; fetch: (keyword: string) => Promise<RemoteOption[]>; initialLabel?: string; placeholder?: string; currentProjectId?: string }>();
const emit = defineEmits<{ 'update:value': [value: string | undefined]; pick: [raw: unknown | undefined] }>();

const previewOpen = ref(false);
</script>

<template>
  <div class="order-pick">
    <RemoteSelect
      :value="value"
      :fetch="fetch"
      :initial-label="initialLabel"
      :placeholder="placeholder ?? '輸入訂單號碼搜尋'"
      @update:value="emit('update:value', $event)"
      @pick="emit('pick', $event)"
    />
    <a-button :disabled="!value" @click="previewOpen = true">查看訂單</a-button>
  </div>
  <OrderPreviewDrawer :open="previewOpen" :order-id="value" :current-project-id="currentProjectId" @close="previewOpen = false" />
</template>

<style scoped>
.order-pick {
  display: flex;
  gap: 8px;
}
.order-pick > :first-child {
  flex: 1;
  min-width: 0;
}
</style>
