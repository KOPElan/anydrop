# Tasks: 核心基础设施与最小 MVP

**Feature Branch**: `feat/001-core-infra-mvp`
**Generated**: 2026-04-18
**Spec**: specs/001-core-infra-mvp/spec.md
**Plan**: specs/main/plan.md

---

## 范围说明

本 tasks.md **不包含**以下内容（延迟至后续 Feature）：
- Minimal API 端点（`Api/` 目录、`ShareItemEndpoints.cs`）
- OpenAPI / Scalar UI 集成（`MapOpenApi()`、`AddOpenApi()`）
- `Microsoft.AspNetCore.OpenApi` / `Scalar.AspNetCore` NuGet 包
- 文件上传 UI

---

## Phase 1 — 项目初始化 & 依赖安装

**目标**: 建立开发环境基线，安装所有必要 NuGet 包，初始化测试项目。

- [ ] T001 添加 EF Core + SQLite NuGet 包至 `AnyDrop/AnyDrop.csproj`（`Microsoft.EntityFrameworkCore.Sqlite`、`Microsoft.EntityFrameworkCore.Design` 10.x）
- [ ] T002 [P] 创建 `AnyDrop.Tests.Unit/AnyDrop.Tests.Unit.csproj`（xUnit、FluentAssertions、Moq、EF Core InMemory）并添加至 `AnyDrop.slnx`
- [ ] T003 [P] 创建 `AnyDrop.Tests.E2E/AnyDrop.Tests.E2E.csproj`（Microsoft.Playwright、xUnit）并添加至 `AnyDrop.slnx`
- [ ] T004 更新 `AnyDrop/appsettings.json`，添加 `Storage:DatabasePath`（`"./data/anydrop.db"`）和 `Storage:BasePath`（`"./data/files"`）配置键

---

## Phase 2 — 基础设施层（Foundational）

**目标**: 建立数据层与服务接口，所有后续任务均依赖本阶段完成。

- [ ] T005 创建 `AnyDrop/Models/ShareContentType.cs`（枚举：Text=0、File=1、Image=2、Video=3、Link=4，显式赋值）
- [ ] T006 创建 `AnyDrop/Models/ShareItem.cs`（EF Core 实体，字段：Id/ContentType/Content/FileName/FileSize/MimeType/CreatedAt，包含 `ToDto()` 辅助方法）
- [ ] T007 创建 `AnyDrop/Models/ShareItemDto.cs`（sealed record，字段与 ShareItem 公开字段对应，用于 SignalR 广播和组件绑定）
- [ ] T008 创建 `AnyDrop/Data/AnyDropDbContext.cs`（`AnyDropDbContext : DbContext`，Primary Constructor，配置 HasMaxLength/HasConversion/HasIndex）
- [ ] T009 创建 `AnyDrop/Services/IShareService.cs`（接口，方法：`SendTextAsync(string, CancellationToken)`、`GetRecentAsync(int count = 50, CancellationToken)`）
- [ ] T010 创建 `AnyDrop/Services/IFileStorageService.cs`（接口，方法：`SaveFileAsync`、`GetFileAsync`、`DeleteFileAsync`；MVP 阶段仅定义接口）
- [ ] T011 执行 `dotnet ef migrations add InitialCreate` 生成初始 Migration，验证 `Migrations/` 目录产生

---

## Phase 3 — User Story 3：ShareItem 核心数据模型（P2）

**故事目标**: 服务层可持久化 ShareItem；通过数据库验证记录字段正确。

**独立验收测试**: 调用 `SendTextAsync` 后，SQLite 数据库 ShareItems 表存在对应记录，`ContentType = 0`（Text），`Content` 为原文，`CreatedAt` 非空。

### 测试

