import { fileURLToPath } from 'node:url';
import { defineConfig } from 'astro/config';
import vue from '@astrojs/vue';

// 後台為 static build；開發時以 Vite proxy 把 /api 轉給本機 API，
// 讓 refresh token cookie（Path=/api/auth、SameSite=Strict）與正式環境一樣維持同源。
const apiTarget = process.env.QUOTE_API_URL ?? 'http://127.0.0.1:5280';
const fileTarget = process.env.QUOTE_FILE_URL ?? 'http://127.0.0.1:5190';
// 預設只聽本機；要讓區網的其他裝置連線時用 `npm run dev:web:lan`（QUOTE_WEB_HOST=0.0.0.0）。API 與 file-server 仍只聽 127.0.0.1，由這個 dev server 代為轉送。
const webHost = process.env.QUOTE_WEB_HOST ?? '127.0.0.1';

export default defineConfig({
  output: 'static',
  integrations: [vue({ appEntrypoint: '/src/app/vue-app.ts' })],
  server: { host: webHost, port: 4341 },
  vite: {
    resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
    server: {
      proxy: {
        '/api': { target: apiTarget, changeOrigin: false, ws: true },
        '/files': { target: fileTarget, changeOrigin: false },
      },
    },
  },
});
