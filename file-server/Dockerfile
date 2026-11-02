# 建置內容根目錄為 repo 根目錄（見 docker-compose.yml 的 build.context）。
FROM node:24-alpine
WORKDIR /app
COPY file-server/package.json file-server/package-lock.json ./
RUN npm ci --omit=dev
COPY file-server/src ./src
ENV FILE_HOST=0.0.0.0 FILE_PORT=5190 FILE_ROOT=/data
USER node
EXPOSE 5190
CMD ["node", "src/server.mjs"]
