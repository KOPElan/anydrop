# Quickstart: 主题会话侧边栏

**Feature**: `speckit/002-topic-session-sidebar`  
**Date**: 2026-04-19

## 前置条件

- .NET 10 SDK 已安装
- 项目可正常构建并运行（`dotnet run --project AnyDrop`）
- 已有 SQLite 数据库（首次运行自动迁移）

## 开发环境启动

```bash
# 克隆/更新仓库并切换到功能分支
git checkout speckit/002-topic-session-sidebar

# 还原依赖并运行
dotnet run --project AnyDrop
```

应用启动后访问：`http://localhost:5002`

## 数据库迁移

本功能新增 `Topics` 表并修改 `ShareItems` 表（添加 `TopicId` 列）。迁移由应用启动时自动执行，无需手动操作。

若需手动检查迁移：

```bash
cd AnyDrop
dotnet ef migrations list
# 应看到新增迁移: AddTopicAndRelations
```

## 功能验证流程

### P1 — 新建主题 + 切换历史

1. 打开 `http://localhost:5002`，左侧侧边栏应显示"暂无主题"引导提示
2. 点击侧边栏顶部"+ 新建主题"按钮，输入"工作文件"，点击确认
3. 主题出现在侧边栏，聊天区域显示空历史
4. 再新建第二个主题"个人备忘"，在该主题下发送一条消息
5. 点击"工作文件"主题 → 聊天区域切换为空历史
6. 点击"个人备忘"主题 → 聊天区域显示刚发送的消息

### P2 — 实时排序

1. 打开两个浏览器窗口（或标签页）
2. 在窗口 A 中选中"工作文件"主题并发送消息
3. 观察窗口 B 的侧边栏 → "工作文件"主题应自动上升至列表顶部，无需刷新

### P3 — 拖拽排序

1. 侧边栏有 2 个以上主题时，拖拽任意主题到不同位置
2. 刷新页面 → 主题仍维持拖拽后的顺序

## API 快速测试

```bash
# 获取所有主题
curl http://localhost:5002/api/v1/topics

# 新建主题
curl -X POST http://localhost:5002/api/v1/topics \
  -H "Content-Type: application/json" \
  -d '{"name": "测试主题"}'

# 获取主题消息（分页）
curl "http://localhost:5002/api/v1/topics/{id}/messages?limit=20"
```

## 运行单元测试

```bash
dotnet test AnyDrop.Tests.Unit --filter "Topic"
```

## 运行 E2E 测试

```bash
cd AnyDrop.Tests.E2E
dotnet test --filter "TopicSidebar"
```
