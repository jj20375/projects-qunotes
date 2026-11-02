<script setup lang="ts">
/**
 * @component TermsEditor
 * 條款編輯器（報價的備註條款、公司預設條款）：一條一列、編號自動產生，可新增、刪除、上下移動。
 *
 * - v-model 是「一條一行、無編號」的文字，與後端儲存格式相同（見 QuoteTerms）。
 * - 在任一列貼上含換行的整段文字（例如從 Excel 或 Word 複製的 1. 2. 3. 條列），會呼叫後端用同一套規則拆成多條：
 *   移除原本的編號、把手動斷行的續行併回上一條。
 * - 「整段貼上」可以一次取代全部或加在後面；直接把整段貼進來就儲存，後端也會轉成條列。
 * - 外層以 a-form-item-rest 包住：放在 a-form-item 內時，多個輸入框不讓 Form.Item 收集（值由 v-model 管理）。
 */
import { ref, watch } from 'vue';
import { message } from 'ant-design-vue';
import { normalizeTermsApi } from '@/shared/services/quote-service';

const props = defineProps<{ modelValue: string | null | undefined; readonly?: boolean; emptyText?: string }>();
const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>();

const split = (value: string | null | undefined) => (value ?? '').split('\n').map((line) => line.trim()).filter(Boolean);
const items = ref<string[]>(split(props.modelValue));
const pasteOpen = ref(false);
const pasteText = ref('');
const busy = ref(false);

/** 送出時去掉空白列；空白列只留在畫面上讓使用者輸入。 */
const serialize = (list: string[]) => list.map((item) => item.replace(/\s*\n\s*/g, ' ').trim()).filter(Boolean).join('\n') || null;

watch(
  () => props.modelValue,
  (value) => {
    if ((value ?? null) !== serialize(items.value)) items.value = split(value);
  },
);

function commit() {
  emit('update:modelValue', serialize(items.value));
}

function add() {
  items.value.push('');
}

function remove(index: number) {
  items.value.splice(index, 1);
  commit();
}

function move(index: number, offset: -1 | 1) {
  const target = index + offset;
  if (target < 0 || target >= items.value.length) return;
  const [item] = items.value.splice(index, 1);
  items.value.splice(target, 0, item);
  commit();
}

async function toItems(text: string): Promise<string[]> {
  busy.value = true;
  try {
    return (await normalizeTermsApi(text)).items;
  } finally {
    busy.value = false;
  }
}

/** 在某一列貼上多行文字：拆成多條，原本這列是空的就取代、否則接在後面。 */
async function onPaste(event: ClipboardEvent, index: number) {
  const text = event.clipboardData?.getData('text') ?? '';
  if (!/\r?\n/.test(text.trim())) return;
  event.preventDefault();
  try {
    const parsed = await toItems(text);
    if (!parsed.length) return;
    items.value.splice(items.value[index]?.trim() ? index + 1 : index, items.value[index]?.trim() ? 0 : 1, ...parsed);
    commit();
    message.success(`已拆成 ${parsed.length} 條`);
  } catch (error) {
    message.error((error as Error).message);
  }
}

async function applyPaste(mode: 'replace' | 'append') {
  try {
    const parsed = await toItems(pasteText.value);
    items.value = mode === 'replace' ? parsed : [...items.value.filter((item) => item.trim()), ...parsed];
    commit();
    pasteOpen.value = false;
    pasteText.value = '';
    message.success(`已整理成 ${parsed.length} 條`);
  } catch (error) {
    message.error((error as Error).message);
  }
}
</script>

<template>
  <a-form-item-rest>
    <div class="terms">
      <template v-if="readonly">
        <ol v-if="items.length" class="terms-list">
          <li v-for="(item, index) in items" :key="index">{{ item }}</li>
        </ol>
        <span v-else class="muted">{{ emptyText ?? '—' }}</span>
      </template>
      <template v-else>
        <div v-for="(item, index) in items" :key="index" class="term-row">
          <span class="term-no">{{ index + 1 }}.</span>
          <a-textarea
            v-model:value="items[index]"
            :auto-size="{ minRows: 1, maxRows: 6 }"
            :aria-label="`第 ${index + 1} 條`"
            placeholder="條款內容"
            @change="commit"
            @paste="(event: ClipboardEvent) => onPaste(event, index)"
          />
          <div class="term-ops">
            <a-button type="text" size="small" :disabled="index === 0" aria-label="上移" @click="move(index, -1)">↑</a-button>
            <a-button type="text" size="small" :disabled="index === items.length - 1" aria-label="下移" @click="move(index, 1)">↓</a-button>
            <a-button type="text" size="small" danger aria-label="刪除這一條" @click="remove(index)">✕</a-button>
          </div>
        </div>
        <a-space wrap class="term-actions">
          <a-button size="small" @click="add">新增一條</a-button>
          <a-button size="small" :loading="busy" @click="pasteOpen = true">整段貼上</a-button>
          <span class="muted">可直接貼上整段條列（1. 2.、一、二、、•），會自動拆成一條一條，編號自動產生。</span>
        </a-space>
      </template>

      <a-modal v-model:open="pasteOpen" title="整段貼上條款" :footer="null" :width="640">
        <p class="muted">貼上從 Excel、Word 複製的條列文字。原本的編號會移除，為了換行而拆開的續行會併回上一條。</p>
        <a-textarea v-model:value="pasteText" :rows="10" placeholder="1. 建置官網屬於 RWD 響應式設計&#10;2. 付款條件：訂單成立後…" />
        <div class="paste-actions">
          <a-button :disabled="!pasteText.trim()" :loading="busy" @click="applyPaste('append')">加在後面</a-button>
          <a-button type="primary" :disabled="!pasteText.trim()" :loading="busy" @click="applyPaste('replace')">取代全部</a-button>
        </div>
      </a-modal>
    </div>
  </a-form-item-rest>
</template>

<style scoped>
.term-row {
  display: grid;
  grid-template-columns: 28px minmax(0, 1fr) auto;
  gap: 8px;
  align-items: start;
  margin-bottom: 6px;
}
.term-no {
  padding-top: 5px;
  color: #595959;
  text-align: right;
  font-variant-numeric: tabular-nums;
}
.term-ops {
  display: flex;
  gap: 2px;
}
.term-actions {
  margin-top: 4px;
}
.terms-list {
  margin: 0;
  padding-left: 1.6em;
}
.terms-list li {
  margin-bottom: 4px;
}
.muted {
  color: #8c8c8c;
  font-size: 12px;
}
.paste-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
}
</style>
