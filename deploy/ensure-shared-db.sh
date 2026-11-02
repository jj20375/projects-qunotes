#!/usr/bin/env bash
# 在共用 MariaDB 上建立 quote_platform 資料庫與 quote_app 帳號（沿用美達特 deploy/ensure-shared-db.sh 的做法）。
#
# 可重複執行：IF NOT EXISTS，並每次把 quote_app 密碼重設為 deploy/.env 的 DB_PASSWORD。
# quote_app 只授權 quote_platform.*；root 密碼只在這支腳本使用，不交給 API container。
#
# 用法：./deploy/ensure-shared-db.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_path="${repo_root}/deploy/.env"
[[ -f "$env_path" ]] || { echo "[錯誤] 找不到 ${env_path}，請由 deploy/.env.example 複製。" >&2; exit 1; }

env_value() { grep -E "^$1=" "$env_path" | tail -1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//'; }
container="$(env_value SHARED_DB_CONTAINER)"; container="${container:-mariadb}"
db_password="$(env_value DB_PASSWORD)"
db_root_password="$(env_value DB_ROOT_PASSWORD)"
[[ -n "$db_password" ]] || { echo "[錯誤] deploy/.env 缺少 DB_PASSWORD。" >&2; exit 1; }
[[ -n "$db_root_password" ]] || { echo "[錯誤] deploy/.env 缺少 DB_ROOT_PASSWORD（共用 MariaDB 的 root 密碼）。" >&2; exit 1; }
[[ "$db_password" != *"'"* ]] || { echo "[錯誤] DB_PASSWORD 不可包含單引號。" >&2; exit 1; }

docker ps --format '{{.Names}}' | grep -qx "$container" || {
  echo "[錯誤] 找不到執行中的共用 MariaDB container：${container}。" >&2
  exit 1
}

echo "[INFO] 等待共用 MariaDB 可連線…"
for _ in $(seq 1 40); do
  if docker exec "$container" mariadb -uroot -p"$db_root_password" -e "SELECT 1;" >/dev/null 2>&1; then ready=1; break; fi
  sleep 3
done
[[ "${ready:-0}" == 1 ]] || { echo "[錯誤] 無法以 root 連線共用 MariaDB，請確認 DB_ROOT_PASSWORD。" >&2; exit 1; }

echo "[INFO] 建立 quote_platform 與 quote_app（可重複執行）…"
docker exec -i "$container" mariadb -uroot -p"$db_root_password" <<SQL
CREATE DATABASE IF NOT EXISTS \`quote_platform\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'quote_app'@'%' IDENTIFIED BY '${db_password}';
ALTER USER 'quote_app'@'%' IDENTIFIED BY '${db_password}';
GRANT ALL PRIVILEGES ON \`quote_platform\`.* TO 'quote_app'@'%';
FLUSH PRIVILEGES;
SQL
echo "[INFO] 完成。"
