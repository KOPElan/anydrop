#!/bin/bash
# AnyDrop 备份（S1-06，DAT-12）。
#
# 顺序不能反——先给数据库拍快照（sqlite3 .backup），再按快照里的记录同步文件：
# 直接 tar 整个 files/ 会混入快照之后才上传的文件（备份里的数据库没有这些记录，
# 恢复后是孤儿文件），还可能打包到一半被删的文件。按快照的 Content /
# ThumbnailPath 清单打包，备份里的每个文件在备份的数据库里都有记录，两边一致。
# Data Protection 密钥目录（keys/）随备份一起打包，丢密钥等于全员掉登录。
#
# 用法（本地目录模式，本机或容器内均可）：
#   scripts/backup.sh --data-dir /data --out-dir ./backup [--keep 7]
#
# 用法（Docker 卷模式，在宿主机上跑；本脚本会在一次性容器里重跑本地模式，
# 镜像里已含本脚本与 sqlite3，见 Dockerfile）：
#   scripts/backup.sh --volume anydrop-data --out-dir ./backup [--keep 7]
#   scripts/backup.sh --volume anydrop-data --out-dir ./backup --image ghcr.io/kopelan/anydrop:latest
#
# 产物：<out-dir>/anydrop-backup-<时间戳>.tar.gz（含 anydrop.db、files/、keys/、
# manifest.sha256、files.txt、missing.txt、backup-info.txt）
# 加 <out-dir>/anydrop-backup-<时间戳>.tar.gz.sha256（整包校验和）。
# 默认保留最近 7 份，旧的自动删除。
#
# 定时备份示例（宿主机 crontab，每天 03:30）：
#   30 3 * * * /path/to/anydrop/scripts/backup.sh --volume anydrop-data --out-dir /path/to/anydrop/backup >> /var/log/anydrop-backup.log 2>&1
set -euo pipefail

DATA_DIR=""
VOLUME=""
OUT_DIR="./backup"
KEEP=7
IMAGE="ghcr.io/kopelan/anydrop:latest"
IN_CONTAINER=0

while [ $# -gt 0 ]; do
    case "$1" in
        --data-dir) DATA_DIR="$2"; shift 2 ;;
        --volume) VOLUME="$2"; shift 2 ;;
        --out-dir) OUT_DIR="$2"; shift 2 ;;
        --keep) KEEP="$2"; shift 2 ;;
        --image) IMAGE="$2"; shift 2 ;;
        --in-container) IN_CONTAINER=1; shift ;;
        -h|--help) sed -n '2,25p' "$0"; exit 0 ;;
        *) echo "未知参数：$1（见 --help）" >&2; exit 2 ;;
    esac
done

if [ -n "$VOLUME" ] && [ -n "$DATA_DIR" ]; then
    echo "--volume 与 --data-dir 只能二选一" >&2; exit 2
fi
if [ -z "$VOLUME" ] && [ -z "$DATA_DIR" ]; then
    echo "必须指定 --volume <卷名> 或 --data-dir <数据目录>" >&2; exit 2
fi
if ! [[ "$KEEP" =~ ^[0-9]+$ ]] || [ "$KEEP" -lt 1 ]; then
    echo "--keep 必须是 >= 1 的整数" >&2; exit 2
fi

# ── Docker 卷模式：在一次性容器里重跑本地模式 ──────────────────────────────
if [ -n "$VOLUME" ] && [ "$IN_CONTAINER" -eq 0 ]; then
    command -v docker >/dev/null 2>&1 || { echo "找不到 docker 命令" >&2; exit 1; }
    OUT_ABS="$(cd "$OUT_DIR" 2>/dev/null && pwd || mkdir -p "$OUT_DIR" && cd "$OUT_DIR" && pwd)"
    SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
    echo "备份卷 $VOLUME（容器内重跑）……"
    docker run --rm \
        -v "$VOLUME:/data" \
        -v "$OUT_ABS:/backup" \
        -v "$SCRIPT_DIR:/scripts:ro" \
        "$IMAGE" sh /scripts/backup.sh \
        --data-dir /data --out-dir /backup --keep "$KEEP" --in-container
    exit $?
fi

# ── 本地模式（本机或容器内）─────────────────────────────────────────────────
[ -n "$DATA_DIR" ] || DATA_DIR="/data"
command -v sqlite3 >/dev/null 2>&1 || { echo "找不到 sqlite3 命令（容器外请先安装 sqlite3；容器内镜像已自带）" >&2; exit 1; }
command -v sha256sum >/dev/null 2>&1 || { echo "找不到 sha256sum 命令" >&2; exit 1; }
[ -f "$DATA_DIR/anydrop.db" ] || { echo "找不到数据库：$DATA_DIR/anydrop.db" >&2; exit 1; }

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
TS="$(date +%Y%m%d-%H%M%S)"
STAGE="$OUT_DIR/.tmp-$TS"
mkdir -p "$STAGE"
trap 'rm -rf "$STAGE"' EXIT

