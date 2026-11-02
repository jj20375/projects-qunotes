<script setup lang="ts">
/**
 * @component ClientSelect
 * 客戶下拉選單：輸入關鍵字即時查詢（名稱、代碼、統編），專案表單與篩選共用。
 * `initialLabel` 讓編輯時不必先查詢就能顯示目前客戶名稱。
 */
import { ref, watch } from 'vue';
import { searchClientsApi } from '@/shared/services/business-service';

const props = defineProps<{ value?: string | null; initialLabel?: string; activeOnly?: boolean; placeholder?: string; allowClear?: boolean }>();
const emit = defineEmits<{ 'update:value': [value: string | undefined] }>();

const options = ref<{ value: string; label: string }[]>([]);
const loading = ref(false);
let timer: ReturnType<typeof setTimeout> | undefined;

async function load(keyword = '') {
  loading.value = true;
  try {
    const result = await searchClientsApi({ keyword, status: props.activeOnly ? 'active' : undefined, pageSize: 30 });
    options.value = result.items.map((client) => ({ value: client.id, label: `${client.code} ${client.name}` }));
    if (props.value && props.initialLabel && !options.value.some((option) => option.value === props.value)) {
      options.value.unshift({ value: props.value, label: props.initialLabel });
    }
  } finally {
    loading.value = false;
  }
}

function onSearch(keyword: string) {
  clearTimeout(timer);
  timer = setTimeout(() => load(keyword), 250);
}

watch(
  () => props.initialLabel,
  () => load(),
  { immediate: true },
);
</script>

<template>
  <a-select
    :value="props.value ?? undefined"
    show-search
    :filter-option="false"
    :options="options"
    :loading="loading"
    :placeholder="props.placeholder ?? '輸入客戶名稱、代碼或統編'"
    :allow-clear="props.allowClear"
    style="width: 100%"
    @search="onSearch"
    @change="(value: unknown) => emit('update:value', value as string | undefined)"
  />
</template>
