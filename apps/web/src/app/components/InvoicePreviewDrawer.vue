<script setup lang="ts">
/**
 * @component InvoicePreviewDrawer
 * 發票唯讀預覽（側邊抽屜）：發票資料、對應訂單與所屬專案。開立發票時號碼重複，用它確認是不是同一張。
 * 訂單的來源報價可以再開報價預覽（QuotePreviewDrawer）。
 */
import { computed, ref } from 'vue';
import QuotePreviewDrawer from '@/app/components/QuotePreviewDrawer.vue';
import type { InvoiceLookup } from '@/shared/services/document-service';
import { formatMoney } from '@/shared/types/business';
import { withBasePath } from '@/shared/utils/base-path';

const props = defineProps<{ open: boolean; lookup: InvoiceLookup | null; currentProjectId?: string }>();
const emit = defineEmits<{ close: [] }>();

const invoice = computed(() => props.lookup?.invoice);
const order = computed(() => props.lookup?.order);
const project = computed(() => props.lookup?.project);
const previewQuoteId = ref<string | null>(null);

const typeLabels: Record<string, string> = { duplicate: '二聯式', triplicate: '三聯式', e_invoice: '電子發票' };
const paymentLabels: Record<string, string> = { unpaid: '未收款', partial: '部分收款', paid: '已收款' };
const orderStatusLabels: Record<string, string> = { received: '已收單', in_progress: '執行中', delivered: '已交貨', cancelled: '已取消' };
const sameProject = computed(() => !!project.value && project.value.id === props.currentProjectId);
</script>

<template>
  <a-drawer :open="open" :width="560" title="發票內容" @close="emit('close')">
    <template v-if="invoice">
      <div class="head">
        <span class="no">{{ invoice.invoiceNo }}</span>
        <a-tag v-if="invoice.status === 'void'" color="default">已作廢</a-tag>
        <a-tag v-else color="blue">開立中</a-tag>
      </div>
      <a-alert v-if="invoice.status === 'void'" type="warning" show-icon class="gap" :message="`作廢原因：${invoice.voidReason ?? '—'}`" description="已作廢的發票號碼仍不能重複登錄。" />

      <a-descriptions :column="2" size="small" bordered class="gap">
        <a-descriptions-item label="發票日期">{{ invoice.invoiceDate }}</a-descriptions-item>
        <a-descriptions-item label="類型">{{ typeLabels[invoice.invoiceType] }}</a-descriptions-item>
        <a-descriptions-item label="買受人" :span="2">{{ invoice.buyerName || '—' }}<span v-if="invoice.buyerTaxId">（{{ invoice.buyerTaxId }}）</span></a-descriptions-item>
        <a-descriptions-item label="銷售額">{{ formatMoney(invoice.salesAmount) }}</a-descriptions-item>
        <a-descriptions-item label="稅額">{{ formatMoney(invoice.taxAmount) }}</a-descriptions-item>
        <a-descriptions-item label="總計">{{ formatMoney(invoice.totalAmount) }}</a-descriptions-item>
        <a-descriptions-item label="收款">{{ paymentLabels[invoice.paymentStatus] }}<span v-if="invoice.paidAmount">（{{ formatMoney(invoice.paidAmount) }}）</span></a-descriptions-item>
        <a-descriptions-item label="收款到期日">{{ invoice.dueDate ?? '—' }}</a-descriptions-item>
        <a-descriptions-item v-if="invoice.note" label="備註" :span="2">{{ invoice.note }}</a-descriptions-item>
      </a-descriptions>

      <h4 class="section">所屬專案</h4>
      <a-descriptions :column="1" size="small" bordered>
        <a-descriptions-item label="專案">
          {{ project?.projectNo }} {{ project?.name }}
          <a-tag v-if="sameProject" color="green">目前這個專案</a-tag>
          <a-tag v-else color="orange">其他專案</a-tag>
        </a-descriptions-item>
      </a-descriptions>
      <a-button v-if="project && !sameProject" type="link" :href="withBasePath(`/projects/detail?id=${project.id}`)" target="_blank" class="link">開啟這個專案</a-button>

      <h4 class="section">對應訂單</h4>
      <a-descriptions v-if="order" :column="2" size="small" bordered>
        <a-descriptions-item label="訂單號碼" :span="2">{{ order.orderNo }}</a-descriptions-item>
        <a-descriptions-item label="訂單日期">{{ order.orderDate }}</a-descriptions-item>
        <a-descriptions-item label="狀態">{{ orderStatusLabels[order.status] }}</a-descriptions-item>
        <a-descriptions-item label="未稅">{{ formatMoney(order.amount) }}</a-descriptions-item>
        <a-descriptions-item label="含稅合計">{{ formatMoney(order.total) }}</a-descriptions-item>
        <a-descriptions-item label="來源報價" :span="2">
          <template v-if="order.quoteId">
            {{ order.quoteNo }}
            <a-button type="link" size="small" @click="previewQuoteId = order.quoteId">查看報價</a-button>
          </template>
          <template v-else>—</template>
        </a-descriptions-item>
      </a-descriptions>
      <a-empty v-else description="沒有對應的訂單（或您沒有檢視訂單的權限）" :image-style="{ height: '40px' }" />
    </template>
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