- [ ] T012 [P] [US3] 创建 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs`：编写 `SendTextAsync_ValidContent_PersistsToDatabase` 测试（使用 EF Core InMemory Provider + Moq IHubContext）
- [ ] T013 [P] [US3] 在 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` 追加：`SendTextAsync_EmptyContent_ThrowsArgumentException` 测试
- [ ] T014 [P] [US3] 在 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` 追加：`GetRecentAsync_ReturnsAtMostCountItems_OrderedByCreatedAtDesc` 测试
- [ ] T015 [P] [US3] 创建 `AnyDrop.Tests.Unit/Services/LocalFileStorageServiceTests.cs`：验证接口存在且空实现可实例化

### 实现

- [ ] T016 [US3] 创建 `AnyDrop/Services/ShareService.cs`（`ShareService : IShareService`，构造函数注入 `AnyDropDbContext` 和 `IHubContext<ShareHub>`；`SendTextAsync` 验证内容非空、长度 ≤ 10000，持久化后广播 DTO；`GetRecentAsync` 按 CreatedAt DESC 取前 N 条）
- [ ] T017 [US3] 创建 `AnyDrop/Services/LocalFileStorageService.cs`（`LocalFileStorageService : IFileStorageService`，MVP 阶段方法体仅 `throw new NotImplementedException()`，为后续实现预留结构）

---

## Phase 4 — User Story 1：双端实时文本共享（P1）🎯 MVP

**故事目标**: 两台设备打开页面，一台发送文字，另一台 1 秒内无刷新收到内容，数据库已持久化。

**独立验收测试**: 打开两个浏览器标签，标签 A 输入文字点击发送，标签 B 无需刷新即可看到内容；数据库有对应记录。

### 测试

- [ ] T018 [P] [US1] 在 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` 追加：`SendTextAsync_ValidContent_BroadcastsToAllClients`（Moq 验证 `IHubContext.Clients.All.SendAsync("ReceiveShareItem", ...)` 被调用一次）
- [ ] T019 [US1] 创建 `AnyDrop.Tests.E2E/Tests/RealtimeSharingTests.cs`：`SendText_TwoClients_ReceiverGetsMessageWithinOneSecond`（Playwright 开两个 Page，Page A 发送文本，断言 Page B 在 1 秒内看到该文本）

### 实现

- [ ] T020 [US1] 创建 `AnyDrop/Hubs/ShareHub.cs`（`ShareHub : Hub`，类体故意为空，仅作命名标识；所有业务逻辑由 ShareService 通过 IHubContext 调用）
- [ ] T021 [US1] 更新 `AnyDrop/Program.cs`：完成所有 DI 注册（`AddDbContext<AnyDropDbContext>`、`AddSignalR()`、`AddScoped<IShareService, ShareService>()`、`AddScoped<IFileStorageService, LocalFileStorageService>()`）、`MapHub<ShareHub>("/hubs/share")`、启动时 `MigrateAsync()`（读取 `Storage:DatabasePath` 配置，创建目录）
- [ ] T022 [US1] 更新 `AnyDrop/Components/Pages/Home.razor`（聊天式界面：上方消息时间线列表 `List<ShareItemDto>`，下方 `<FluentTextField>` 输入框 + `<FluentButton>` 发送按钮；组件挂载时调用 `GetRecentAsync(50)` 加载历史；注册 SignalR `HubConnection` 监听 `ReceiveShareItem` 事件更新列表；发送后输入框清空，列表滚动到最新）
- [ ] T023 [P] [US1] 创建 `AnyDrop/Components/Pages/Home.razor.css`（消息列表区域 `flex: 1; overflow-y: auto`，输入区域固定底部，保持在 360px–1920px 宽度范围内无水平滚动）
- [ ] T024 [P] [US1] 创建 `AnyDrop/Components/Pages/Home.razor.cs`（code-behind，提取 `_messages`、`_inputText`、`_connection` 字段，`SendMessageAsync()`、`OnInitializedAsync()` 等逻辑；实现 `IAsyncDisposable` 释放 HubConnection）

---

## Phase 5 — User Story 2：基础 UI 骨架（P2）

**故事目标**: 首页显示侧边栏 + 主聊天区，桌面端和移动端均无水平滚动，布局可扩展。

**独立验收测试**: 打开首页，左侧可见侧边栏区域，右侧为主内容区；在 360px 宽度下无水平滚动条。

### 实现

