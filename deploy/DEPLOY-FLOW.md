# 部署流程

同一套腳本部署到三個環境，差異全部放在 `deploy/.env.<環境>`。映像在開發機打包，demo／正式機只載入、不重新建置。

| 環境 | 機器 | 設定檔 | 映像來源 |
|---|---|---|---|
| `development` | Mac 開發機（用 Docker 跑完整 stack 驗證） | `deploy/.env.development` | `build`：本機建置 |
| `demo` | Hyper-V Ubuntu | `deploy/.env.demo` | `offline`：載入 tar |
| `production` | 正式機 | `deploy/.env.production` | `offline`：載入 tar |

平常開發（`dotnet run`、`npm run dev:*`）不走這套流程，用根目錄的 `.env.development-api.local`。

## 指令一覽

| 指令 | 說明 |
|---|---|
| `./deploy/deploy.sh --environment=<環境>` | 部署（選設定檔 → 準備映像 → 建資料庫帳號 → 啟動 → 等 API 就緒） |
| `./deploy/export-images.sh --environment=<環境>` | 開發機打包映像（linux/amd64），輸出到 `deploy/dist/` |
| `./deploy/backup.sh` | 備份 `quote_platform` 與 MinIO 到 `backups/` |
| `./deploy/restore.sh` | 還原備份（破壞性，需輸入 `yes`） |
| `./deploy/dc.sh <docker compose 參數>` | 維運用，自動帶上 shared-db overlay 與 `deploy/.env` |

`deploy.sh` 的選項：`--migrate`（這次啟動套用 migration）、`--bootstrap`（這次啟動執行 Seeder）、`--restore-backup`（API 啟動前先還原）、`--skip-image-load`、`--image-tar=<路徑>`、`--build`／`--offline`（覆寫 `DEPLOY_MODE`）。`--migrate`、`--bootstrap` 只對該次部署生效，API 就緒後會以 `.env` 的設定重建 api。

## 第一次部署某個環境

1. 目標機有 Docker，且共用 MariaDB 已在執行（demo／正式機的 `mariadb`、network `mariadb-network`）。
2. 第一次執行會從範本建立設定檔並中止，填完後再跑一次：

   ```bash
   ./deploy/deploy.sh --environment=demo
   $EDITOR deploy/.env.demo
   ```

   必填：`DB_PASSWORD`、`DB_ROOT_PASSWORD`（與共用 MariaDB 的 root 密碼相同）、`JWT_KEY`、`FILE_TOKEN_KEY`、`FILE_CALLBACK_SECRET`、`MINIO_ROOT_PASSWORD`、`DATA_ROOT`。`demo`、`production` 若仍有 `change-me` 會被擋下。
3. `DATA_ROOT` 目錄要事先建立並交給執行者：`sudo mkdir -p /srv/quote-platform/data && sudo chown "$USER" /srv/quote-platform/data`。執行者需在 `docker` 群組。
4. 資料庫從哪裡來，二選一：
   - **全新資料庫**：設定 `BOOTSTRAP_ADMIN_PASSWORD` 後執行 `./deploy/deploy.sh --environment=demo --bootstrap`，會建立資料表、權限目錄與 admin。登入改密碼後把 `BOOTSTRAP_ADMIN_PASSWORD` 清空。
   - **沿用其他機器的資料**：見下方「把開發機的資料搬到 demo 機」。

腳本啟動前會比對資料庫與程式的最新 migration：全新資料庫沒加 `--bootstrap`、或 schema 落後沒加 `--migrate`，都會直接中止並說明原因，不會帶著錯誤的資料庫啟動（`/api/health` 不碰資料庫，光看健康檢查發現不了）。

> 設定只改 `deploy/.env.<環境>`。`deploy/.env` 每次部署都會被覆寫。密碼與金鑰不要含 `; $ ' "`，建議用 `openssl rand -hex 24` 產生。

## demo／正式機的目錄配置

Hyper-V 主機慣例是一個專案一個 `/srv/<專案名>`。程式碼與資料分開放，更新程式時整個換掉 `app/` 也不會動到資料：

