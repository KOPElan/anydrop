# Tasks: 核心基础设施与最小 MVP

**Feature**: feat/001-core-infra-mvp | **Branch**: main  
**Constitution**: v2.0.0 | **Tests**: Principle IV 强制要求（xUnit + Moq + Playwright）  
**Input**: plan.md、spec.md（3 个 User Story）、data-model.md、research.md  
**Note**: `contracts/share-items-api.md` 中的 Minimal API 端点已 **Deferred**（本版本不实现），不含相关任务

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: 可并行（不同文件，无未完成依赖）
- **[Story]**: 所属 User Story（[US1] / [US2]）；Setup / Foundational / Polish 阶段不附 Story 标签
- 描述中必须包含准确文件路径

---

## Phase 1: Setup（项目初始化）

**Purpose**: 清理旧依赖、初始化测试项目、安装 Tailwind CSS v4 CLI 构建链

- [X] T001 更新 AnyDrop/AnyDrop.csproj — 移除 Microsoft.FluentUI.AspNetCore.Components 包引用，添加 Microsoft.EntityFrameworkCore.Sqlite 和 Microsoft.EntityFrameworkCore.Design（版本 10.x）
- [X] T002 [P] 初始化 AnyDrop.Tests.Unit 测试项目 — dotnet new xunit，添加 FluentAssertions、Moq、Microsoft.EntityFrameworkCore.InMemory 包，并项目引用 AnyDrop 主项目（AnyDrop.Tests.Unit/AnyDrop.Tests.Unit.csproj）
- [X] T003 [P] 初始化 AnyDrop.Tests.E2E 测试项目 — dotnet new xunit，添加 Microsoft.Playwright 包，并项目引用 AnyDrop 主项目（AnyDrop.Tests.E2E/AnyDrop.Tests.E2E.csproj）
- [X] T004 配置 Tailwind CSS v4 构建链 — 仓库根执行 npm init -y 并 npm install -D @tailwindcss/cli，确认 node_modules/@tailwindcss/cli 存在（package.json）
- [X] T005 [P] 更新配置文件 — AnyDrop/appsettings.json 添加 Storage 配置节（DatabasePath: data/anydrop.db，BasePath: data/files）；AnyDrop/appsettings.Development.json 同步（含 EF Core SQL 日志 LogLevel: Information）

**Checkpoint**: csproj 已更新、两个测试项目已创建、Tailwind CLI 已安装 ✅

---

## Phase 2: Foundational（阻塞性前提）

**Purpose**: 所有 User Story 共同依赖的核心实体、接口、DbContext 和 Program.cs 注册  
**⚠️ CRITICAL**: 此阶段完成前，任何 User Story 均不能开始

> **US3 说明**：T006–T009（实体 + DbContext）同时交付 User Story 3「ShareItem 核心数据模型」的实现需求（FR-001、FR-002）；US3 接受场景（发送后 DB 记录字段正确性）通过 Phase 3 的 ShareServiceTests 验证。

