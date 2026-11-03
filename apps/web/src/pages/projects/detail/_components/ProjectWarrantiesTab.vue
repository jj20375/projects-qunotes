<script setup lang="ts">
/**
 * @component ProjectWarrantiesTab
 * 專案的保固單（可多筆）；90 天內到期標示「即將到期」，展開列可管理保固書附件。
 */
import { reactive, ref } from 'vue';
import dayjs, { type Dayjs } from 'dayjs';
import { message, Modal } from 'ant-design-vue';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import OrderSelect from '@/app/components/OrderSelect.vue';
import type { RemoteOption } from '@/app/components/RemoteSelect.vue';
import { dataColumns } from '@/shared/table/data-table';
import AttachmentList from '@/app/components/AttachmentList.vue';
import type { ProjectDetail } from '@/shared/services/business-service';
import { deleteWarrantyApi, listOrdersApi, listWarrantiesApi, saveWarrantyApi, type Order, type Warranty } from '@/shared/services/document-service';
import { useMe } from '@/app/composables/use-me';

const props = defineProps<{ project: ProjectDetail }>();
const emit = defineEmits<{ changed: [] }>();
const { can } = useMe();
const queryClient = useQueryClient();
// 搜尋與分頁由後端處理：換頁、換每頁筆數都會重新向 API 取資料。
const paging = reactive({ page: 1, pageSize: 20 });
const warranties = useQuery({
  queryKey: ['warranties', props.project.id, paging],
  queryFn: () => listWarrantiesApi(props.project.id, { ...paging }),
  placeholderData: keepPreviousData,
});
// 對應訂單：關鍵字搜尋由後端處理。
const fetchOrders = async (keyword: string): Promise<RemoteOption<Order>[]> =>
  (await listOrdersApi(props.project.id, { keyword, pageSize: 20 })).items.map((order) => ({ value: order.id, label: order.orderNo, raw: order }));
function onTableChange(change: TableChange) {
  Object.assign(paging, { page: change.page, pageSize: change.pageSize });
}
const stateLabels = { active: ['保固中', 'green'], expiring: ['即將到期', 'orange'], expired: ['已到期', 'default'] } as const;

const formOpen = ref(false);
const form = reactive<Partial<Warranty> & { period?: Dayjs[] }>({});
const saving = ref(false);

function open(row: Warranty | null) {
  Object.keys(form).forEach((key) => delete (form as Record<string, unknown>)[key]);
  Object.assign(form, row ? { ...row, period: [dayjs(row.startDate), dayjs(row.endDate)] } : { period: [dayjs(), dayjs().add(1, 'year').subtract(1, 'day')] });
  formOpen.value = true;
}

async function save() {
  saving.value = true;
  try {
    const [start, end] = form.period ?? [];
    await saveWarrantyApi(props.project.id, { ...form, startDate: start?.format('YYYY-MM-DD'), endDate: end?.format('YYYY-MM-DD') });
    message.success('保固單已儲存');
    formOpen.value = false;
    await queryClient.invalidateQueries({ queryKey: ['warranties', props.project.id] });
    emit('changed');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

function remove(row: Warranty) {
  Modal.confirm({
    title: `刪除保固單 ${row.warrantyNo}？`,
    okType: 'danger',
    onOk: async () => {
      await deleteWarrantyApi(row.id);
      await queryClient.invalidateQueries({ queryKey: ['warranties', props.project.id] });
      emit('changed');
    },
  });
}

const col = dataColumns<Warranty>();
const columns = [
  col.accessor('warrantyNo', { header: '保固單號', meta: { width: 140 } }),
  col.accessor('subject', { header: '保固項目' }),
  col.accessor('startDate', { header: '保固期間', meta: { width: 210 } }),
  col.accessor('state', { header: '狀態', meta: { width: 100 } }),
  col.accessor('orderNo', { header: '對應訂單', meta: { width: 140 } }),
  col.accessor('attachmentCount', { header: '附件', meta: { width: 70 } }),
  col.display({ id: 'ops', header: '', meta: { width: 120 } }),
];
</script>

<template>
  <div class="toolbar">
    <a-button v-if="warranties.data.value?.canCreate" type="primary" @click="open(null)">新增保固單</a-button>
  </div>
  <DataTable
    :data="warranties.data.value?.items ?? []"
    :columns="columns"
    row-key="id"
    size="small"
    :loading="warranties.isFetching.value"
    :pagination="{ page: paging.page, pageSize: paging.pageSize, total: warranties.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 筆` }"
    :scroll-x="860"
    @change="onTableChange"
  >
    <template #cell="{ columnId, record }">
      <template v-if="columnId === 'startDate'">{{ record.startDate }} ~ {{ record.endDate }}</template>
      <template v-else-if="columnId === 'state'">
        <a-tag :color="stateLabels[record.state as Warranty['state']][1]">{{ stateLabels[record.state as Warranty['state']][0] }}</a-tag>
      </template>
      <template v-else-if="columnId === 'ops'">
        <a-button v-if="warranties.data.value?.canUpdate" type="link" size="small" @click="open(record)">編輯</a-button>
        <a-button v-if="warranties.data.value?.canDelete" type="link" size="small" danger @click="remove(record)">刪除</a-button>
      </template>
    </template>
    <template #expanded="{ record }">
      <AttachmentList owner-type="warranty" :owner-id="record.id" category="保固書" @changed="queryClient.invalidateQueries({ queryKey: ['warranties', project.id] })" />
    </template>
    <template #empty><a-empty description="還沒有保固單" /></template>
  </DataTable>

  <a-modal v-model:open="formOpen" :title="form.id ? '編輯保固單' : '新增保固單'" :confirm-loading="saving" ok-text="儲存" @ok="save">
    <a-form layout="vertical">
      <a-form-item label="保固單號" required><a-input v-model:value="form.warrantyNo" /></a-form-item>
      <a-form-item label="保固項目" required><a-input v-model:value="form.subject" placeholder="例如：伺服器硬體保固" /></a-form-item>
      <a-form-item label="保固期間" required><a-range-picker v-model:value="form.period" style="width: 100%" /></a-form-item>
      <a-form-item v-if="can('orders', 'read')" label="對應訂單" :required="!form.id || !!form.orderId"><OrderSelect v-model:value="form.orderId" :fetch="fetchOrders" :initial-label="form.orderNo ?? undefined" :current-project-id="project.id" /></a-form-item>
      <a-form-item label="保固條款"><a-textarea v-model:value="form.terms" :rows="3" /></a-form-item>
      <a-form-item label="備註"><a-textarea v-model:value="form.note" :rows="2" /></a-form-item>
    </a-form>
  </a-modal>
</template>

<style scoped>
.toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 12px;
}
</style>
