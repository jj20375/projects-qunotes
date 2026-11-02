<script setup lang="ts">
/**
 * @component EChart
 * ECharts 包裝：只註冊用到的圖表與元件（tree-shaking），容器大小改變時自動 resize。
 * 圖表樣式規則（細線、圓角柱頭、淡格線、hover 提示）集中在 src/shared/utils/chart-theme.ts。
 * 無障礙名稱放在外層容器：ECharts 內建的 aria 描述是簡體中文且會覆寫 aria-label，所以不啟用。
 */
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import * as echarts from 'echarts/core';
import { BarChart, LineChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import { SVGRenderer } from 'echarts/renderers';
import type { EChartsCoreOption } from 'echarts/core';

echarts.use([BarChart, LineChart, GridComponent, LegendComponent, TooltipComponent, SVGRenderer]);

const props = defineProps<{ option: EChartsCoreOption; height?: number; label: string }>();
const root = ref<HTMLDivElement>();
let chart: echarts.ECharts | undefined;
let observer: ResizeObserver | undefined;

onMounted(() => {
  chart = echarts.init(root.value!, undefined, { renderer: 'svg' });
  chart.setOption(props.option);
  observer = new ResizeObserver(() => chart?.resize());
  observer.observe(root.value!);
});

watch(
  () => props.option,
  (option) => chart?.setOption(option, { notMerge: true }),
  { deep: true },
);

onBeforeUnmount(() => {
  observer?.disconnect();
  chart?.dispose();
});
</script>

<template>
  <div class="chart" role="img" :aria-label="label">
    <div ref="root" :style="{ height: `${height ?? 280}px` }" />
  </div>
</template>

<style scoped>
.chart {
  width: 100%;
}
</style>
