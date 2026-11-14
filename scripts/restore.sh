#!/bin/bash
# AnyDrop 恢复（S1-06，DAT-12）。
#
# 恢复到一个新目录，校验通过后再切换——绝不在原目录上先 rm：
# 1. 校验整包 sha256（有 .sha256 sidecar 时）；
# 2. 解到临时目录，校验包内 manifest.sha256（每个文件逐一核对）；
# 3. sqlite3 integrity_check + foreign_key_check；
# 4. 以上都通过，才把内容拷进目标目录。
#
# 用法：
#   scripts/restore.sh --archive ./backup/anydrop-backup-<时间戳>.tar.gz --target-dir ./restored-data
#
# 切换（校验通过后，二选一）：
#   本地运行：停服务，把 Storage__DatabasePath / Storage__BasePath /
#     DataProtection__KeysPath 指到新目录，再起服务，访问 /health 确认。
#   Docker：先按上面恢复到宿主机新目录，再用它起一个临时实例验证
#     （见 README「数据备份与恢复」一节），确认无误后把内容拷回数据卷，
#     或直接改用该目录作 bind mount；旧卷在确认新实例正常前不要删。
set -euo pipefail

ARCHIVE=""
TARGET_DIR=""

while [ $# -gt 0 ]; do
    case "$1" in
        --archive) ARCHIVE="$2"; shift 2 ;;
        --target-dir) TARGET_DIR="$2"; shift 2 ;;
        -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
        *) echo "未知参数：$1（见 --help）" >&2; exit 2 ;;
    esac
done

[ -n "$ARCHIVE" ] || { echo "必须指定 --archive <备份包>" >&2; exit 2; }
[ -n "$TARGET_DIR" ] || { echo "必须指定 --target-dir <新目录>" >&2; exit 2; }
[ -f "$ARCHIVE" ] || { echo "找不到备份包：$ARCHIVE" >&2; exit 1; }
command -v sqlite3 >/dev/null 2>&1 || { echo "找不到 sqlite3 命令" >&2; exit 1; }
command -v sha256sum >/dev/null 2>&1 || { echo "找不到 sha256sum 命令" >&2; exit 1; }

# 目标目录必须是新的或空的：恢复绝不往已有数据的目录里混写。
if [ -e "$TARGET_DIR" ] && [ -n "$(ls -A "$TARGET_DIR" 2>/dev/null)" ]; then
    echo "拒绝恢复：目标目录已存在且非空：$TARGET_DIR（请换一个新目录）" >&2
    exit 1
fi
mkdir -p "$TARGET_DIR"
TARGET_DIR="$(cd "$TARGET_DIR" && pwd)"

# 1. 整包校验和
if [ -f "$ARCHIVE.sha256" ]; then
    echo "校验整包 sha256……"
    (cd "$(dirname "$ARCHIVE")" && sha256sum -c "$(basename "$ARCHIVE").sha256")
else
    echo "提示：无 .sha256 sidecar，跳过整包校验（建议用 backup.sh 产出的成对文件）" >&2
fi

# 2. 解到临时目录，逐文件校验清单
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
echo "解包到临时目录并校验清单……"
tar xzf "$ARCHIVE" -C "$STAGE"
[ -f "$STAGE/manifest.sha256" ] || { echo "备份包缺少 manifest.sha256，拒绝恢复" >&2; exit 1; }
(cd "$STAGE" && sha256sum -c manifest.sha256)

# 3. 数据库完整性
echo "校验数据库完整性……"
[ -f "$STAGE/anydrop.db" ] || { echo "备份包缺少 anydrop.db，拒绝恢复" >&2; exit 1; }
INTEGRITY="$(sqlite3 "$STAGE/anydrop.db" "PRAGMA integrity_check;")"
[ "$INTEGRITY" = "ok" ] || { echo "数据库完整性校验失败：$INTEGRITY" >&2; exit 1; }
FK="$(sqlite3 "$STAGE/anydrop.db" "PRAGMA foreign_key_check;" | head -5)"
[ -z "$FK" ] || { echo "外键校验失败：$FK" >&2; exit 1; }
ROW_COUNT="$(sqlite3 "$STAGE/anydrop.db" "SELECT COUNT(*) FROM ShareItems;")"

# 4. 备份时就缺失的文件（恢复后会在设置页「存储健康」里看到对应修复记录）
if [ -s "$STAGE/missing.txt" ]; then
    echo "警告：本次备份在备份时就有 $(wc -l < "$STAGE/missing.txt" | tr -d ' ') 个文件缺失，恢复后请在设置页「存储健康」处理：" >&2
    sed 's/^/  缺失：/' "$STAGE/missing.txt" >&2
fi

# 5. 校验全部通过，才拷进目标目录（只保留权限与时间戳，不保留属主）
cp -r --preserve=mode,timestamps "$STAGE/anydrop.db" "$STAGE/files" "$STAGE/keys" "$TARGET_DIR/"
trap - EXIT
rm -rf "$STAGE"

echo "恢复完成：$TARGET_DIR（消息 $ROW_COUNT 条）"
echo "下一步：停掉旧服务，把服务指向新目录再启动，然后访问 /health 并做一次对账确认（见 README「数据备份与恢复」）。"
