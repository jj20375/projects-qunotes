#!/usr/bin/env bash
# 一支指令部署到任何環境。環境差異全部放在 deploy/.env.<環境>，腳本本身不分環境。
#
#   ./deploy/deploy.sh --environment=development   Mac 開發機，本機建置映像
#   ./deploy/deploy.sh --environment=demo          Hyper-V Ubuntu demo 機，載入打包好的映像
#   ./deploy/deploy.sh --environment=production    正式機，載入打包好的映像
#
# 選項：
#   --build / --offline        覆寫 .env 的 DEPLOY_MODE（build＝本機建置；offline＝docker load 映像 tar）
#   --image-tar=<路徑>         指定映像 tar；預設取 deploy/dist/ 最新的 quote-platform-images-*.tar*
#   --skip-image-load          映像已載入過，跳過 docker load
#   --migrate                  這一次啟動時套用資料庫 migration（資料庫 schema 落後時腳本會要求加上）
#   --bootstrap                這一次啟動時執行 Seeder（全新資料庫必須加；既有 admin 密碼不會被覆寫）
#   --restore-backup           啟動 API 之前先還原 backups/ 下最新一組備份（只在新機器首次移轉資料時用）
#
# --migrate、--bootstrap 只對這一次部署生效：API 健康後會以 .env 的設定重建 api，
# 避免旗標殘留在容器裡，之後每次重啟都再跑一次。
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
env_path="${repo_root}/deploy/.env"
migrations_dir="${repo_root}/apps/api/QuotePlatform.Api/Migrations"

# shellcheck source=deploy/compose-lib.sh
source "${script_dir}/compose-lib.sh"

environment=""
mode_override=""
image_tar=""
skip_image_load=false
migrate=false
bootstrap=false
restore_backup=false

for arg in "$@"; do
  case "$arg" in
    --environment=development|--environment=demo|--environment=production) environment="${arg#*=}" ;;
    --environment=*) echo "[錯誤] --environment 只接受 development、demo、production。" >&2; exit 1 ;;
    --build) mode_override=build ;;
    --offline) mode_override=offline ;;
    --image-tar=*) image_tar="${arg#*=}" ;;
    --skip-image-load) skip_image_load=true ;;
    --migrate|--migrate-on-start) migrate=true ;;
    --bootstrap) bootstrap=true ;;
    --restore-backup) restore_backup=true ;;
    -h|--help) sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "[錯誤] 未知參數：$arg（--help 查看用法）" >&2; exit 1 ;;
  esac
done

cd "$repo_root"

if ! docker info >/dev/null 2>&1; then
  echo "[錯誤] Docker 沒有在執行，或目前使用者沒有權限（Ubuntu 需加入 docker 群組）。" >&2
  exit 1
fi

# ── 選定環境設定檔 ───────────────────────────────────────────
if [[ -n "$environment" ]]; then
  profile_path="${repo_root}/deploy/.env.${environment}"
  if [[ ! -f "$profile_path" ]]; then
    if [[ ! -f "${profile_path}.example" ]]; then
      echo "[錯誤] 找不到 ${profile_path}.example。" >&2
      exit 1
    fi
    cp "${profile_path}.example" "$profile_path"
    chmod 600 "$profile_path"
    echo "[OK] 已建立 deploy/.env.${environment}。"
    echo "[中止] 請先填妥密碼、金鑰與 DATA_ROOT，再重新執行同一個指令。"
    exit 1
  fi
  # 每次部署都會覆寫 deploy/.env；在檔頭註明來源，避免有人直接改 deploy/.env 後被下次部署蓋掉。
  {
    echo "# 由 deploy/deploy.sh 從 deploy/.env.${environment} 複製產生；要改設定請改 deploy/.env.${environment}。"
    tr -d '\r' < "$profile_path"
  } > "$env_path"
  chmod 600 "$env_path"
  echo "[INFO] 部署環境：${environment}"
elif [[ -f "$env_path" ]]; then
  echo "[INFO] 未指定 --environment，沿用現有 deploy/.env。"
else
  echo "[錯誤] 請指定 --environment=development|demo|production。" >&2
  exit 1
fi

