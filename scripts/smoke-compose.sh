#!/bin/bash
# AnyDrop compose 冒烟测试（S1-08 门禁）。
#
# 流程：构建镜像 → 以临时数据目录起容器 → /health → 建号/登录 → 建主题 →
# 上传文件 → 列表可见 → 下载逐字节一致 → 批量删除 → 重建容器（同一数据目录）
# 后旧 Cookie 仍有效（S1-05 密钥持久化）→ 备份脚本 + 恢复脚本跑通。
#
# 用法：bash scripts/smoke-compose.sh
# 要求：本机有 docker + docker compose + curl + jq + sqlite3。
# CI（.github/workflows/ci.yml 的 compose-smoke 任务）每次 PR 都会跑。
# 注意：脚本结束即删临时数据目录（.smoke/compose-data），不碰真实数据。
set -euo pipefail

cd "$(dirname "$0")/.."
BASE="http://127.0.0.1:18080"
SMOKE_DIR="$PWD/.smoke/compose"
DATA_DIR="$SMOKE_DIR/data"
export SMOKE_DATA_DIR="$DATA_DIR"
export COMPOSE_FILE="docker-compose.yml:docker-compose.smoke.yml"

fail() { echo "FAIL: $*" >&2; exit 1; }
step() { echo "== $*"; }

cleanup() {
    echo "清理冒烟容器与临时数据……"
    docker compose -f docker-compose.yml -f docker-compose.smoke.yml down --remove-orphans >/dev/null 2>&1 || true
    rm -rf "$SMOKE_DIR"
}
trap cleanup EXIT

for cmd in docker curl jq sqlite3; do
    command -v "$cmd" >/dev/null 2>&1 || fail "找不到 $cmd"
done
docker compose version >/dev/null 2>&1 || fail "找不到 docker compose（v2）"

step "构建镜像"
docker compose -f docker-compose.yml -f docker-compose.smoke.yml build

step "起容器（临时数据目录）"
mkdir -p "$DATA_DIR"
# 数据目录由宿主机用户创建，容器内以非 root（$APP_UID）运行：
# 不放开写权限，应用创建 /data/keys（S1-05 密钥持久化）时会报 Permission denied。
# 该目录是脚本结束即删的临时目录，放开权限无长期影响。
chmod 777 "$DATA_DIR"
# 让容器以宿主机用户的 UID/GID 运行（见 docker-compose.smoke.yml 的 user:）：
# 容器创建的文件属主是宿主机用户，备份（cp）和清理（rm）才不会 Permission denied。
export SMOKE_UID="$(id -u)"
export SMOKE_GID="$(id -g)"
docker compose -f docker-compose.yml -f docker-compose.smoke.yml up -d

wait_health() {
    for _ in $(seq 1 60); do
        curl -fsS -o /dev/null "$BASE/health" 2>/dev/null && return 0
        sleep 1
    done
    echo "容器未在 60 秒内健康，日志如下：" >&2
    docker compose -f docker-compose.yml -f docker-compose.smoke.yml logs --tail 50 >&2 || true
    return 1
}
wait_health || fail "容器启动失败"
echo "health: $(curl -s "$BASE/health")"

step "建号 / 登录 / 建主题"
code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/v1/auth/setup" \
    -H 'Content-Type: application/json' -d '{"nickname":"Admin","password":"Password1!","confirmPassword":"Password1!"}')
[ "$code" = "201" ] || fail "建号期望 201，实际 $code"
code=$(curl -s -c "$SMOKE_DIR/cookies.txt" -o /dev/null -w "%{http_code}" -X POST "$BASE/api/v1/auth/login" \
    -H 'Content-Type: application/json' -d '{"password":"Password1!","returnUrl":"/"}')
[ "$code" = "200" ] || fail "登录期望 200，实际 $code"
TOPIC=$(curl -s -b "$SMOKE_DIR/cookies.txt" -X POST "$BASE/api/v1/topics" \
    -H 'Content-Type: application/json' -d '{"name":"smoke"}' | jq -r '.data.id')
