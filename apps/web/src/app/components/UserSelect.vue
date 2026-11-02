<script setup lang="ts">
/**
 * @component UserSelect
 * 人員下拉選單（負責人、專案成員、篩選條件）：輸入關鍵字由後端查詢姓名與部門，不在前端過濾整份名單。
 * 已選取的值會以 ids 向後端取回姓名，所以編輯既有資料時不必另外傳顯示文字。
 * `excludeIds` 由後端排除（例如專案負責人不必再列為成員）。
 */
import { ref, watch } from 'vue';
import { searchDirectoryUsersApi, type DirectoryUser } from '@/shared/services/business-service';

const props = defineProps<{ value?: string | null; excludeIds?: string[]; placeholder?: string; allowClear?: boolean }>();
const emit = defineEmits<{ 'update:value': [value: string | undefined]; change: [value: string | undefined] }>();

const options = ref<{ value: string; label: string }[]>([]);
const loading = ref(false);
let timer: ReturnType<typeof setTimeout> | undefined;
let requestNo = 0;

const toOption = (user: DirectoryUser) => ({ value: user.userId, label: user.departmentName ? `${user.displayName}（${user.departmentName}）` : user.displayName });

async function load(keyword = '') {
  const current = ++requestNo;
  loading.value = true;
  try {
    const exclude = props.excludeIds?.filter(Boolean);
    const [found, selected] = await Promise.all([
      searchDirectoryUsersApi({ keyword, excludeIds: exclude, limit: 20 }),
      // 目前選取的人不一定在搜尋結果裡，另外取回才能顯示姓名。
      props.value && !keyword ? searchDirectoryUsersApi({ ids: [props.value] }) : Promise.resolve([] as DirectoryUser[]),
    ]);
    if (current !== requestNo) return;
    const merged = [...selected, ...found.filter((user) => !selected.some((value) => value.userId === user.userId))];
    options.value = merged.map(toOption);
  } finally {
    if (current === requestNo) loading.value = false;
  }
}

function onSearch(keyword: string) {
  clearTimeout(timer);
  timer = setTimeout(() => load(keyword), 250);
}

function onChange(value: unknown) {
  emit('update:value', value as string | undefined);
  emit('change', value as string | undefined);
}

watch(
  () => [props.value, props.excludeIds?.join(',')],
  (next, previous) => {
    // 只有在已選值不在目前選項中、或排除名單改變時才重新查詢，避免每次選取都打 API。
    const valueMissing = props.value && !options.value.some((option) => option.value === props.value);
    if (!previous || valueMissing || next[1] !== previous[1]) load();
  },
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
    :placeholder="props.placeholder ?? '輸入姓名或部門搜尋'"
    :allow-clear="props.allowClear"
    style="width: 100%"
    @search="onSearch"
    @change="onChange"
  />
</template>