placeholders="$(grep -v '^[[:space:]]*#' "$env_path" | grep 'change-me' | cut -d= -f1 || true)"
if [[ -n "$placeholders" ]]; then
  echo "[警告] 這些設定還是範本預設值（change-me）：" >&2
  echo "$placeholders" | sed 's/^/  /' >&2
  # 開發機允許帶著範本值試跑；demo／正式機的密碼與金鑰必須換掉。
  if [[ "$environment" != "development" ]]; then
    echo "[錯誤] ${environment:-此} 環境不可使用範本預設值，請修改後重新執行。" >&2
    exit 1
  fi
fi

for key in DB_PASSWORD DB_ROOT_PASSWORD DATA_ROOT; do
  if [[ -z "$(quote_env_value "$key" "$env_path")" ]]; then
    echo "[錯誤] deploy/.env 缺少 ${key}。" >&2
    exit 1
  fi
done

# 連線字串以 ; 分隔、compose 會展開 $，這些字元出現在密碼裡會在執行期才以難懂的方式失敗。
db_password="$(quote_env_value DB_PASSWORD "$env_path")"
if [[ "$db_password" == *[\;\$\'\"]* ]]; then
  echo "[錯誤] DB_PASSWORD 不可包含 ; \$ ' \" 等字元（建議用 openssl rand -hex 24 產生）。" >&2
  exit 1
fi

# nginx 的 geo／set_real_ip_from 只接受單一位址或 CIDR；格式錯誤時 web 會起不來，只看得到健康檢查逾時。
trusted_proxy="$(quote_env_value TRUSTED_PROXY_CIDR "$env_path")"
if [[ -n "$trusted_proxy" && ! "$trusted_proxy" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}(/[0-9]{1,2})?$ ]]; then
  echo "[錯誤] TRUSTED_PROXY_CIDR 只能填一個 IPv4 位址或 CIDR（例如 172.16.0.0/12），目前是：${trusted_proxy}" >&2
  exit 1
fi
# 信任 Docker 網段時，能連到 published port 的人都會被當成前置代理；只綁本機才能確保只有前置 nginx 連得到。
web_bind_value="$(quote_env_value WEB_BIND "$env_path")"
if [[ -n "$trusted_proxy" && "$trusted_proxy" != "127.0.0.1/32" && "${web_bind_value:-127.0.0.1}" != "127.0.0.1" ]]; then
  echo "[警告] TRUSTED_PROXY_CIDR=${trusted_proxy} 但 WEB_BIND=${web_bind_value}：直接連到這個 port 的人可能可以偽造 IP 繞過登入節流。" >&2
  echo "       有前置代理時請把 WEB_BIND 設為 127.0.0.1。" >&2
fi

deploy_mode="${mode_override:-$(quote_env_value DEPLOY_MODE "$env_path")}"
deploy_mode="${deploy_mode:-build}"
case "$deploy_mode" in
  build|offline) ;;
  *) echo "[錯誤] DEPLOY_MODE 只接受 build 或 offline，目前是：${deploy_mode}" >&2; exit 1 ;;
esac
echo "[INFO] 映像來源：${deploy_mode}"

image_tag="$(quote_env_value IMAGE_TAG "$env_path")"; image_tag="${image_tag:-latest}"
minio_tag="$(quote_env_value MINIO_TAG "$env_path")"; minio_tag="${minio_tag:-RELEASE.2025-09-07T16-13-09Z}"

