#!/bin/bash
# Docker 部署测试脚本
# 用于验证标准模式和 Root 模式部署是否正常工作

set -e

echo "=== AnyDrop Docker 部署测试 ==="
echo

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# 测试函数
test_standard_mode() {
    echo -e "${YELLOW}测试 1: 标准模式部署（非 root 用户）${NC}"
    echo "清理现有容器和 volume..."
    docker compose down -v 2>/dev/null || true

    echo "构建并启动容器..."
    docker compose up -d --build

    echo "等待容器启动..."
    sleep 10

    echo "检查容器状态..."
    if docker ps | grep -q anydrop; then
        echo -e "${GREEN}✓ 容器启动成功${NC}"
    else
        echo -e "${RED}✗ 容器启动失败${NC}"
        docker logs anydrop
        return 1
    fi

    echo "检查容器日志..."
    if docker logs anydrop 2>&1 | grep -q "WARNING: /data directory is not writable"; then
        echo -e "${RED}✗ 发现权限警告${NC}"
        docker logs anydrop
        return 1
    else
        echo -e "${GREEN}✓ 无权限警告${NC}"
    fi

    echo "测试 HTTP 端点..."
    for i in {1..30}; do
        if curl -f -s http://localhost:8080/ > /dev/null 2>&1; then
            echo -e "${GREEN}✓ HTTP 端点响应正常${NC}"
            break
        fi
        if [ $i -eq 30 ]; then
            echo -e "${RED}✗ HTTP 端点无响应${NC}"
            docker logs anydrop
            return 1
        fi
        sleep 1
    done

    echo "测试 setup 页面..."
    if curl -s http://localhost:8080/setup | grep -q "初始化"; then
        echo -e "${GREEN}✓ Setup 页面加载正常${NC}"
    else
        echo -e "${RED}✗ Setup 页面加载失败${NC}"
        return 1
    fi

    echo "测试 blazor.web.js 资源..."
    if curl -s http://localhost:8080/_framework/blazor.web.js | head -n 1 | grep -q "function\|var\|const\|!function"; then
        echo -e "${GREEN}✓ blazor.web.js 返回正确的 JavaScript 内容${NC}"
    else
        echo -e "${RED}✗ blazor.web.js 返回内容异常${NC}"
        curl -s http://localhost:8080/_framework/blazor.web.js | head -n 10
        return 1
    fi

    echo "清理..."
    docker compose down

    echo -e "${GREEN}=== 标准模式测试通过 ===${NC}"
    echo
}

test_root_mode() {
    echo -e "${YELLOW}测试 2: Root 模式部署${NC}"
    echo "清理现有容器和 volume..."
    docker compose -f docker-compose.root.yml down -v 2>/dev/null || true

    echo "构建并启动容器..."
    docker compose -f docker-compose.root.yml up -d --build

    echo "等待容器启动..."
    sleep 10

    echo "检查容器状态..."
    if docker ps | grep -q anydrop-root; then
        echo -e "${GREEN}✓ 容器启动成功${NC}"
    else
        echo -e "${RED}✗ 容器启动失败${NC}"
        docker logs anydrop-root
        return 1
    fi

    echo "验证以 root 用户运行..."
    if docker exec anydrop-root id | grep -q "uid=0"; then
        echo -e "${GREEN}✓ 容器以 root 用户运行${NC}"
    else
        echo -e "${RED}✗ 容器未以 root 用户运行${NC}"
        return 1
    fi

    echo "测试 HTTP 端点（端口 80）..."
    for i in {1..30}; do
        if curl -f -s http://localhost:80/ > /dev/null 2>&1; then
            echo -e "${GREEN}✓ HTTP 端点响应正常${NC}"
            break
        fi
        if [ $i -eq 30 ]; then
            echo -e "${RED}✗ HTTP 端点无响应${NC}"
            docker logs anydrop-root
            return 1
        fi
        sleep 1
    done

    echo "清理..."
    docker compose -f docker-compose.root.yml down

    echo -e "${GREEN}=== Root 模式测试通过 ===${NC}"
    echo
}

# 主测试流程
main() {
    # 检查是否设置了 JWT Secret
    if [ -z "$ANYDROP_JWT_SECRET" ]; then
        echo -e "${YELLOW}警告: ANYDROP_JWT_SECRET 未设置，使用默认值${NC}"
        export ANYDROP_JWT_SECRET="test-secret-key-for-docker-deployment-testing-32chars"
    fi

    # 运行测试
    if test_standard_mode; then
        echo -e "${GREEN}标准模式测试通过${NC}"
    else
        echo -e "${RED}标准模式测试失败${NC}"
        exit 1
    fi

    if test_root_mode; then
        echo -e "${GREEN}Root 模式测试通过${NC}"
    else
        echo -e "${RED}Root 模式测试失败${NC}"
        exit 1
    fi

    echo -e "${GREEN}=== 所有测试通过 ===${NC}"
}

# 运行主测试
main
