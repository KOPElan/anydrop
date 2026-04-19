# Implementation Plan: 主题会话侧边栏

**Branch**: `speckit/002-topic-session-sidebar` | **Date**: 2026-04-19 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/speckit/002-topic-session-sidebar/spec.md`

## Summary

在首页左侧侧边栏添加主题会话管理功能，用户可新建主题、切换主题查看历史消息、通过拖拽自定义排序，侧边栏按"最后消息时间"自动排序并实时更新。技术方案：新增 `Topic` EF Core 实体（含 `SortOrder` 排序权重字段），`ShareItem` 添加可空外键 `TopicId`；`ITopicService` 封装主题 CRUD 与排序逻辑；`ShareHub` 扩展 `TopicsUpdated` SignalR 广播；侧边栏 Blazor 组件接收实时更新；拖拽排序通过 SortableJS + JS Interop 实现；主题消息历史使用游标分页。

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: Blazor Server (Interactive Server), EF Core 10 + SQLite, SignalR, SortableJS (CDN), Tailwind CSS v4  
**Storage**: SQLite（通过 EF Core，新增 `Topics` 表，`ShareItems` 表新增 `TopicId` 列）  
**Testing**: xUnit + FluentAssertions + Moq（单元测试），Playwright（E2E）  
**Target Platform**: 浏览器（桌面端，linux/amd64 Docker）  
**Project Type**: Blazor Server Web App（单体）  
**Performance Goals**: 主题切换 <1s，侧边栏实时更新 <1s，50 主题列表渲染流畅  
**Constraints**: 无额外 CSS 框架（Tailwind only），无 MVC Controller，消息分页（游标分页，初始 50 条）  
**Scale/Scope**: 单用户/小群组，主题数量预计 <100，消息数量 <10000/主题

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Verify the following gates against `.specify/memory/constitution.md` (AnyDrop v2.0.0):

- [x] **I. 单体架构分离**：`ITopicService`/`TopicService` 在 `Services/` 实现；`ShareHub` 仅调用 `ITopicService`，不含业务逻辑；Razor 侧边栏组件通过 `@inject ITopicService` 使用服务
- [x] **II. 技术栈合规**：使用 .NET 10 + Blazor Server + Tailwind CSS v4 + SQLite/EF Core + SignalR；拖拽使用 SortableJS（JS 库，不是 CSS 框架，不违反规范）；无 Fluent UI 或其他 CSS 框架
- [x] **III. 命名规范**：新增方法均以 `Async` 结尾（`CreateTopicAsync`、`ReorderTopicsAsync`、`GetTopicMessagesAsync`）；接口以 `I` 开头（`ITopicService`）；PascalCase 一致
- [x] **IV. 测试覆盖**：`TopicService` 所有方法有 xUnit 单元测试；`ShareHub.SendTopicsUpdatedAsync` 有 Moq 验证；E2E 有"新建主题→发送消息→侧边栏实时排序"完整链路
- [x] **V. 安全合规**：无硬编码凭证；主题名称验证（非空 + MaxLength）在服务层边界执行；API 端点复用全局认证
- [x] **VI. 容器化**：新增 `Topics` 表数据通过已有 SQLite Volume 挂载，无额外配置
- [x] **VII. RESTful API**：`TopicEndpoints.cs` 在 `Api/` 目录，通过 `app.MapTopicEndpoints()` 注册；`/api/v1/topics` 前缀；MUST NOT 使用 MVC Controller

**结论**: 全部 7 项 PASS，无需 Complexity Tracking 记录。

## Project Structure

### Documentation (this feature)

```text
specs/speckit/002-topic-session-sidebar/
├── plan.md              # 本文件
├── spec.md              # 功能规格
├── research.md          # Phase 0 研究结论
├── data-model.md        # Phase 1 数据模型
├── quickstart.md        # Phase 1 快速上手指南
├── contracts/
│   └── topics-api.md   # API 合约文档
└── tasks.md             # Phase 2 任务列表（由 /speckit.tasks 生成）
```

### Source Code (repository root)

```text
AnyDrop/
├── Models/
│   ├── ShareItem.cs           # 现有 — 新增 TopicId (Guid?) 字段
│   ├── Topic.cs               # 新增 — Topic 实体
│   └── TopicDto.cs            # 新增 — TopicDto、CreateTopicRequest、
│                              #         UpdateTopicRequest、ReorderTopicsRequest
├── Data/
│   └── AnyDropDbContext.cs    # 现有 — 新增 Topics DbSet + EF Core 配置
├── Migrations/
│   └── *_AddTopicAndRelations.cs  # 新增 EF Core 迁移
├── Services/
│   ├── ITopicService.cs       # 新增 — 主题服务接口
│   └── TopicService.cs        # 新增 — 主题服务实现
├── Hubs/
│   └── ShareHub.cs            # 现有 — 扩展 SendTopicsUpdatedAsync 方法
├── Api/
│   └── TopicEndpoints.cs      # 新增 — Minimal API 端点（5 个路由）
├── Components/
│   ├── Layout/
│   │   └── MainLayout.razor   # 现有 — 嵌入 TopicSidebar 组件
│   └── Layout/
│       ├── TopicSidebar.razor     # 新增 — 侧边栏主题列表组件
│       └── TopicSidebar.razor.cs  # 新增 — code-behind（SignalR 订阅、拖拽逻辑）
├── wwwroot/
│   ├── app.css                # 现有 — 新增侧边栏 @layer 样式
│   └── js/
│       └── sortable-interop.js  # 新增 — SortableJS 初始化与 JS Interop 桥接
└── Program.cs                 # 现有 — 注册 ITopicService + MapTopicEndpoints()

AnyDrop.Tests.Unit/
└── Services/
    └── TopicServiceTests.cs   # 新增 — xUnit + FluentAssertions + Moq

AnyDrop.Tests.E2E/
└── TopicSidebarTests.cs       # 新增 — Playwright E2E 测试
```

**Structure Decision**: 单项目布局（Option 1 变体），与现有代码结构完全对齐。所有新增文件均遵循现有目录约定，无结构性偏差。