# ── 準備映像 ─────────────────────────────────────────────────
if [[ "$deploy_mode" == "offline" ]]; then
  if [[ "$skip_image_load" == false ]]; then
    if [[ -z "$image_tar" ]]; then
      image_tar="$(ls -t "${repo_root}"/deploy/dist/quote-platform-images-*.tar* 2>/dev/null | head -1 || true)"
      if [[ -z "$image_tar" ]]; then
        echo "[錯誤] deploy/dist/ 沒有 quote-platform-images-*.tar*。請在開發機執行 ./deploy/export-images.sh --environment=${environment:-demo} 後把檔案放進來。" >&2
        exit 1
      fi
    fi
    echo "[INFO] 載入映像：${image_tar}（需要一點時間）…"
    docker load -i "$image_tar"
  fi

  # 名稱必須與 docker-compose.yml 的 image: 一致，否則 compose 會判定映像不存在而嘗試建置。
  image_error=0
  for image in "quote-platform-api:${image_tag}" "quote-platform-file-server:${image_tag}" \
               "quote-platform-web:${image_tag}" "minio/minio:${minio_tag}" "alpine:latest"; do
    arch="$(docker image inspect --format '{{.Architecture}}' "$image" 2>/dev/null || true)"
    if [[ -z "$arch" ]]; then
      echo "  [X] 缺少映像：${image}" >&2
      image_error=1
    else
      echo "  [OK] ${image} (${arch})"
    fi
  done
  if [[ "$image_error" -ne 0 ]]; then
    echo "[錯誤] 映像不齊全。請確認打包時的 IMAGE_TAG／MINIO_TAG 與 deploy/.env 一致。" >&2
    exit 1
  fi
else
  echo "[INFO] 建置映像…"
  dc build
fi

# ── 共用 MariaDB 與資料目錄 ──────────────────────────────────
shared_net="$(quote_shared_network "$env_path")"
if ! docker network inspect "$shared_net" >/dev/null 2>&1; then
  echo "[錯誤] 找不到 network '${shared_net}'。共用 MariaDB 必須先在這台主機啟動（本系統不會自己建立）。" >&2
  exit 1
fi
quote_ensure_data_dirs "$env_path"
bash "${script_dir}/ensure-shared-db.sh"

# 還原必須在 API 啟動之前：API 一旦先啟動，可能已經對空資料庫套用 migration 或寫入資料。
if [[ "$restore_backup" == true ]]; then
  dc up -d --no-build minio
  bash "${script_dir}/restore.sh"
fi

# ── 檢查資料庫狀態 ───────────────────────────────────────────
# /api/health 不碰資料庫，schema 落後時 API 照樣「健康」，要等使用者登入才 500，
# 所以在啟動前比對資料庫與程式的最新 migration，不符就要求明確加旗標。
db_container="$(quote_mariadb_container "$env_path")"
db_root_password="$(quote_env_value DB_ROOT_PASSWORD "$env_path")"
db_query() {
  quote_db_exec "$db_container" "$db_root_password" mariadb -uroot -N -B -e "$1" quote_platform
}
migrate_enabled=$migrate
bootstrap_enabled=$bootstrap
[[ "$(quote_env_value MIGRATE_ON_START "$env_path")" == "true" ]] && migrate_enabled=true
[[ "$(quote_env_value BOOTSTRAP_ON_START "$env_path")" == "true" ]] && bootstrap_enabled=true

latest_code="$(ls "$migrations_dir" 2>/dev/null | grep -E '^[0-9]{14}_.+\.cs$' | grep -v '\.Designer\.cs$' | sort | tail -1 | sed 's/\.cs$//' || true)"
has_history="$(db_query "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'quote_platform' AND table_name = '__EFMigrationsHistory';")"

if [[ "$has_history" == "0" ]]; then
  echo "[INFO] quote_platform 是全新資料庫。"
  if [[ "$bootstrap_enabled" != true ]]; then
    echo "[錯誤] 全新資料庫需要建立資料表、權限目錄與 admin：請加 --bootstrap 重新執行。" >&2
    echo "       要沿用其他機器的資料則改用 --restore-backup。" >&2
    exit 1
  fi
  if [[ -z "$(quote_env_value BOOTSTRAP_ADMIN_PASSWORD "$env_path")" ]]; then
    echo "[錯誤] 全新資料庫需要在 deploy/.env.${environment:-<環境>} 設定 BOOTSTRAP_ADMIN_PASSWORD，否則不會建立 admin，沒有帳號能登入。" >&2
    exit 1
  fi
elif [[ -z "$latest_code" ]]; then
  echo "[警告] 找不到 ${migrations_dir}，略過 schema 版本檢查。" >&2