- [ ] T025 [US2] 更新 `AnyDrop/Components/Layout/MainLayout.razor`（CSS Grid 两列：侧边栏 240px + 主内容区 1fr；包含 `<FluentToastProvider />`、`<FluentDialogProvider />`；使用 Fluent Design Tokens 颜色，MUST NOT 使用硬编码颜色值）
- [ ] T026 [P] [US2] 创建或更新 `AnyDrop/Components/Layout/MainLayout.razor.css`（`.app-shell: display:grid; grid-template-columns: 240px 1fr; height:100vh`；`.sidebar: border-right; background var(--neutral-layer-2)`；移动端媒体查询 `<768px` 隐藏侧边栏并主内容占满宽度）

---

## Phase 6 — 收尾与质量验证

**目标**: 确保构建零警告、测试全部通过、代码风格合规。

- [ ] T027 运行 `dotnet build AnyDrop.slnx` 验证零警告、零错误；修复所有 CS 警告
- [ ] T028 [P] 运行 `dotnet test AnyDrop.Tests.Unit` 验证所有单元测试通过（SC-003：所有公开方法 100% 覆盖）
- [ ] T029 [P] 运行 `dotnet test AnyDrop.Tests.E2E`（需本地 `dotnet run` 服务已启动）验证 E2E 链路测试通过（SC-004）
- [ ] T030 [P] 检查 `Program.cs`：确认无 `Api/`、无 `MapOpenApi()`、无 Scalar、无硬编码路径；确认 `appsettings.json` 的 `Storage:DatabasePath` 已正确配置
- [ ] T031 验证布局响应式：使用 Chrome DevTools 模拟 360px 宽度，确认首页无水平滚动（SC-005）

---

## 依赖关系图

```
T001 → T004 → T005 → T006 → T007 → T008 → T009 → T010 → T011
                                                          ↓
                             T002 ──────────────────► T012–T015 (Unit Tests)
                             T003 ──────────────────► T019 (E2E Tests)

T011 (Migration) → T016 (ShareService) → T020 (ShareHub) → T021 (Program.cs)
                                                             ↓
T017 (FileStorageService) ─────────────────────────────► T021
                                                             ↓
                                                        T022 (Home.razor)
                                                             ↓
                                                        T025 (MainLayout) → T026
```

**用户故事完成顺序**（推荐执行顺序）：

1. Phase 1（T001–T004）→ Phase 2（T005–T011）
2. Phase 3（T012–T017，US3 数据模型）
3. Phase 4（T018–T024，US1 实时共享 🎯 MVP）
4. Phase 5（T025–T026，US2 UI 骨架）
5. Phase 6（T027–T031，质量验证）

---

## 并行执行示例

**Phase 1 并行**:
- T002（Unit 测试项目）‖ T003（E2E 测试项目）可并行创建

**Phase 2 并行**:
- T005–T010 可依序快速完成（纯文件创建，无交叉依赖）

**Phase 3 并行**:
- T012–T015（测试文件）可与 T016–T017（实现文件）并行编写（TDD 方式：先写测试，再写实现）

**Phase 4 并行**:
- T023（Home.razor.css）‖ T024（Home.razor.cs）可在 T022 确定组件结构后并行

**Phase 6 并行**:
- T028（Unit）‖ T029（E2E）‖ T030（合规检查）‖ T031（响应式验证）可并行执行

---

## 实现策略

**MVP 优先**（建议顺序）：
1. 先完成 Phase 1 + Phase 2（基础设施），确保 `dotnet build` 通过
2. 然后 Phase 3（数据模型 + 服务层），跑通单元测试
3. 再做 Phase 4（实时共享链路），这是核心价值，完成即达成 MVP（SC-001）
4. 最后补全 Phase 5（UI 骨架完善）和 Phase 6（质量验收）

**MVP 最小交付范围**（Phase 1 + 2 + 3 + 4）：
- 两台设备可以打开页面、发送文字、实时接收 → SC-001 达成

---

## 任务统计

| 阶段 | 任务数 | 并行任务 |
|------|--------|---------|
| Phase 1（初始化） | 4 | T002‖T003 |
| Phase 2（基础设施） | 7 | — |
| Phase 3（US3 数据模型） | 6 | T012–T015 可并行 |
| Phase 4（US1 实时共享） | 7 | T023‖T024 |
| Phase 5（US2 UI 骨架） | 2 | T026 |
| Phase 6（收尾验证） | 5 | T028‖T029‖T030‖T031 |
| **合计** | **31** | — |