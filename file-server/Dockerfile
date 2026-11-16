# 建置內容根目錄為 repo 根目錄（見 docker-compose.yml 的 build.context）。
FROM node:24-alpine
WORKDIR /app
COPY file-server/package.json file-server/package-lock.json ./
RUN npm ci --omit=dev
COPY file-server/src ./src
ENV FILE_HOST=0.0.0.0 FILE_PORT=5190 FILE_ROOT=/data
# 上傳暫存檔寫在 /data/tmp（compose 掛 file-tmp volume）。映像裡沒有 /data 時，Docker 會以 root 建立掛載點，
# 下面切成 node 之後寫不進去，上傳一律回 500「檔案服務發生錯誤」。先建好並交給 node，
# 全新的 named volume 第一次掛載時會沿用這個擁有者。
RUN mkdir -p /data && chown node:node /data
USER node
EXPOSE 5190
CMD ["node", "src/server.mjs"]