else
  latest_db="$(db_query "SELECT MAX(MigrationId) FROM __EFMigrationsHistory;")"
  echo "[INFO] 資料庫 schema：${latest_db}；程式：${latest_code}"
  if [[ "$latest_db" < "$latest_code" && "$migrate_enabled" != true && "$bootstrap_enabled" != true ]]; then
    echo "[錯誤] 資料庫 schema 落後於這個版本的程式。請先 ./deploy/backup.sh，再加 --migrate 重新執行。" >&2
    exit 1
  fi
  if [[ "$latest_db" > "$latest_code" ]]; then
    echo "[警告] 資料庫 schema 比程式新（可能是部署了舊版），舊版程式可能無法正常運作。" >&2
  fi
fi

# ── 啟動 ─────────────────────────────────────────────────────
if [[ "$migrate" == true ]]; then
  export MIGRATE_ON_START=true
  echo "[INFO] 本次啟動套用資料庫 migration。"
fi
if [[ "$bootstrap" == true ]]; then
  export BOOTSTRAP_ON_START=true
  echo "[INFO] 本次啟動執行 Seeder。"
fi

# build 模式上面已經建置過，這裡兩種模式都不再建置。
dc up -d --no-build --remove-orphans

# 用 web 容器自己的 busybox wget 檢查：API 映像沒有 curl／wget，且這樣也驗到 nginx → api 這條路。
# 必須寫 127.0.0.1：容器內 localhost 先解析成 ::1，而 nginx 只監聽 IPv4，會一直 Connection refused。
wait_for_api() {
  for _ in $(seq 1 40); do
    if dc exec -T web wget -qO- http://127.0.0.1/api/health >/dev/null 2>&1; then return 0; fi
    sleep 3
  done
  return 1
}

echo "[INFO] 等待 API 就緒…"
if ! wait_for_api; then
  echo "[錯誤] API 在預期時間內沒有就緒。請查看：./deploy/dc.sh logs --tail=100 api" >&2
  exit 1
fi

# opt-in 旗標只該生效一次：容器是帶著旗標建立的，不重建的話之後 docker restart 會再跑一次。
if [[ "$migrate" == true || "$bootstrap" == true ]]; then
  unset MIGRATE_ON_START BOOTSTRAP_ON_START
  echo "[INFO] 以 .env 的設定重建 api（移除本次的一次性旗標）…"
  dc up -d --no-build api
  wait_for_api || { echo "[錯誤] api 重建後沒有就緒：./deploy/dc.sh logs --tail=100 api" >&2; exit 1; }
fi

if [[ "$bootstrap" == true && "$has_history" == "0" ]]; then
  if [[ "$(db_query "SELECT COUNT(*) FROM users WHERE Account = 'admin';" 2>/dev/null || echo 0)" == "0" ]]; then
    echo "[警告] Seeder 沒有建立 admin，通常是 BOOTSTRAP_ADMIN_PASSWORD 不符合密碼規則；請看 ./deploy/dc.sh logs api。" >&2
  fi
fi

web_bind="$(quote_env_value WEB_BIND "$env_path")"; web_bind="${web_bind:-127.0.0.1}"
web_port="$(quote_env_value WEB_PORT "$env_path")"; web_port="${web_port:-8096}"
echo ""
echo "==== 部署完成（${environment:-既有 .env}／${deploy_mode}）===="
echo "後台入口：http://${web_bind}:${web_port}/"
echo "維運指令：./deploy/dc.sh ps | logs -f api | up -d api"
if [[ -n "$trusted_proxy" && "$trusted_proxy" != "127.0.0.1/32" ]]; then
  gateway="$(docker network inspect quote-platform_default --format '{{range .IPAM.Config}}{{.Gateway}} {{end}}' 2>/dev/null || true)"
  echo "信任的前置代理：${trusted_proxy}；web 所在網路的閘道：${gateway:-（查不到）}"
  echo "  前置 nginx 在這台主機時，閘道必須落在信任範圍內，否則所有使用者 IP 都會一樣。"
fi
if [[ "$bootstrap" == true && "$has_history" == "0" ]]; then
  echo "admin 已建立：登入後請立即修改密碼，並把 deploy/.env.${environment:-<環境>} 的 BOOTSTRAP_ADMIN_PASSWORD 清空。"
fi
if [[ "$restore_backup" == true ]]; then
  echo "已還原備份：JWT_KEY 與來源環境不同時，所有人需重新登入。"
fi
