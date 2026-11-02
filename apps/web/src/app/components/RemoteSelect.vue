<script setup lang="ts">
/**
 * @component RemoteSelect
 * 遠端搜尋下拉：選項與關鍵字搜尋都由後端 API 處理（`fetch(keyword)` 只回一頁），前端不做過濾。
 * 訂單、報價這類「某個專案底下的資料」用它；專案、客戶、人員各有自己的包裝元件（ProjectSelect…）。
 *
 * - `fetch`：依關鍵字向後端查詢，回傳 `{ value, label, raw }`；`raw` 是完整資料，選取時由 `pick` 事件帶出（例如依訂單帶入金額）。
 * - `initialLabel`：編輯既有資料時，目前的選項不一定在第一頁，用它顯示已選項目的名稱。
 */
import { ref, watch } from 'vue';

export interface RemoteOption<T = unknown> {
  value: string;
  label: string;
  raw: T;
}

const props = defineProps<{ value?: string | null; fetch: (keyword: string) => Promise<RemoteOption[]>; placeholder?: string; initialLabel?: string }>();
const emit = defineEmits<{ 'update:value': [value: string | undefined]; pick: [raw: unknown | undefined] }>();

const options = ref<RemoteOption[]>([]);
const loading = ref(false);
let timer: ReturnType<typeof setTimeout> | undefined;
let latest = 0;

/** 已選項目不一定在這一頁的結果裡，補上一個只有名稱的選項，讓選單能顯示它。 */
function ensureSelected() {
  if (props.value && props.initialLabel && !options.value.some((option) => option.value === props.value)) {
    options.value = [{ value: props.value, label: props.initialLabel, raw: undefined }, ...options.value];
  }
}

async function load(keyword = '') {
  const ticket = ++latest;
  loading.value = true;
  try {
    const result = await props.fetch(keyword);
    if (ticket !== latest) return;
    options.value = result;
    ensureSelected();
  } catch {
    // 查詢失敗時保留目前的選項。
  } finally {
    if (ticket === latest) loading.value = false;
  }
}

function onSearch(keyword: string) {
  clearTimeout(timer);
  timer = setTimeout(() => load(keyword), 250);
}

function onChange(value: unknown) {
  emit('update:value', (value as string | undefined) ?? undefined);
  emit('pick', options.value.find((option) => option.value === value)?.raw);
}

watch(() => props.fetch, () => load());
watch(() => [props.value, props.initialLabel], ensureSelected);
load();
</script>

<template>
  <a-select
    :value="props.value ?? undefined"
    show-search
    allow-clear
    :filter-option="false"
    :options="options"
    :loading="loading"
    :placeholder="props.placeholder ?? '輸入關鍵字搜尋'"
    style="width: 100%"
    @search="onSearch"
    @change="onChange"
  />
</template>
