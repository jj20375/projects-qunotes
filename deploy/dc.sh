#!/usr/bin/env bash
# docker compose 的薄包裝：自動帶上 shared-db overlay 與 deploy/.env，其餘參數原樣往下傳。
# 日常維運一律用這支，不要直接下 docker compose。
#
#   ./deploy/dc.sh ps
#   ./deploy/dc.sh logs -f api
#   ./deploy/dc.sh up -d api
#   ./deploy/dc.sh exec -T web nginx -t
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"

# shellcheck source=deploy/compose-lib.sh
source "${script_dir}/compose-lib.sh"

if [[ ! -f "${repo_root}/deploy/.env" ]]; then
  echo "[錯誤] 找不到 deploy/.env，請先執行 ./deploy/deploy.sh --environment=<環境>。" >&2
  exit 1
fi

cd "$repo_root"
dc "$@"
