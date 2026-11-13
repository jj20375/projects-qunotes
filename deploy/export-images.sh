#!/usr/bin/env bash
# 在開發機（Mac／Linux）建置映像並打包成單一 tar，供 demo／正式機離線部署。
# 目標機只需要這份 repo 加上這個 tar，不用 .NET SDK、Node，也不必連 Docker Hub、npm、NuGet。
#
#   ./deploy/export-images.sh --environment=demo   讀 deploy/.env.demo 的 IMAGE_TAG／MINIO_TAG
#   ./deploy/export-images.sh                      沒指定環境就用預設值（IMAGE_TAG=latest）
#   ./deploy/export-images.sh --skip-build         只重新打包目前已存在的映像
#
# 產出 deploy/dist/quote-platform-images-<時間>.tar.gz，放到目標機 repo 的 deploy/dist/ 後執行
#   ./deploy/deploy.sh --environment=demo
set -euo pipefail

# Apple Silicon 預設會建出 arm64 映像，載到 amd64 的 Hyper-V／VPS 會以 exec format error 啟動失敗，
# 而且訊息完全看不出是架構問題，所以強制指定並在打包前逐一驗證。
readonly TARGET_PLATFORM="linux/amd64"

environment=""
skip_build=false
for arg in "$@"; do
  case "$arg" in
    --environment=*) environment="${arg#*=}" ;;
    --skip-build) skip_build=true ;;
    *) echo "[錯誤] 未知參數：$arg" >&2; exit 1 ;;
  esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
cd "$repo_root"

# shellcheck source=deploy/compose-lib.sh
source "${script_dir}/compose-lib.sh"

# 標籤必須與目標機 deploy/.env 一致，否則 deploy.sh 會找不到映像。
image_tag="latest"
minio_tag="RELEASE.2025-09-07T16-13-09Z"
if [[ -n "$environment" ]]; then
  profile="deploy/.env.${environment}"
  [[ -f "$profile" ]] || { echo "[錯誤] 找不到 ${profile}。" >&2; exit 1; }
  image_tag="$(quote_env_value IMAGE_TAG "$profile")"; image_tag="${image_tag:-latest}"
  minio_tag="$(quote_env_value MINIO_TAG "$profile")"; minio_tag="${minio_tag:-RELEASE.2025-09-07T16-13-09Z}"
fi

built_images=(
  "quote-platform-api:${image_tag}"
  "quote-platform-file-server:${image_tag}"
  "quote-platform-web-build:${image_tag}"
)
# web 直接用官方 nginx，靜態檔由目標機的 web-build 依 APP_BASE_PATH 產生，所以這裡不含建置好的前端。
# alpine 是 backup.sh／restore.sh 打包 MinIO volume 的臨時容器，漏掉會讓目標機第一次備份就失敗。
pulled_images=("minio/minio:${minio_tag}" "alpine:latest" "nginx:1.27-alpine")

if [[ "$skip_build" == false ]]; then
  echo "[INFO] 建置應用程式映像（${TARGET_PLATFORM}）…"
  quote_build_images "$image_tag" "$TARGET_PLATFORM"

  echo "[INFO] 取得基礎映像（${TARGET_PLATFORM}）…"
  # MinIO 官方已不再公開發布映像：Docker Hub 的 minio/minio 倉庫已不存在，Quay 的舊 tag 也會回 401。
  # 所以優先使用本機已載入的 ${TARGET_PLATFORM} 版本（Apple Silicon 上 docker pull 預設只會有 arm64，不能拿來打包）。
  # docker save --platform 在該平台內容不在本機時一定會失敗，用它判斷最可靠。
  if docker save --platform "$TARGET_PLATFORM" "minio/minio:${minio_tag}" >/dev/null 2>&1; then
    echo "[INFO] minio/minio:${minio_tag} 本機已有 ${TARGET_PLATFORM} 內容，略過下載。"
  elif docker pull --platform "$TARGET_PLATFORM" "minio/minio:${minio_tag}" 2>/dev/null; then
    :
  elif docker pull --platform "$TARGET_PLATFORM" "quay.io/minio/minio:${minio_tag}" 2>/dev/null; then
    docker tag "quay.io/minio/minio:${minio_tag}" "minio/minio:${minio_tag}"
  else
    echo "[錯誤] 取不到 minio/minio:${minio_tag}（${TARGET_PLATFORM}）：Docker Hub 與 quay.io 都已不提供。" >&2
    echo "       需要先把 ${TARGET_PLATFORM} 版 MinIO 載入本機並標成 minio/minio:${minio_tag}，" >&2
    echo "       例如從已打包過它的映像包取出，做法見 deploy/DEPLOY-FLOW.md 的排錯表。" >&2
    exit 1
  fi
  docker pull --platform "$TARGET_PLATFORM" alpine:latest
  docker pull --platform "$TARGET_PLATFORM" nginx:1.27-alpine
fi

echo "[INFO] 驗證映像架構…"
# 不用 inspect 的 Architecture 欄位：containerd image store 下多平台 index 會回報空值或錯誤值。
# docker save --platform 在該平台內容不在本機時一定會明確失敗，是可信的探測方式。
probe_error="$(mktemp)"
trap 'rm -f "$probe_error"' EXIT
arch_error=0
for image in "${built_images[@]}" "${pulled_images[@]}"; do
  if docker save --platform "$TARGET_PLATFORM" "$image" >/dev/null 2>"$probe_error"; then
    echo "  [OK] $image"
  else
    echo "  [X] $image — $(cat "$probe_error")" >&2
    arch_error=1
  fi
done
if [[ "$arch_error" -ne 0 ]]; then
  echo "[錯誤] 有映像沒有 ${TARGET_PLATFORM} 內容，載到目標機會啟動失敗。請不帶 --skip-build 重新執行。" >&2
  exit 1
fi

dist_dir="${repo_root}/deploy/dist"
mkdir -p "$dist_dir"
out="${dist_dir}/quote-platform-images-$(date +%Y-%m-%d-%H%M).tar.gz"
echo "[INFO] 打包成 ${out} …"
# 這裡的 --platform 不能省，原因同上：pulled_images 在本機是多平台 index。
docker save --platform "$TARGET_PLATFORM" "${built_images[@]}" "${pulled_images[@]}" | gzip -c > "$out"

echo ""
echo "==== 匯出完成 ===="
echo "映像檔：${out}（$(du -h "$out" | cut -f1)）"
echo "交給目標機：這份 repo（不含 .git、node_modules、backups）＋這個 tar（放進 deploy/dist/）"
echo "目標機執行：./deploy/deploy.sh --environment=${environment:-<環境>}"