[ -n "$TOPIC" ] && [ "$TOPIC" != "null" ] || fail "建主题失败"

step "上传 / 列表 / 下载 / 删除往返"
printf 'smoke file\nsecond line\n' > "$SMOKE_DIR/orig.txt"
ITEM_ID=$(curl -s -b "$SMOKE_DIR/cookies.txt" -X POST "$BASE/api/v1/files/" \
    -F "file=@$SMOKE_DIR/orig.txt;filename=smoke.txt" -F "topicId=$TOPIC" | jq -r '.data.id // empty')
[ -n "$ITEM_ID" ] || fail "上传失败"
LISTED=$(curl -s -b "$SMOKE_DIR/cookies.txt" "$BASE/api/v1/share-items/?count=10" | jq -r --arg id "$ITEM_ID" '.data[] | select(.id == $id) | .id')
[ "$LISTED" = "$ITEM_ID" ] || fail "列表里找不到刚上传的消息"
curl -s -b "$SMOKE_DIR/cookies.txt" -o "$SMOKE_DIR/dl.txt" "$BASE/api/v1/share-items/$ITEM_ID/file"
cmp -s "$SMOKE_DIR/dl.txt" "$SMOKE_DIR/orig.txt" || fail "下载内容与原件不一致"
code=$(curl -s -b "$SMOKE_DIR/cookies.txt" -o /dev/null -w "%{http_code}" -X DELETE "$BASE/api/v1/share-items/batch" \
    -H 'Content-Type: application/json' -d "{\"ids\":[\"$ITEM_ID\"]}")
[ "$code" = "200" ] || fail "批量删除期望 200，实际 $code"
LISTED_AFTER=$(curl -s -b "$SMOKE_DIR/cookies.txt" "$BASE/api/v1/share-items/?count=10" | jq -r --arg id "$ITEM_ID" '.data[] | select(.id == $id) | .id')
[ -z "$LISTED_AFTER" ] || fail "删除后列表里仍有该消息"
echo "上传/列表/下载/删除往返：OK"

step "重建容器，旧 Cookie 仍有效（S1-05）"
docker compose -f docker-compose.yml -f docker-compose.smoke.yml down
docker compose -f docker-compose.yml -f docker-compose.smoke.yml up -d
wait_health || fail "重建后容器启动失败"
code=$(curl -s -b "$SMOKE_DIR/cookies.txt" -o /dev/null -w "%{http_code}" "$BASE/api/v1/topics")
[ "$code" = "200" ] || fail "重建容器后旧 Cookie 期望 200，实际 $code"
echo "重建容器后登录态未失效：OK"

step "备份 + 恢复脚本跑通"
bash scripts/backup.sh --data-dir "$DATA_DIR" --out-dir "$SMOKE_DIR/backup" --keep 3
ARCHIVE="$(ls -t "$SMOKE_DIR"/backup/anydrop-backup-*.tar.gz | head -1)"
[ -n "$ARCHIVE" ] || fail "备份未产出备份包"
bash scripts/restore.sh --archive "$ARCHIVE" --target-dir "$SMOKE_DIR/restored"
RESTORED_ROWS="$(sqlite3 "$SMOKE_DIR/restored/anydrop.db" "SELECT COUNT(*) FROM ShareItems;")"
LIVE_ROWS="$(sqlite3 "$DATA_DIR/anydrop.db" "SELECT COUNT(*) FROM ShareItems;")"
[ "$RESTORED_ROWS" = "$LIVE_ROWS" ] || fail "恢复后消息数不一致：恢复 $RESTORED_ROWS，实际 $LIVE_ROWS"
echo "备份恢复：OK（消息 $RESTORED_ROWS 条）"

echo "compose 冒烟全部通过"
