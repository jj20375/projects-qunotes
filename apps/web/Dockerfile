# 建置內容根目錄為 repo 根目錄（見 docker-compose.yml 的 build.context）。
# Astro 7 需要 Node >= 22.23.4；與本機 .nvmrc 一致使用 Node 24。
FROM node:24-alpine AS build
WORKDIR /src/apps/web
COPY apps/web/package.json apps/web/package-lock.json ./
RUN npm ci
COPY apps/web/ ./
RUN npm run build

FROM nginx:1.27-alpine
# 官方 entrypoint 啟動時把 templates/*.template 代入環境變數後輸出到 conf.d/。
# 只允許代換 TRUSTED_PROXY_CIDR，其他 $ 開頭的都是 nginx 自己的變數；預設不信任任何代理。
ENV NGINX_ENVSUBST_FILTER=TRUSTED_PROXY_CIDR \
    TRUSTED_PROXY_CIDR=127.0.0.1/32
COPY infra/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/apps/web/dist/ /usr/share/nginx/html/