```
/srv/quote-platform/
├── app/                                   ← repo（部署與維運指令都在這裡執行）
│   ├── docker-compose.yml  docker-compose.shared-db.yml
│   ├── apps/api/QuotePlatform.Api/Migrations/   ← deploy.sh 用它比對 schema 版本，不能省
│   ├── deploy/
│   │   ├── *.sh
│   │   ├── config/ai-settings.json        ← api 容器掛載，不能省
│   │   ├── .env.demo                      ← 你填的設定
│   │   ├── .env                           ← deploy.sh 每次覆寫產生
│   │   └── dist/quote-platform-images-*.tar.gz
│   └── backups/                           ← 備份輸出
└── data/                                  ← DATA_ROOT
    └── quote/minio/                       ← 附件與公司 Logo
```

- `DATA_ROOT=/srv/quote-platform/data`（範本已填），目錄要事先建立：`sudo mkdir -p /srv/quote-platform/{app,data} && sudo chown -R "$USER" /srv/quote-platform`。
- compose 專案名稱固定為 `quote-platform`，所以 repo 資料夾取名 `app` 不影響 volume、網路與映像名稱。
- MariaDB 資料在共用 `mariadb` 容器自己的 volume，不在這個目錄。
- `demo`／`production` 不需要 `tests/`、`kenney-downloads/`、`.claude/`；用 `git archive --format=tar.gz -o quote-platform-src.tar.gz HEAD` 可以只打包已 commit 的檔案。

## demo／正式機（離線映像）

```bash
# 開發機
./deploy/export-images.sh --environment=demo        # 產生 deploy/dist/quote-platform-images-*.tar.gz

# 把 repo（不含 .git、node_modules、backups）與 tar 交給目標機，tar 放進 deploy/dist/
# 目標機
./deploy/deploy.sh --environment=demo
```

`export-images.sh` 的環境參數用來讀 `IMAGE_TAG`／`MINIO_TAG`，打包與目標機的值必須一致，否則 `deploy.sh` 會列出缺少的映像。

## 之後更新版本

```bash
./deploy/backup.sh                                   # 先備份
./deploy/deploy.sh --environment=demo                # 一般更新
./deploy/deploy.sh --environment=demo --migrate      # 這個版本有 schema 變更
./deploy/deploy.sh --environment=demo --bootstrap    # 這個版本有新增權限，需要跑 Seeder
```

更新時**不要**帶 `--restore-backup`。

## 把開發機的資料搬到 demo 機

在開發機先 `npm run demo:reset`（只留平台帳號、demo 與展示公司，會先備份），然後：

```bash
# 開發機：還沒用 Docker 部署過也能跑，只需要 deploy/.env.development 裡的 SHARED_DB_CONTAINER 與 DB_ROOT_PASSWORD
./deploy/backup.sh --environment=development   # 產生 backups/mariadb/quote_platform_<時間>.sql
```

把 `backups/mariadb/` 的最新檔案放到 demo 機 repo 的 `backups/mariadb/`，再執行：

```bash
./deploy/deploy.sh --environment=demo --restore-backup
```

- 這會 `DROP` 並重建 `quote_platform`，所以只用在新機器首次移轉資料。`backups/minio/` 有檔案時也會清空並還原 MinIO；沒有就略過。
- 檔案本體不在資料庫裡。開發機用 `npm run dev:file` 時，上傳的檔案在 `file-server/data/objects`，不在 MinIO，`backup.sh` 不會備份到；有附件或公司 Logo 時要另外匯入 demo 機的 MinIO，否則會找不到檔案。
- 資料庫裡的 admin 密碼會一起過去；來源與目標的 `JWT_KEY` 不同，所有人需重新登入。
- 備份是 `mariadb-dump` 不含 `CREATE DATABASE`，也可以手動匯入：`docker exec -i mariadb mariadb -uroot -p quote_platform < xxx.sql`，匯入後需重新執行 `./deploy/ensure-shared-db.sh` 授權。

## 前置反向代理（HTTPS）

web 容器只綁 `WEB_BIND:WEB_PORT`（預設 `127.0.0.1:8096`），由前面的 nginx 終止 TLS 後轉發。與美達特不同、需要特別交代的設定：

```nginx
client_max_body_size 20m;            # 檔案上傳上限
location = /api/quote-imports {      # AI 圖像識別會等比較久
    proxy_pass http://127.0.0.1:8096;
    proxy_read_timeout 330s;
}
location /api/hubs/ {                # 虛擬辦公室 SignalR 需要 WebSocket
    proxy_pass http://127.0.0.1:8096;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
}
```

其餘照一般反向代理，三個標頭都要帶：

