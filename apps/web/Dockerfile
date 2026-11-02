# 建置內容根目錄為 repo 根目錄（見 docker-compose.yml 的 build.context）。
# Astro 7 需要 Node >= 22.23.4；與本機 .nvmrc 一致使用 Node 24。
FROM node:24-alpine AS build
WORKDIR /src/apps/web
COPY apps/web/package.json apps/web/package-lock.json ./
RUN npm ci
COPY apps/web/ ./
RUN npm run build

FROM nginx:1.27-alpine
COPY infra/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/apps/web/dist/ /usr/share/nginx/html/
