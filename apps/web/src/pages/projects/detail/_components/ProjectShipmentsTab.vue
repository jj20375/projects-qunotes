<script setup lang="ts">
/**
 * @component ProjectShipmentsTab
 * 專案的出貨單（可多筆）；每張出貨單掛在一張訂單下（同一張訂單可分批出貨），展開列可管理簽收單等附件。
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
import { deleteShipmentApi, listOrdersApi, listShipmentsApi, saveShipmentApi, type Order, type Shipment } from '@/shared/services/document-service';

const props = defineProps<{ project: ProjectDetail }>();
const emit = defineEmits<{ changed: [] }>();
const queryClient = useQueryClient();
// 搜尋與分頁由後端處理：換頁、換每頁筆數都會重新向 API 取資料。
const paging = reactive({ page: 1, pageSize: 20 });
const shipments = useQuery({
  queryKey: ['shipments', props.project.id, paging],
  queryFn: () => listShipmentsApi(props.project.id, { ...paging }),
  placeholderData: keepPreviousData,
});
// 對應訂單：排除已取消的訂單（activeOnly）與關鍵字搜尋都由後端處理。
const fetchOrders = async (keyword: string): Promise<RemoteOption<Order>[]> =>
  (await listOrdersApi(props.project.id, { activeOnly: true, keyword, pageSize: 20 })).items.map((order) => ({ value: order.id, label: order.orderNo, raw: order }));
function onTableChange(change: TableChange) {
  Object.assign(paging, { page: change.page, pageSize: change.pageSize });
}
const statusLabels: Record<Shipment['status'], [string, string]> = {
  shipped: ['已出貨', 'blue'],
  delivered: ['已簽收', 'green'],
  cancelled: ['已取消', 'default'],
};

const formOpen = ref(false);
const form = reactive<Partial<Shipment> & { date?: Dayjs }>({});
const saving = ref(false);

function open(row: Shipment | null) {
  Object.keys(form).forEach((key) => delete (form as Record<string, unknown>)[key]);
  Object.assign(form, row ? { ...row, date: dayjs(row.shipDate) } : { date: dayjs(), status: 'shipped' });
  formOpen.value = true;
}

async function save() {
  saving.value = true;
  try {
    await saveShipmentApi(props.project.id, { ...form, shipDate: form.date?.format('YYYY-MM-DD') });
    message.success('出貨單已儲存');
    formOpen.value = false;
    await queryClient.invalidateQueries({ queryKey: ['shipments', props.project.id] });
    emit('changed');
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

function remove(row: Shipment) {
  Modal.confirm({
    title: `刪除出貨單 ${row.shipmentNo}？`,
    okType: 'danger',
    onOk: async () => {
      try {
        await deleteShipmentApi(row.id);
        await queryClient.invalidateQueries({ queryKey: ['shipments', props.project.id] });
        emit('changed');
      } catch (error) {
        message.error((error as Error).message);
      }
    },
  });
}

const col = dataColumns<Shipment>();
const columns = [
  col.accessor('shipmentNo', { header: '出貨單號', meta: { width: 140 } }),
  col.accessor('shipDate', { header: '出貨日期', meta: { width: 110 } }),
  col.accessor('orderNo', { header: '對應訂單', meta: { width: 140 } }),
  col.accessor('receiverName', { header: '收貨人' }),
  col.accessor('trackingNo', { header: '物流單號', meta: { width: 140 } }),
  col.accessor('status', { header: '狀態', meta: { width: 90 } }),
  col.accessor('attachmentCount', { header: '附件', meta: { width: 70 } }),
  col.display({ id: 'ops', header: '', meta: { width: 120 } }),
];
</script>

<template>
  <div class="toolbar">
    <a-button v-if="shipments.data.value?.canCreate" type="primary" @click="open(null)">新增出貨單</a-button>
  </div>
  <DataTable
    :data="shipments.data.value?.items ?? []"
    :columns="columns"
    row-key="id"
    size="small"
    :loading="shipments.isFetching.value"
    :pagination="{ page: paging.page, pageSize: paging.pageSize, total: shipments.data.value?.total ?? 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 筆` }"
    :scroll-x="900"
    @change="onTableChange"
  >
    <template #cell="{ columnId, record }">
      <template v-if="columnId === 'status'">
        <a-tag :color="statusLabels[record.status as Shipment['status']][1]">{{ statusLabels[record.status as Shipment['status']][0] }}</a-tag>
      </template>
      <template v-else-if="columnId === 'ops'">
        <a-button v-if="shipments.data.value?.canUpdate" type="link" size="small" @click="open(record)">編輯</a-button>
        <a-button v-if="shipments.data.value?.canDelete" type="link" size="small" danger @click="remove(record)">刪除</a-button>
      </template>
    </template>
    <template #expanded="{ record }">
      <AttachmentList owner-type="shipment" :owner-id="record.id" category="簽收單" @changed="queryClient.invalidateQueries({ queryKey: ['shipments', project.id] })" />
    </template>
    <template #empty><a-empty description="還沒有出貨單" /></template>
  </DataTable>

  <a-modal v-model:open="formOpen" :title="form.id ? '編輯出貨單' : '新增出貨單'" :confirm-loading="saving" ok-text="儲存" @ok="save">
    <a-form layout="vertical">
      <a-row :gutter="12">
        <a-col :span="24">
          <a-form-item label="對應訂單" required>
            <OrderSelect v-model:value="form.orderId" :fetch="fetchOrders" :initial-label="form.orderNo ?? undefined" :current-project-id="project.id" />
          </a-form-item>
        </a-col>
        <a-col :span="14"><a-form-item label="出貨單號" required><a-input v-model:value="form.shipmentNo" /></a-form-item></a-col>
        <a-col :span="10"><a-form-item label="出貨日期" required><a-date-picker v-model:value="form.date" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="12"><a-form-item label="收貨人"><a-input v-model:value="form.receiverName" /></a-form-item></a-col>
        <a-col :span="12">
          <a-form-item label="狀態">
            <a-select v-model:value="form.status" :options="Object.entries(statusLabels).map(([value, [label]]) => ({ value, label }))" />
          </a-form-item>
        </a-col>
        <a-col :span="24"><a-form-item label="送貨地址"><a-input v-model:value="form.deliveryAddress" /></a-form-item></a-col>
        <a-col :span="24"><a-form-item label="物流單號"><a-input v-model:value="form.trackingNo" placeholder="貨運或快遞追蹤號碼" /></a-form-item></a-col>
        <a-col :span="24"><a-form-item label="備註"><a-textarea v-model:value="form.note" :rows="2" /></a-form-item></a-col>
      </a-row>
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
