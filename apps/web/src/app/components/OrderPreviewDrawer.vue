<script setup lang="ts">
/**
 * @component OrderPreviewDrawer
 * 訂單唯讀預覽（側邊抽屜）：訂單資料、所屬專案、來源報價，以及底下出貨單／發票／保固單的筆數與已開發票金額。
 * 開立發票、出貨單、保固單選對應訂單時，不離開表單就能確認訂單內容；來源報價可再開報價預覽（QuotePreviewDrawer）。
 */
import { computed, ref } from 'vue';
import { useQuery } from '@tanstack/vue-query';
import QuotePreviewDrawer from '@/app/components/QuotePreviewDrawer.vue';
import { getOrderApi } from '@/shared/services/document-service';
import { formatMoney } from '@/shared/types/business';
import { withBasePath } from '@/shared/utils/base-path';

const props = defineProps<{ open: boolean; orderId?: string | null; currentProjectId?: string }>();
const emit = defineEmits<{ close: [] }>();

const order = useQuery({
  queryKey: computed(() => ['order', props.orderId]),
  queryFn: () => getOrderApi(props.orderId!),
  enabled: computed(() => props.open && !!props.orderId),
});
const o = computed(() => order.data.value);
const previewQuoteId = ref<string | null>(null);

const statusLabels: Record<string, [string, string]> = {
  received: ['已收單', 'blue'],
  in_progress: ['執行中', 'processing'],
  delivered: ['已交貨', 'green'],
  cancelled: ['已取消', 'default'],
};
const sameProject = computed(() => !!o.value && o.value.project.id === props.currentProjectId);
/** 尚未開立發票的含稅金額；超開時為負數，顯示成 0 並另外提示。 */
const uninvoiced = computed(() => (o.value ? o.value.total - o.value.invoicedAmount : 0));
</script>

<template>
  <a-drawer :open="open" :width="560" title="訂單內容" @close="emit('close')">
    <a-spin :spinning="order.isFetching.value">
      <template v-if="o">
        <div class="head">
          <span class="no">{{ o.orderNo }}</span>
          <a-tag :color="statusLabels[o.status]?.[1]">{{ statusLabels[o.status]?.[0] ?? o.status }}</a-tag>
        </div>
        <div class="muted">
          {{ o.project.projectNo }} {{ o.project.name }}
          <a-tag v-if="!sameProject && currentProjectId" color="orange">其他專案</a-tag>
        </div>

        <a-descriptions :column="2" size="small" bordered class="gap">
          <a-descriptions-item label="訂單日期" :span="2">{{ o.orderDate }}</a-descriptions-item>
          <a-descriptions-item label="未稅金額">{{ formatMoney(o.amount) }}</a-descriptions-item>
          <a-descriptions-item label="稅額">{{ formatMoney(o.taxAmount) }}</a-descriptions-item>
          <a-descriptions-item label="含稅合計" :span="2"><strong>{{ formatMoney(o.total) }}</strong></a-descriptions-item>
          <a-descriptions-item label="來源報價" :span="2">
            <template v-if="o.quoteId">
              {{ o.quoteNo }}
              <a-button type="link" size="small" @click="previewQuoteId = o.quoteId">查看報價</a-button>
            </template>
            <template v-else>—</template>
          </a-descriptions-item>
          <a-descriptions-item v-if="o.note" label="備註" :span="2">{{ o.note }}</a-descriptions-item>
        </a-descriptions>

        <h4 class="section">訂單底下</h4>
        <a-descriptions :column="2" size="small" bordered>
          <a-descriptions-item label="發票">{{ o.invoiceCount }} 張（{{ formatMoney(o.invoicedAmount) }}）</a-descriptions-item>
          <a-descriptions-item label="尚未開立">
            {{ formatMoney(Math.max(uninvoiced, 0)) }}
            <a-tag v-if="uninvoiced < 0" color="red">已超開 {{ formatMoney(-uninvoiced) }}</a-tag>
          </a-descriptions-item>
          <a-descriptions-item label="出貨單">{{ o.shipmentCount }} 張</a-descriptions-item>
          <a-descriptions-item label="保固單">{{ o.warrantyCount }} 張</a-descriptions-item>
        </a-descriptions>
        <a-button v-if="!sameProject" type="link" :href="withBasePath(`/projects/detail?id=${o.project.id}`)" target="_blank" class="link">開啟這個專案</a-button>
      </template>
      <a-empty v-else-if="!order.isFetching.value" description="找不到訂單" />
    </a-spin>
    <QuotePreviewDrawer :open="!!previewQuoteId" :quote-id="previewQuoteId" @close="previewQuoteId = null" />
  </a-drawer>
</template>

<style scoped>
.head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.no {
  font-size: 18px;
  font-weight: 600;
}
.muted {
  color: #8c8c8c;
}
.gap {
  margin-top: 16px;
}
.section {
  margin: 20px 0 8px;
  font-weight: 600;
}
.link {
  padding-left: 0;
}
</style>
