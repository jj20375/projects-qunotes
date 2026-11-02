import type { App } from 'vue';
import Antd from 'ant-design-vue';
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { ApiError } from '@/shared/api/http-client';
import 'ant-design-vue/dist/reset.css';

/** 4xx 是授權或輸入問題，重送不會改善；只有網路錯誤與 5xx 重試一次。 */
function shouldRetry(failureCount: number, error: unknown): boolean {
  return failureCount < 1 && (!(error instanceof ApiError) || error.status >= 500);
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 30_000, retry: shouldRetry, refetchOnWindowFocus: false },
    mutations: { retry: false },
  },
});

/** 每個 Vue island 都掛上同一組 plugin（Astro appEntrypoint）。 */
export default function setup(app: App): void {
  app.use(Antd);
  app.use(VueQueryPlugin, { queryClient });
}