```nginx
proxy_set_header Host              $host;
proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $scheme;
```

正式機的 `REFRESH_COOKIE_SECURE` 要是 `true`。

### 使用者真實 IP（`TRUSTED_PROXY_CIDR`）

登入節流與操作紀錄依賴使用者 IP。正式機的請求經過「前置 nginx → web 容器 nginx → API」兩層，web 容器看到的連線來源是 Docker 閘道，不是使用者。web 容器的 nginx 只在連線來源屬於 `TRUSTED_PROXY_CIDR` 時，才從 `X-Forwarded-For` 由右往左取第一個不受信任的位址當作使用者 IP，再只把這一筆傳給 API。使用者自己帶的偽造值在最左邊，不會被採用。

| 環境 | 設定 | 說明 |
|---|---|---|
| production | `172.16.0.0/12` | 前置 nginx 在同一台主機時，連線從 Docker 閘道進來。部署完成時腳本會印出實際閘道，不在範圍內就改成閘道 IP（例如 `192.168.16.1/32`）；前置 nginx 在別台機器時填那台的 IP |
| demo／development | `127.0.0.1/32` | 不信任任何代理。Hyper-V 預設交換器常用 `172.x`，信任 Docker 網段會讓 Hyper-V 主機上的人能偽造 IP |

信任 Docker 網段時必須 `WEB_BIND=127.0.0.1`，否則直接連到 port 的人也會被當成前置代理（腳本會警告）。設錯的症狀是操作紀錄裡所有人的 IP 都一樣，或一個人打錯密碼太多次，所有人都被擋。

## 維運注意事項

- **定期備份**：腳本不會自己排程。demo／正式機用 crontab，例如每天 02:00：`0 2 * * * cd /srv/quote-platform/app && ./deploy/backup.sh >> backups/backup.log 2>&1`。備份與資料在同一台機器，主機壞掉會一起消失，要定期把 `backups/` 複製到別台。
- **防火牆**：Docker 發布的 port 不受 ufw 規則限制。demo 機 `WEB_BIND=0.0.0.0` 時，能連到這台 VM 的人都能開後台；正式機請維持 `127.0.0.1`。
- **版本回滾**：`IMAGE_TAG=latest` 時，載入新版會覆蓋舊映像，無法切回。需要回滾能力時，每次發版把 `IMAGE_TAG` 改成日期（例如 `2026-11-12`），開發機與目標機的設定檔要一致；回滾時改回舊標籤再部署即可（schema 已升級的版本不能回滾）。
- **磁碟**：`deploy/dist/` 的舊 tar 與 `docker image prune` 可清掉的舊映像要定期清理。容器日誌已設定輪替（每個服務最多 5 × 10MB）。
- **開發機的 Docker stack 與 `npm run dev:api` 共用同一個 `quote_platform`**，但檔案儲存不同（MinIO vs `file-server/data`），在一邊上傳的附件另一邊看不到。

## 排錯

| 症狀 | 常見原因 |
|---|---|
| 容器啟動即 `exec format error` | 映像不是 amd64。回開發機重跑 `export-images.sh`（它會在打包前擋下） |
| `Access denied for user 'quote_app'` | 密碼與 `.env` 不一致，或還原後沒重新授權；重跑 `./deploy/ensure-shared-db.sh` |
| 找不到 network `mariadb-network` | 共用 MariaDB 沒啟動，或 `SHARED_DB_NETWORK` 填錯（Mac 是 `shared-db`） |
| API 一直沒就緒 | `./deploy/dc.sh logs --tail=100 api` |
| 部署中止：「schema 落後」 | 這個版本有 migration：先 `./deploy/backup.sh`，再加 `--migrate` |
| 部署中止：「全新資料庫」 | 加 `--bootstrap` 並設定 `BOOTSTRAP_ADMIN_PASSWORD`，或改用 `--restore-backup` |
| 所有使用者 IP 都一樣、一人打錯密碼大家都被擋 | `TRUSTED_PROXY_CIDR` 沒涵蓋前置 nginx 的連線來源，見「使用者真實 IP」 |
| `$'\r': command not found` | 腳本經過 Windows 被轉成 CRLF；repo 已用 `.gitattributes` 固定 LF，手動複製時請保留 LF |
| 附件、Logo 打不開 | 檔案沒搬過來（只還原了資料庫），或 MinIO 帳密與還原來源不一致 |