- [X] T006 [P] 创建 ShareContentType 枚举（AnyDrop/Models/ShareContentType.cs）— 显式赋值：Text=0、File=1、Image=2、Video=3、Link=4
- [X] T007 [P] 创建 ShareItem 实体（AnyDrop/Models/ShareItem.cs）— 字段：Id(Guid/NewGuid)、ContentType、Content(string.Empty)、FileName?(string?)、FileSize?(long?)、MimeType?(string?)、CreatedAt(DateTimeOffset.UtcNow)；含 ToDto() 方法
- [X] T008 [P] 创建 ShareItemDto 传输对象（AnyDrop/Models/ShareItemDto.cs）— sealed record，参数列表与 ShareItem 公开字段一一对应，不含导航属性
- [X] T009 创建 AnyDropDbContext（AnyDrop/Data/AnyDropDbContext.cs）— Primary Constructor（DbContextOptions<AnyDropDbContext>），OnModelCreating 配置：HasConversion<int>()、Content.HasMaxLength(10_000)、FileName.HasMaxLength(260)、MimeType.HasMaxLength(127)、HasIndex(e => e.CreatedAt)
- [X] T010 [P] 定义 IShareService 接口（AnyDrop/Services/IShareService.cs）— Task<ShareItemDto> SendTextAsync(string content, CancellationToken ct = default)、Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default)
- [X] T011 [P] 定义 IFileStorageService 接口（AnyDrop/Services/IFileStorageService.cs）— SaveFileAsync、GetFileAsync、DeleteFileAsync（含 CancellationToken 参数）
- [X] T012 [P] 创建 ShareHub 空类（AnyDrop/Hubs/ShareHub.cs）— 仅继承 Hub，无业务逻辑，无方法
- [X] T013 [P] 更新 AnyDrop/Components/_Imports.razor — 移除 @using Microsoft.FluentUI.AspNetCore.Components（及其子命名空间）；添加 @using AnyDrop.Models 和 @using AnyDrop.Services
- [X] T014 更新 AnyDrop/Program.cs — 全量 DI 注册：AddDbContext<AnyDropDbContext>（从 IConfiguration 读取 DatabasePath）、AddSignalR()、AddScoped<IShareService, ShareService>()、AddScoped<IFileStorageService, LocalFileStorageService>()、AddRazorComponents().AddInteractiveServerComponents()；中间件：UseStatusCodePagesWithReExecute("/not-found")、MapHub<ShareHub>("/hubs/share")、MapRazorComponents<App>().AddInteractiveServerRenderMode()；启动时 MigrateAsync（CreateScope）

**Checkpoint**: 实体 / 接口 / DbContext / Program.cs 全部到位，`dotnet build` 应无错误 ✅

---

## Phase 3: User Story 1 — 双端实时文本共享（Priority: P1）🎯 MVP

**Goal**: 任意两个客户端打开页面，一方发送文本，另一方在 1 秒内（局域网）无需刷新即可看到内容，同时消息持久化到 SQLite

**Independent Test**: 打开两个浏览器标签页，在标签 A 输入文本点击发送，标签 B 应立即显示该消息；`dotnet test AnyDrop.Tests.Unit` 全部通过

### Tests for User Story 1

- [X] T015 [P] [US1] 编写 ShareServiceTests 单元测试（AnyDrop.Tests.Unit/Services/ShareServiceTests.cs）— 覆盖：SendTextAsync_ValidContent_PersistsToDatabase（验证 DB 记录 ContentType=Text、Content 正确）、SendTextAsync_ValidContent_BroadcastsViaSignalR（Moq 验证 IHubContext.Clients.All.SendAsync 被调用、dto 参数正确）、SendTextAsync_EmptyContent_ThrowsArgumentException、SendTextAsync_ContentExceeds10000Chars_ThrowsArgumentException、GetRecentAsync_ReturnsTopNOrderedByCreatedAtDesc
- [X] T016 [P] [US1] 编写 LocalFileStorageServiceTests 单元测试（AnyDrop.Tests.Unit/Services/LocalFileStorageServiceTests.cs）— 验证 SaveFileAsync / GetFileAsync / DeleteFileAsync 均抛出 NotImplementedException
- [X] T017 [US1] 编写 ShareFlowTests Playwright E2E 测试（AnyDrop.Tests.E2E/Tests/ShareFlowTests.cs）— 启动两个浏览器上下文（Browser.NewContextAsync × 2）、双方均导航到 /、上下文 A 输入文本并点击发送、断言上下文 B 页面在 2000ms 内可见该文本内容（验证跨端推送链路 SC-004）

### Implementation for User Story 1

