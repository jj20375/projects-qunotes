#!/usr/bin/env bash
# 還原 backup.sh 產生的備份（或手動匯出的 .sql）。破壞性操作：會 DROP 並重建
# quote_platform、清空 MinIO 資料目錄，預設要求輸入 yes 確認。
#
#   ./deploy/restore.sh                              還原 backups/ 下最新的 MariaDB 與 MinIO 備份
#   ./deploy/restore.sh --mariadb-file=<路徑>        指定 .sql（例如從別台機器匯出的檔案）
#   ./deploy/restore.sh --minio-file=<路徑>          指定 MinIO 備份
#   ./deploy/restore.sh --skip-minio                 只還原資料庫
#   ./deploy/restore.sh --skip-mariadb               只還原 MinIO
#   ./deploy/restore.sh --yes                        跳過確認
#
# 沒指定檔案時，backups/ 裡只有其中一種備份也可以（例如開發機只有資料庫），另一種略過並提示。
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
env_path="${repo_root}/deploy/.env"
backup_root="${repo_root}/backups"

# shellcheck source=deploy/compose-lib.sh
source "${script_dir}/compose-lib.sh"

# 本系統只有這一個資料庫；寫死名稱，避免備份檔名怪異時 DROP 到共用 MariaDB 上別的專案。
readonly DB_NAME="quote_platform"

mariadb_file=""
minio_file=""
skip_mariadb=false
skip_minio=false
assume_yes=false
for arg in "$@"; do
  case "$arg" in
    --mariadb-file=*) mariadb_file="${arg#*=}" ;;
    --minio-file=*) minio_file="${arg#*=}" ;;
    --skip-mariadb) skip_mariadb=true ;;
    --skip-minio) skip_minio=true ;;
    --yes) assume_yes=true ;;
    *) echo "[錯誤] 未知參數：$arg" >&2; exit 1 ;;
  esac
done

[[ -f "$env_path" ]] || { echo "[錯誤] 找不到 deploy/.env。" >&2; exit 1; }
cd "$repo_root"

db_root_password="$(quote_env_value DB_ROOT_PASSWORD "$env_path")"
[[ -n "$db_root_password" ]] || { echo "[錯誤] deploy/.env 缺少 DB_ROOT_PASSWORD。" >&2; exit 1; }

# 明確指定的檔案不存在一律報錯；自動尋找時找不到只略過該種備份。
if [[ -n "$mariadb_file" && ! -f "$mariadb_file" ]]; then echo "[錯誤] 找不到 ${mariadb_file}。" >&2; exit 1; fi
if [[ -n "$minio_file" && ! -f "$minio_file" ]]; then echo "[錯誤] 找不到 ${minio_file}。" >&2; exit 1; fi
if [[ "$skip_mariadb" == false && -z "$mariadb_file" ]]; then
  mariadb_file="$(ls -t "${backup_root}/mariadb/${DB_NAME}"_*.sql 2>/dev/null | head -1 || true)"
  [[ -n "$mariadb_file" ]] || { echo "[提醒] backups/mariadb 沒有 ${DB_NAME} 備份，略過資料庫。"; skip_mariadb=true; }
fi
if [[ "$skip_minio" == false && -z "$minio_file" ]]; then
  minio_file="$(ls -t "${backup_root}/minio"/*.tar.gz 2>/dev/null | head -1 || true)"
  [[ -n "$minio_file" ]] || { echo "[提醒] backups/minio 沒有備份，略過檔案（MinIO 維持原狀）。"; skip_minio=true; }
fi
if [[ "$skip_mariadb" == true && "$skip_minio" == true ]]; then
  echo "[錯誤] 沒有任何可還原的備份。" >&2
  exit 1
fi

echo "準備還原："
[[ "$skip_mariadb" == false ]] && echo "  MariaDB：${mariadb_file} → ${DB_NAME}"
[[ "$skip_minio" == false ]] && echo "  MinIO：  ${minio_file}"
echo "此操作會覆蓋上面列出的資料，無法復原成還原前的狀態。"
if [[ "$assume_yes" == false ]]; then
  read -r -p "確定要繼續嗎？（輸入 yes 確認）：" confirm
  [[ "$confirm" == "yes" ]] || { echo "已取消。"; exit 0; }
fi

# 還原期間先停掉會寫入的服務；不論成功或中途失敗，都把原本在跑的服務啟動回來。
running_services="$(dc ps --status running --services 2>/dev/null | grep -E '^(api|file-server)$' || true)"
restart_services() {
  if [[ -n "$running_services" ]]; then
    echo "[還原] 重新啟動：$(echo "$running_services" | tr '\n' ' ')"
    # shellcheck disable=SC2086
    dc start $running_services || true
  fi
}
trap restart_services EXIT
if [[ -n "$running_services" ]]; then
  # shellcheck disable=SC2086
  dc stop $running_services
fi

# ── MariaDB ─────────────────────────────────────────────────
if [[ "$skip_mariadb" == false ]]; then
  mariadb_container="$(quote_mariadb_container "$env_path")"
  [[ -n "$mariadb_container" ]] || { echo "[錯誤] 找不到執行中的共用 MariaDB 容器。" >&2; exit 1; }

  echo "[還原] 重建 ${DB_NAME}…"
  quote_db_exec "$mariadb_container" "$db_root_password" mariadb -uroot \
    -e "DROP DATABASE IF EXISTS \`${DB_NAME}\`; CREATE DATABASE \`${DB_NAME}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
  quote_db_exec "$mariadb_container" "$db_root_password" mariadb -uroot "$DB_NAME" < "$mariadb_file"
  # 授權不在 dump 裡；從別台機器搬來的備份，這台的 quote_app 未必有這個庫的權限，一律補授權。
  quote_db_exec "$mariadb_container" "$db_root_password" mariadb -uroot \
    -e "GRANT ALL PRIVILEGES ON \`${DB_NAME}\`.* TO 'quote_app'@'%'; FLUSH PRIVILEGES;"
  echo "[OK] ${DB_NAME} 還原完成（已授權給 quote_app）。"
fi

# ── MinIO ───────────────────────────────────────────────────
if [[ "$skip_minio" == false ]]; then
  minio_volume="$(quote_minio_volume)"
  [[ -n "$minio_volume" ]] || { echo "[錯誤] 找不到 minio-data volume，請先部署讓 compose 建立 volume。" >&2; exit 1; }
  minio_file_abs="$(cd "$(dirname "$minio_file")" && pwd)/$(basename "$minio_file")"

  echo "[還原] 停用 minio 避免寫入衝突…"
  dc stop minio || true
  tmp_container="$(docker create -v "${minio_volume}:/data" alpine \
    sh -c 'rm -rf /data/* /data/..?* /data/.[!.]* 2>/dev/null; tar xzf /tmp/restore.tar.gz -C /data')"
  docker cp "$minio_file_abs" "${tmp_container}:/tmp/restore.tar.gz"
  docker start -a "$tmp_container"
  docker rm -f "$tmp_container" >/dev/null
  dc up -d --no-build minio
  echo "[OK] MinIO 還原完成。"
fi

echo "==== 還原完成 ===="
