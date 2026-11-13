#!/usr/bin/env bash
# deploy/*.sh 的共用函式庫，由其他腳本 source，不直接執行。
#
# 所有腳本都透過 dc() 呼叫 docker compose，確保一律帶上 shared-db overlay 與
# deploy/.env。漏帶 overlay 時 api 會連不到共用 MariaDB，錯誤訊息卻看不出原因。
# 呼叫端必須先 cd 到 repo 根目錄（-f 用相對路徑）。

COMPOSE_FILE_ARGS=(-f docker-compose.yml -f docker-compose.shared-db.yml)

# 讀取 .env 檔裡某個 key 的值：取最後一次出現、允許值內含 = 號、去掉成對的雙引號。
# 去掉 \r：demo 機的設定檔可能在 Windows（Hyper-V 主機）上編輯過，CRLF 會讓密碼多一個看不見的字元。
quote_env_value() {
  local key="$1" file="$2"
  grep -E "^${key}=" "$file" 2>/dev/null | tail -1 | cut -d= -f2- | tr -d '\r' \
    | sed -e 's/^"//' -e 's/"$//' || true
}

dc() {
  docker compose "${COMPOSE_FILE_ARGS[@]}" --env-file deploy/.env "$@"
}

# 共用 MariaDB 的 container id；找不到時回傳空字串，由呼叫端決定錯誤訊息。
quote_mariadb_container() {
  local name
  name="$(quote_env_value SHARED_DB_CONTAINER "$1")"
  name="${name:-mariadb}"
  docker ps -q --filter "name=^${name}$" 2>/dev/null || true
}

# 以 root 在共用 MariaDB 執行指令：quote_db_exec <container> <root 密碼> <指令...>
# 密碼用「-e MYSQL_PWD」不帶值的形式從環境變數傳入，不出現在主機的處理程序參數裡。
quote_db_exec() {
  local container="$1" password="$2"
  shift 2
  MYSQL_PWD="$password" docker exec -i -e MYSQL_PWD "$container" "$@"
}

quote_shared_network() {
  local network
  network="$(quote_env_value SHARED_DB_NETWORK "$1")"
  echo "${network:-mariadb-network}"
}

# 本系統 MinIO 的 volume 名稱。shared-db overlay 把它改成 bind-backed，但仍是帶有
# compose label 的 volume，所以用 label 找，不寫死 project 前綴。
quote_minio_volume() {
  docker volume ls -q --filter label=com.docker.compose.project=quote-platform \
    | grep -E 'minio-data$' | head -1 || true
}

# type=none,o=bind 的 device 目錄必須事先存在，否則建立 volume 時才失敗。
quote_ensure_data_dirs() {
  local data_root
  data_root="$(quote_env_value DATA_ROOT "$1")"
  if [[ -z "$data_root" ]]; then
    echo "[錯誤] deploy/.env 沒有設定 DATA_ROOT。" >&2
    return 1
  fi
  if ! mkdir -p "${data_root}/quote/minio" 2>/dev/null; then
    echo "[錯誤] 無法建立 ${data_root}/quote/minio。請先執行：" >&2
    echo "       sudo mkdir -p ${data_root} && sudo chown \"\$USER\" ${data_root}" >&2
    return 1
  fi
}

# 建置三個應用映像（名稱需與 docker-compose.yml 的 image: 一致）。用法：quote_build_images <tag> [platform]
# 不用 docker compose build：repo 路徑含中文時，compose 的 BuildKit session 會把路徑放進 HTTP 標頭，
# 以「x-docker-expose-session-sharedkey contains value with non-printable ASCII characters」失敗。
# --provenance=false／--sbom=false：預設附掛的 attestation 會讓 docker save 產出 manifest list，
# 部分 docker load 的處理不一致；離線交付要最單純的單平台映像。
quote_build_images() {
  local tag="$1" platform="${2:-}" spec
  local -a platform_args=()
  [[ -n "$platform" ]] && platform_args=(--platform "$platform")
  for spec in "apps/api/QuotePlatform.Api/Dockerfile:quote-platform-api" \
              "file-server/Dockerfile:quote-platform-file-server" \
              "apps/web/Dockerfile:quote-platform-web-build"; do
    # macOS 內建 bash 3.2 在 set -u 下展開空陣列會報 unbound variable，所以用 ${arr[@]+...} 的寫法。
    DOCKER_BUILDKIT=1 docker buildx build ${platform_args[@]+"${platform_args[@]}"} \
      --provenance=false --sbom=false --load -f "${spec%%:*}" -t "${spec##*:}:${tag}" . || return 1
  done
}
