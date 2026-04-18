# Implementation Plan: 核心基础设施与最小 MVP

**Branch**: `feat/001-core-infra-mvp` | **Date**: 2026-04-18 | **Spec**: [specs/001-core-infra-mvp/spec.md](../001-core-infra-mvp/spec.md)
**Input**: Feature specification from `/specs/001-core-infra-mvp/spec.md`

## Summary

建立 AnyDrop 的完整基础设施层，使两台设备可以通过浏览器打开页面并实时共享文本内容。本次交付包括：

1. **数据层**：`ShareItem` 实体 + `AnyDropDbContext`（EF Core + SQLite）+ 初始 Migration
2. **服务层**：`IShareService` / `ShareService`（文本发送、历史查询）+ `IFileStorageService` / `LocalFileStorageService`（接口占位）
3. **通信层**：`ShareHub`（SignalR，广播新条目到所有客户端）
4. **API 层**：`ShareItemEndpoints`（Minimal API `/api/v1/share-items`）+ OpenAPI 集成
5. **UI 层**：`MainLayout`（侧边栏 + 主内容区骨架）+ `Home.razor`（聊天式界面）

技术方案：在现有 Blazor Server 单体上叠加 EF Core/SQLite、SignalR、Minimal API；通过 DI 容器共享服务实例；SignalR 负责多端广播，Blazor Server 负责当前会话状态同步。

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: Microsoft.FluentUI.AspNetCore.Components 4.13.2、Microsoft.EntityFrameworkCore.Sqlite（10.x）、Microsoft.AspNetCore.OpenApi（.NET 10 内置）、Scalar.AspNetCore（Swagger UI）
**Storage**: SQLite，路径通过 `Storage:DatabasePath` 配置键注入，默认 `./data/anydrop.db`
**Testing**: xUnit + FluentAssertions + Moq（单元测试）、Playwright（E2E）
**Target Platform**: Linux/Windows Server，Kestrel HTTP，Docker `linux/amd64`
**Project Type**: Blazor Web App（单体，Interactive Server 渲染模式）
**Performance Goals**: SignalR 消息广播 < 1 秒（局域网），历史加载 < 3 秒（50 条）
**Constraints**: 无 WASM，无 MVC Controller，无硬编码颜色/凭证，SignalR Hub 不含业务逻辑
**Scale/Scope**: 小型私有部署（< 20 并发连接），单实例，数据量初期 < 10k 条

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Verify the following gates against `.specify/memory/constitution.md` (AnyDrop v1.1.0):

- [x] **I. 单体架构分离**：服务层实现于 `Services/`，Razor 组件仅调用 DI 注入的服务，无内联业务逻辑
- [x] **II. 技术栈合规**：仅使用 .NET 10 + Blazor Server + Fluent UI + SQLite/EF Core + SignalR + Minimal API，无替代方案
- [x] **III. 命名规范**：所有新增异步方法以 `Async` 结尾，接口以 `I` 开头，PascalCase 一致
- [x] **IV. 测试覆盖**：Service 层方法有 xUnit 单元测试，SignalR 分发逻辑有 Moq 验证，E2E 链路有 Playwright 用例
- [x] **V. 安全合规**：无硬编码凭证（DB 路径通过配置注入），文件服务接口预留 MIME/大小验证扩展点
- [x] **VI. 容器化**：数据库路径和文件路径通过环境变量注入（12-Factor），Dockerfile 在下一步交付（本 Feature 为冷启动）
- [x] **VII. RESTful API**：使用 Minimal API（`app.Map*`），路径前缀 `/api/v1/`，端点提取至 `Api/ShareItemEndpoints.cs`，复用 IShareService，集成 OpenAPI

## Project Structure

### Documentation (this feature)

```text
specs/001-core-infra-mvp/
├── spec.md              # Feature specification
specs/main/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── contracts/
    └── share-items-api.md   # Phase 1 API contract
```

### Source Code

```text
AnyDrop/
├── AnyDrop.csproj               # + EF Core Sqlite, OpenApi, Scalar NuGet 包
├── Program.cs                   # 注册 EF Core、SignalR、Services、API 路由、OpenAPI
├── appsettings.json             # 新增 Storage:DatabasePath、Storage:BasePath
├── Api/
│   └── ShareItemEndpoints.cs    # Minimal API 端点扩展方法（NEW）
├── Components/
│   ├── Layout/
│   │   └── MainLayout.razor     # 修改：侧边栏 + 主内容区骨架
│   └── Pages/
│       └── Home.razor           # 修改：聊天式界面（消息列表 + 输入框 + 发送）
├── Hubs/
│   └── ShareHub.cs              # SignalR Hub（NEW）
├── Models/
│   ├── ShareItem.cs             # EF Core 实体（NEW）
│   ├── ShareItemDto.cs          # 传输对象（NEW）
│   └── ShareContentType.cs      # 枚举（NEW）
├── Services/
│   ├── IShareService.cs         # 接口（NEW）
│   ├── ShareService.cs          # 实现（NEW）
│   ├── IFileStorageService.cs   # 接口占位（NEW）
│   └── LocalFileStorageService.cs # 空实现（NEW）
└── Data/
    └── AnyDropDbContext.cs      # EF Core DbContext（NEW）

AnyDrop.Tests.Unit/              # xUnit 测试项目（NEW）
├── AnyDrop.Tests.Unit.csproj
└── Services/
    ├── ShareServiceTests.cs
    └── LocalFileStorageServiceTests.cs

AnyDrop.Tests.E2E/               # Playwright E2E 测试项目（NEW）
├── AnyDrop.Tests.E2E.csproj
└── Tests/
    └── RealtimeSharingTests.cs
```

**Structure Decision**: 在 AnyDrop 单体应用内按关注点分目录（Api/、Data/、Hubs/、Models/、Services/），符合宪法 Principle III 目录约定。测试项目独立于主项目（`AnyDrop.Tests.Unit`、`AnyDrop.Tests.E2E`），通过 `AnyDrop.slnx` 统一管理。

## Complexity Tracking

> 无宪法违规，此节留空。