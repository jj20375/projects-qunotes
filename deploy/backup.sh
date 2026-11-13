#!/usr/bin/env bash
# 備份本系統的 MariaDB 資料庫（只處理 quote_ 開頭，共用 MariaDB 上其他專案的庫一律不碰）
# 與 MinIO 檔案資料。輸出到 repo 底下的 backups/（不進版控，也不在 Docker volume 或
# DATA_ROOT 裡，刪 stack 或清資料目錄都不會連備份一起消失）。
#
#   ./deploy/backup.sh                            用已部署的 deploy/.env
#   ./deploy/backup.sh --environment=development  還沒用 Docker 部署過的開發機，直接讀 deploy/.env.development
#
# 這台機器沒有本系統的 MinIO（例如開發機用 npm run dev:file，檔案在 file-server/data）時，
# 只備份資料庫並提示。
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
env_path="${repo_root}/deploy/.env"
backup_root="${repo_root}/backups"

# shellcheck source=deploy/compose-lib.sh
source "${script_dir}/compose-lib.sh"

for arg in "$@"; do
  case "$arg" in
    --environment=*) env_path="${repo_root}/deploy/.env.${arg#*=}" ;;
    *) echo "[錯誤] 未知參數：$arg" >&2; exit 1 ;;
  esac
done

[[ -f "$env_path" ]] || { echo "[錯誤] 找不到 ${env_path}。" >&2; exit 1; }
cd "$repo_root"

db_root_password="$(quote_env_value DB_ROOT_PASSWORD "$env_path")"
keep_days="$(quote_env_value BACKUP_KEEP_DAYS "$env_path")"; keep_days="${keep_days:-7}"
[[ -n "$db_root_password" ]] || { echo "[錯誤] ${env_path} 缺少 DB_ROOT_PASSWORD。" >&2; exit 1; }

mariadb_container="$(quote_mariadb_container "$env_path")"
[[ -n "$mariadb_container" ]] || { echo "[錯誤] 找不到執行中的共用 MariaDB 容器。" >&2; exit 1; }

timestamp="$(date +%Y-%m-%d-%H-%M-%S)"

# ── MariaDB ─────────────────────────────────────────────────
mariadb_dir="${backup_root}/mariadb"
mkdir -p "$mariadb_dir"
# 登入失敗與「沒有資料庫」要分開處理，而且都必須讓腳本失敗：
# 排程備份若在密碼失效後仍回報成功，會一直沒有備份卻沒人發現。
if ! databases="$(quote_db_exec "$mariadb_container" "$db_root_password" mariadb -uroot -N -e "SHOW DATABASES LIKE 'quote\\_%';")"; then
  echo "[錯誤] 無法以 root 登入容器 ${mariadb_container}，請確認 ${env_path} 的 DB_ROOT_PASSWORD。" >&2
  exit 1
fi
if [[ -z "$databases" ]]; then
  echo "[錯誤] 容器 ${mariadb_container} 裡沒有任何 quote_% 資料庫，沒有東西可備份。" >&2
  exit 1
else
  while IFS= read -r db; do
    [[ -z "$db" ]] && continue
    out_file="${mariadb_dir}/${db}_${timestamp}.sql"
    # 不加 --databases：dump 裡不帶 CREATE DATABASE／USE，匯入時由指令指定目標資料庫，
    # 才能把 dump 還原成不同名稱或不同環境的資料庫。
    if ! quote_db_exec "$mariadb_container" "$db_root_password" \
        mariadb-dump -uroot --single-transaction --routines --triggers "$db" > "${out_file}.partial"; then
      rm -f "${out_file}.partial"
      echo "[錯誤] 匯出 ${db} 失敗。" >&2
      exit 1
    fi
    mv "${out_file}.partial" "$out_file"
    echo "[OK] MariaDB 備份完成：${out_file}"
  done <<< "$databases"
fi

# ── MinIO ───────────────────────────────────────────────────
minio_dir="${backup_root}/minio"
minio_volume="$(quote_minio_volume)"
if [[ -z "$minio_volume" ]]; then
  echo "[提醒] 這台機器沒有本系統的 MinIO volume，略過檔案備份。"
  echo "       開發機的上傳檔案在 file-server/data/objects，需要時另外複製。"
else
  mkdir -p "$minio_dir"
  minio_out="${minio_dir}/minio_${timestamp}.tar.gz"
  # 走臨時容器而不是主機直接 tar：MinIO 以 root 寫檔，主機一般使用者可能沒有讀取權限。
  tmp_container="$(docker create -v "${minio_volume}:/data:ro" alpine tar czf /tmp/backup.tar.gz -C /data .)"
  docker start -a "$tmp_container"
  docker cp "${tmp_container}:/tmp/backup.tar.gz" "$minio_out"
  docker rm -f "$tmp_container" >/dev/null
  echo "[OK] MinIO 備份完成：${minio_out}"
fi

# 清理放在備份都成功之後：避免舊備份刪了、新備份卻不完整。
find "$mariadb_dir" -name "*.sql" -type f -mtime "+${keep_days}" -print -delete 2>/dev/null \
  | sed 's/^/[清理] 刪除逾期備份：/' || true
if [[ -d "$minio_dir" ]]; then
  find "$minio_dir" -name "*.tar.gz" -type f -mtime "+${keep_days}" -print -delete 2>/dev/null \
    | sed 's/^/[清理] 刪除逾期備份：/' || true
fi

echo "==== 備份完成 ===="
