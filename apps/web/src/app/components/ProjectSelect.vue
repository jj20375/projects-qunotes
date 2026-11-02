<script setup lang="ts">
/**
 * @component ProjectSelect
 * 專案下拉選單：輸入關鍵字即時查詢（只會列出目前資料範圍看得到的專案），排除已結案與已取消。
 */
import { ref } from 'vue';
import { searchProjectsApi } from '@/shared/services/business-service';

const props = defineProps<{ value?: string | null; placeholder?: string; initialLabel?: string }>();
const emit = defineEmits<{ 'update:value': [value: string | undefined] }>();

const openStatuses = ['draft', 'quoting', 'won', 'lost', 'in_progress', 'completed'];
const options = ref<{ value: string; label: string }[]>([]);
const loading = ref(false);
let timer: ReturnType<typeof setTimeout> | undefined;

async function load(keyword = '') {
  loading.value = true;
  try {
    const result = await searchProjectsApi({ keyword, status: openStatuses, pageSize: 30 });
    options.value = result.items.map((project) => ({ value: project.id, label: `${project.projectNo} ${project.name}（${project.client.name}）` }));
    if (props.value && props.initialLabel && !options.value.some((option) => option.value === props.value)) options.value.unshift({ value: props.value, label: props.initialLabel });
  } finally {
    loading.value = false;
  }
}

function onSearch(keyword: string) {
  clearTimeout(timer);
  timer = setTimeout(() => load(keyword), 250);
}

load();
</script>

<template>
  <a-select
    :value="props.value ?? undefined"
    show-search
    :filter-option="false"
    :options="options"
    :loading="loading"
    :placeholder="props.placeholder ?? '輸入專案名稱或編號'"
    style="width: 100%"
    @search="onSearch"
    @change="(value: unknown) => emit('update:value', value as string | undefined)"
  />
</template>
