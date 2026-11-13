# 前端建置工具映像（quote-platform-web-build）：只含 Node、相依套件與原始碼，不含建置結果。
# 建置內容根目錄為 repo 根目錄（見 docker-compose.yml 的 build.context）。
#
# 部署前綴（APP_BASE_PATH）由各環境的 deploy/.env 決定，deploy.sh 每次部署在目標機用這個映像產生靜態檔，
# 寫進 web-dist volume 給 web（nginx）讀取。前綴不寫死在映像裡，同一份映像可以用在根目錄與子路徑的環境，
# demo 機也不必為了前綴另外打包。
#
# Astro 7 需要 Node >= 22.23.4；與本機 .nvmrc 一致使用 Node 24。
FROM node:24-alpine
WORKDIR /src/apps/web
COPY apps/web/package.json apps/web/package-lock.json ./
RUN npm ci
COPY apps/web/ ./
# 先建置成功才清空並替換 /out：建置失敗時 volume 裡保留上一版，網站不會只剩一半的檔案。
CMD ["sh", "-c", "npm run build && find /out -mindepth 1 -delete && cp -a dist/. /out/"]