- [X] T018 [P] [US1] 实现 LocalFileStorageService 空实现（AnyDrop/Services/LocalFileStorageService.cs）— 三个接口方法均抛出 NotImplementedException（"File storage not implemented in MVP"）
- [X] T019 [US1] 实现 ShareService（AnyDrop/Services/ShareService.cs）— 注入 AnyDropDbContext 和 IHubContext<ShareHub>；SendTextAsync：验证 content 非空非空白且长度 ≤ 10,000（否则抛 ArgumentException）→ 创建 ShareItem（ContentType=Text、服务端 CreatedAt）→ SaveChangesAsync → ToDto → hub.Clients.All.SendAsync("ReceiveShareItem", dto) → return dto；GetRecentAsync：按 CreatedAt DESC 取 count 条，Select(x => x.ToDto()).ToListAsync()
- [X] T020 [US1] 创建 EF Core Migration InitialCreate — 在仓库根执行 `dotnet ef migrations add InitialCreate -p AnyDrop -s AnyDrop`，确认 AnyDrop/Migrations/ 目录及迁移文件已生成
- [X] T021 [US1] 实现 Home.razor.cs code-behind（AnyDrop/Components/Pages/Home.razor.cs）— 实现 IAsyncDisposable；@inject IShareService 和 NavigationManager；OnInitializedAsync 加载历史消息（GetRecentAsync）；在 OnAfterRenderAsync(firstRender) 建立 HubConnection（连接 /hubs/share）、注册 On("ReceiveShareItem", (ShareItemDto dto) => { _messages.Add(dto); InvokeAsync(StateHasChanged); })；DisposeAsync 停止并释放 HubConnection
- [X] T022 [US1] 实现 Home.razor UI（AnyDrop/Components/Pages/Home.razor）— @page "/"、消息时间线列表（@foreach，显示 CreatedAt 格式化时间 + Content）、文本 textarea（@bind）、发送按钮（调用 SendAsync → 清空输入框 → 滚动至底部）；禁止内联 style；所有样式使用 Tailwind CSS 工具类；客户端侧验证（空内容禁止提交、超长提示）

**Checkpoint**: 两标签页实时文本共享链路可用；`dotnet test AnyDrop.Tests.Unit` 全通过 ✅

---

## Phase 4: User Story 2 — 基础 UI 骨架（侧边栏 + 主聊天区域）（Priority: P2）

**Goal**: MainLayout 提供完整两列 CSS Grid 骨架（16rem 侧边栏 + 1fr 主内容），桌面端（≥768px）和移动端（<768px）均正确渲染无水平滚动条

**Independent Test**: 访问首页，DevTools 确认 aside.sidebar（左）与 main（右）并排（≥768px）；缩窄至 375px 时侧边栏隐藏、主区域全宽

### Tests for User Story 2

- [X] T023 [P] [US2] 编写 LayoutTests Playwright E2E 响应式测试（AnyDrop.Tests.E2E/Tests/LayoutTests.cs）— 三个断言：①桌面端（1280×800）aside 元素 IsVisible()、②移动端（375×667）aside 元素 Not.IsVisible()、③document.body.scrollWidth ≤ window.innerWidth（无水平滚动条）

### Implementation for User Story 2

- [X] T024 [P] [US2] 配置 AnyDrop/wwwroot/app.css 完整内容 — @import "tailwindcss"；@theme 定义 --color-brand（#6366f1）、--color-brand-hover（#4f46e5）、--color-surface（#f9fafb）、--color-sidebar（#f3f4f6）；@layer base（html body h-full antialiased bg-[--color-surface]）；@layer components（.btn-primary、.app-shell 使用 grid h-screen grid-template-columns: 16rem 1fr、.sidebar、移动端 @media max-width:768px 规则隐藏 .sidebar 并设 .app-shell 为 1fr）
- [X] T025 [P] [US2] 构建 tailwind.css — 执行 npx @tailwindcss/cli -i ./AnyDrop/wwwroot/app.css -o ./AnyDrop/wwwroot/tailwind.css，确认文件生成且大小 > 0（AnyDrop/wwwroot/tailwind.css）
- [X] T026 [P] [US2] 更新 AnyDrop/Components/App.razor — 在 <head> 中添加 <link rel="stylesheet" href="tailwind.css" />；移除所有 FluentUI 组件标签（如 <FluentDesignTheme>、<FluentToastProvider> 等）
- [X] T027 [US2] 实现 AnyDrop/Components/Layout/MainLayout.razor — 外层 <div class="app-shell">；<aside class="sidebar bg-[--color-sidebar] p-4">（静态文字占位"AnyDrop"）；<main class="flex flex-col overflow-hidden">（@Body）；所有类名使用 Tailwind 工具类，禁止硬编码颜色值
- [X] T028 [US2] 更新 AnyDrop/Components/Layout/MainLayout.razor.css — 可为空文件或仅补充 Tailwind 无法表达的样式（CSS Grid 布局已在 app.css @layer components 中定义，此文件作为预留扩展点）

