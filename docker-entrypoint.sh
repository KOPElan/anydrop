#!/bin/sh
set -e

# 如果以 root 身份运行（docker-compose.root.yml），直接启动应用
if [ "$(id -u)" = "0" ]; then
    echo "Running as root user..."
    exec dotnet AnyDrop.dll
fi

# 以 anydrop 用户运行时，检查并尝试修复权限
echo "Running as anydrop user..."
echo "Checking /data directory permissions..."

# 检查是否能写入 /data 目录
if [ ! -w "/data" ]; then
    echo "WARNING: /data directory is not writable by current user"
    echo "Please ensure the volume is mounted with correct permissions, or use docker-compose.root.yml"
    echo "Current user: $(id)"
    echo "/data permissions: $(ls -ld /data)"
fi

# 尝试创建必要的子目录（如果有权限）
mkdir -p /data/files 2>/dev/null || true
mkdir -p /data/keys 2>/dev/null || true

echo "Starting AnyDrop..."
exec dotnet AnyDrop.dll
