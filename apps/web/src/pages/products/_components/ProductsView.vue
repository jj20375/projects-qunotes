<script setup lang="ts">
/**
 * @component ProductsView
 * 品項價目表：報價明細輸入品名時可搜尋帶入。預設成本只有具 quotes:view_cost 的人看得到、改得到。
 */
import { computed, reactive, ref } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/vue-query';
import DataTable, { type TableChange } from '@/app/components/DataTable.vue';
import { useMe } from '@/app/composables/use-me';
import { deleteProductApi, saveProductApi, searchProductsApi, type ProductRow } from '@/shared/services/quote-service';
import { dataColumns } from '@/shared/table/data-table';
import { formatMoney } from '@/shared/types/business';

const { can, canOption } = useMe();
const showCost = computed(() => canOption('quotes', 'view_cost'));
const queryClient = useQueryClient();
const keyword = ref('');
const applied = reactive({ keyword: '', page: 1, pageSize: 20 });
const products = useQuery({ queryKey: ['products', applied], queryFn: () => searchProductsApi({ ...applied }), placeholderData: keepPreviousData });

const formOpen = ref(false);
const form = reactive<Partial<ProductRow>>({});
const saving = ref(false);

function open(row: ProductRow | null) {
  Object.keys(form).forEach((key) => delete (form as Record<string, unknown>)[key]);
  Object.assign(form, row ?? { isActive: true, defaultPrice: 0 });
  formOpen.value = true;
}

async function save() {
  saving.value = true;
  try {
    await saveProductApi({ ...form });
    message.success('已儲存');
    formOpen.value = false;
    queryClient.invalidateQueries({ queryKey: ['products'] });
  } catch (error) {
    message.error((error as Error).message);
  } finally {
    saving.value = false;
  }
}

function remove(row: ProductRow) {
  Modal.confirm({
    title: `刪除品項「${row.name}」？`,
    content: '已被報價引用的品項會改為停用，不會真的刪除。',
    okType: 'danger',
    onOk: async () => {
      const result = await deleteProductApi(row.id);
      message.success(result.deactivated ? '品項已被報價使用，已改為停用' : '已刪除');
      queryClient.invalidateQueries({ queryKey: ['products'] });
    },
  });
}

const col = dataColumns<ProductRow>();
const columns = computed(() => [
  col.accessor('code', { header: '品號', meta: { width: 120 } }),
  col.accessor('name', { header: '品名' }),
  col.accessor('spec', { header: '規格' }),
  col.accessor('unit', { header: '單位', meta: { width: 70 } }),
  col.accessor('category', { header: '分類', meta: { width: 120 } }),
  col.accessor('defaultPrice', { header: '單價', meta: { width: 110, align: 'right' } }),
  // 只有具 quotes:view_cost 的人才有成本欄（後端也不會回傳成本給沒有權限的人）。
  ...(showCost.value ? [col.accessor('defaultCost', { header: '成本', meta: { width: 110, align: 'right' as const } })] : []),
  col.accessor('isActive', { header: '狀態', meta: { width: 80 } }),
  col.display({ id: 'ops', header: '', meta: { width: 120 } }),
]);

function onTableChange(change: TableChange) {
  Object.assign(applied, { page: change.page, pageSize: change.pageSize });
}
</script>

<template>
  <a-card :bordered="false">
    <div class="toolbar">
      <a-input-search v-model:value="keyword" placeholder="品號、品名、規格、分類" allow-clear style="max-width: 280px" @search="() => Object.assign(applied, { keyword, page: 1 })" />
      <a-button v-if="can('products', 'create')" type="primary" @click="open(null)">新增品項</a-button>
    </div>
    <DataTable
      :data="products.data.value?.items ?? []"
      :columns="columns"
      row-key="id"
      :loading="products.isFetching.value"
      :pagination="{ page: applied.page, pageSize: applied.pageSize, total: products.data.value?.total ?? 0, showTotal: (t: number) => `共 ${t} 筆` }"
      :scroll-x="900"
      @change="onTableChange"
    >
      <template #cell="{ columnId, record }">
        <template v-if="columnId === 'defaultPrice' || columnId === 'defaultCost'">{{ formatMoney(record[columnId as 'defaultPrice']) }}</template>
        <template v-else-if="columnId === 'isActive'"><a-tag :color="record.isActive ? 'green' : 'default'">{{ record.isActive ? '使用中' : '停用' }}</a-tag></template>
        <template v-else-if="columnId === 'ops'">
          <a-button v-if="can('products', 'update')" type="link" size="small" @click="open(record)">編輯</a-button>
          <a-button v-if="can('products', 'delete')" type="link" size="small" danger @click="remove(record)">刪除</a-button>
        </template>
      </template>
    </DataTable>
  </a-card>

  <a-modal v-model:open="formOpen" :title="form.id ? '編輯品項' : '新增品項'" :confirm-loading="saving" ok-text="儲存" @ok="save">
    <a-form layout="vertical">
      <a-row :gutter="12">
        <a-col :span="10"><a-form-item label="品號" required><a-input v-model:value="form.code" /></a-form-item></a-col>
        <a-col :span="14"><a-form-item label="品名" required><a-input v-model:value="form.name" /></a-form-item></a-col>
        <a-col :span="24"><a-form-item label="規格"><a-input v-model:value="form.spec" /></a-form-item></a-col>
        <a-col :span="8"><a-form-item label="單位"><a-input v-model:value="form.unit" /></a-form-item></a-col>
        <a-col :span="16"><a-form-item label="分類"><a-input v-model:value="form.category" /></a-form-item></a-col>
        <a-col :span="12"><a-form-item label="預設單價"><a-input-number v-model:value="form.defaultPrice" :min="0" style="width: 100%" /></a-form-item></a-col>
        <a-col v-if="showCost" :span="12"><a-form-item label="預設成本"><a-input-number v-model:value="form.defaultCost" :min="0" style="width: 100%" /></a-form-item></a-col>
        <a-col :span="24"><a-form-item label="使用中"><a-switch v-model:checked="form.isActive" /></a-form-item></a-col>
      </a-row>
    </a-form>
  </a-modal>
</template>

<style scoped>
.toolbar {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}
</style>