**Checkpoint**: UI 骨架完整，LayoutTests E2E 通过，移动端响应式正确 ✅

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: 构建验证、全量测试、端到端完整流程确认、安全合规复查

- [X] T029 [P] 运行 dotnet build AnyDrop.slnx — 确认零警告、零错误（重点检查 FluentUI 残留引用已清除）
- [X] T030 [P] 运行 dotnet test — 确认 AnyDrop.Tests.Unit 和 AnyDrop.Tests.E2E 全部通过（SC-003、SC-004）
- [X] T031 按 specs/main/quickstart.md 步骤 1–7 验证完整本地开发流程 — Tailwind CLI 构建正常、dotnet run 启动正常、两标签页发送 / 接收文本测试通过（SC-001、SC-002）
- [X] T032 [P] 安全合规复查 — 确认 appsettings.json 无硬编码路径以外的敏感信息；数据库路径 / 文件路径通过 IConfiguration 读取（Constitution V）；无任何 FluentUI 残留 using 或组件标签（Constitution II）

**Checkpoint**: SC-001 至 SC-006 全部满足 ✅

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: 无依赖，立即开始
- **Foundational (Phase 2)**: 依赖 Phase 1 完成 → 阻塞所有 User Story
- **User Story 1 (Phase 3)**: 依赖 Phase 2 完成
- **User Story 2 (Phase 4)**: 依赖 Phase 2 完成（可与 US1 并行，若有多人协作）
- **Polish (Phase 5)**: 依赖 US1（Phase 3）+ US2（Phase 4）全部完成

### User Story 依赖关系

| User Story | 前置依赖 | 说明 |
|---|---|---|
| US1（双端文本共享，P1）| Phase 2 完成 | 独立可测试 |
| US2（UI 骨架，P2）| Phase 2 完成 | 独立可测试；完成后 Home.razor 集成在更完整的布局中 |
| US3（数据模型，P2）| ✅ 已在 Phase 2 交付（T006–T009）| 验证通过 US1 ShareServiceTests（T015）完成 |

### Parallel Opportunities Per Story

**Phase 1**：T002、T003、T004、T005 均可与 T001 并行（但 T002/T003 建议 T001 完成后启动，避免 csproj 引用缺失）

**User Story 1**：
```
[Phase 2 完成]
    ├── T015 (ShareServiceTests)      ─┐
    ├── T016 (LocalFileStorageTests)  ─┤ 并行 ──► T019 (ShareService) ──► T020 (Migration) ──► T021 ──► T022
    └── T018 (LocalFileStorageService)─┘
    T017 (E2E) 可与 T018/T019 并行编写，等 T022 完成后运行
```

**User Story 2**：
```
[Phase 2 完成]
    ├── T023 (LayoutTests 编写) ──────────────────────────────────────────────── 并行
    ├── T024 (app.css) ──► T025 (tailwind.css 构建) ──► T026 (App.razor) ──► T027 ──► T028
    └── T027 (MainLayout.razor) 可与 T024 并行开始（结构无依赖）
```

### Implementation Strategy（实施策略）

**MVP 最小范围**（推荐首次交付）：Phase 1 + Phase 2 + Phase 3（US1）  
→ 完成后即可验证核心价值主张：双端实时文本共享

**完整交付**：继续 Phase 4（US2 布局骨架）→ Phase 5（收尾验证）

> **提示**：US1 中的 Home.razor（T022）使用现有 `MainLayout.razor`（已在项目中）。US2 完成后替换为完整 CSS Grid 布局，视觉更佳但不影响 US1 功能正确性——两者可独立测试、按优先级顺序交付。