# 1. 数据库快照（在线，不停服务；WAL 下 .backup 才能保证一致性，见 README）
echo "快照数据库……"
sqlite3 "$DATA_DIR/anydrop.db" ".backup $STAGE/anydrop.db"

# 2. 按快照里的记录列出文件（ContentType：1=File，2=Image，3=Video；Text/Link 的
#    Content 是文本/URL 不是路径；ThumbnailPath 是缩略图路径）
sqlite3 "$STAGE/anydrop.db" \
    "SELECT DISTINCT Content FROM ShareItems WHERE ContentType IN (1, 2, 3) AND Content IS NOT NULL AND Content <> '' UNION SELECT DISTINCT ThumbnailPath FROM ShareItems WHERE ThumbnailPath IS NOT NULL AND ThumbnailPath <> '';" \
    | sort -u > "$STAGE/files.txt"
FILE_COUNT="$(wc -l < "$STAGE/files.txt" | tr -d ' ')"

# 3. 按清单同步文件（只拷清单里的，缺失的记入 missing.txt，不让备份静默不完整）
mkdir -p "$STAGE/files"
MISSING=0
while IFS= read -r rel; do
    [ -n "$rel" ] || continue
    if [ -f "$DATA_DIR/files/$rel" ]; then
        mkdir -p "$STAGE/files/$(dirname "$rel")"
        cp --preserve=mode,timestamps "$DATA_DIR/files/$rel" "$STAGE/files/$rel"
    else
        echo "$rel" >> "$STAGE/missing.txt"
        MISSING=$((MISSING + 1))
    fi
done < "$STAGE/files.txt"
touch "$STAGE/missing.txt"
if [ "$MISSING" -gt 0 ]; then
    echo "警告：数据库引用了 $MISSING 个磁盘上不存在的文件，已记入 missing.txt（恢复后请在设置页「存储健康」处理）：" >&2
    sed 's/^/  缺失：/' "$STAGE/missing.txt" >&2
fi

# 4. 密钥目录（丢密钥等于全员掉登录；只保留权限与时间戳，不保留属主——
#    恢复环境属主可能不同，且某些文件系统不允许 chown）
if [ -d "$DATA_DIR/keys" ]; then
    cp -r --preserve=mode,timestamps "$DATA_DIR/keys" "$STAGE/keys"
    KEYS_COUNT="$(find "$STAGE/keys" -type f | wc -l | tr -d ' ')"
else
    echo "警告：未找到密钥目录 $DATA_DIR/keys（旧部署或未持久化），本次备份不含密钥" >&2
    KEYS_COUNT=0
    mkdir -p "$STAGE/keys"
fi

# 5. 清单校验和 + 备份信息
(cd "$STAGE" && find anydrop.db files keys -type f | sort | xargs sha256sum > manifest.sha256)
ROW_COUNT="$(sqlite3 "$STAGE/anydrop.db" "SELECT COUNT(*) FROM ShareItems;")"
{
    echo "anydrop-backup-$TS"
    echo "created: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "source: $DATA_DIR"
    echo "share_items: $ROW_COUNT"
    echo "files_listed: $FILE_COUNT"
    echo "files_missing: $MISSING"
    echo "keys_files: $KEYS_COUNT"
} > "$STAGE/backup-info.txt"

# 6. 打包 + 整包校验和
ARCHIVE="$OUT_DIR/anydrop-backup-$TS.tar.gz"
tar czf "$ARCHIVE" -C "$STAGE" .
sha256sum "$ARCHIVE" | sed "s|$ARCHIVE|anydrop-backup-$TS.tar.gz|" > "$ARCHIVE.sha256"
trap - EXIT
rm -rf "$STAGE"

# 7. 保留最近 KEEP 份
#    shellcheck disable=SC2012
ls -t "$OUT_DIR"/anydrop-backup-*.tar.gz 2>/dev/null | tail -n +"$((KEEP + 1))" | while IFS= read -r old; do
    echo "删除过期备份：$old"
    rm -f "$old" "$old.sha256"
done

SIZE="$(du -h "$ARCHIVE" | cut -f1)"
echo "备份完成：$ARCHIVE（$SIZE，消息 $ROW_COUNT 条，文件 $FILE_COUNT 个，缺失 $MISSING 个，密钥 $KEYS_COUNT 个）"
