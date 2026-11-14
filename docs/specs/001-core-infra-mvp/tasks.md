---

description: "Task list for Feature 001-core-infra-mvp: 核心基础设施与最小 MVP"
---

# Tasks: 核心基础设施与最小 MVP

## 冻结核查（S3-05，2026-11-15）

本次按当前代码逐项核对：**27 项已完成、6 项以演变形式完成、2 项未完成**
（T004、T023）。清单自此冻结为历史记录，不再作为跟踪方法；
今后跟踪用审计 ID + 修复任务 ID（S0–S3）。冻结常量与跟踪方法见
`docs/spec-freeze.md`。每项行尾的 `S3-05` 注释为本次核查结论
（done = 按规格完成；evolved = 功能已实现但形式与规格不同；
partial = 部分完成；not-done = 未完成）。


**Input**: Design documents from `/specs/001-core-infra-mvp/` and `/specs/main/` (plan.md, spec.md, data-model.md, contracts/)

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 [P] Create feature folder `AnyDrop/Api`, `AnyDrop/Models`, `AnyDrop/Services`, `AnyDrop/Hubs`, `AnyDrop/Data` per plan.md <!-- S3-05 [done]: 目录均已存在 -->
- [x] T002 [P] Update `AnyDrop.csproj` to add dependencies: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore` (or Scalar OpenAPI package) and FluentUI if missing <!-- S3-05 [evolved]: Swashbuckle 已接，/openapi/v1.json 可用；FluentUI 从未采用，服务端 UI 用自研 CSS/Tailwind -->
- [x] T003 [P] Add configuration keys to `appsettings.json`: `Storage:DatabasePath` default `./data/anydrop.db`, `Storage:BasePath` default `./data` (file path in AnyDrop/appsettings.json) <!-- S3-05 [done]: BasePath 默认 data/files（规格写 ./data，见 spec-freeze.md） -->
- [ ] T004 [P] Create `specs/001-core-infra-mvp/README.md` with quickstart notes from `specs/main/quickstart.md` <!-- S3-05 [not-done]: spec 目录 README 未建；内容在根 README（快速开始） -->

---

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T005 Implement `AnyDrop/Data/AnyDropDbContext.cs` (DbSet<ShareItem>) <!-- S3-05 [done]: AnyDropDbContext 已存在 -->
- [x] T006 [P] Add EF Core migrations setup and initial migration (SQLite) in `AnyDrop/` (dotnet-ef scaffold/migrations) <!-- S3-05 [done]: 基线迁移 20261114195057_Baseline -->
- [x] T007 Create `AnyDrop/Models/ShareItem.cs` per spec (Id Guid, ContentType enum, Content, FileName?, FileSize?, CreatedAt) <!-- S3-05 [done]: ShareItem 实体已存在 -->
- [x] T008 Create `AnyDrop/Models/ShareItemDto.cs` for API and SignalR broadcasting <!-- S3-05 [done]: ShareItemDto 已存在 -->
- [x] T009 Create `AnyDrop/Services/IShareService.cs` with `Task<ShareItemDto> SendTextAsync(string text)`, `Task<IEnumerable<ShareItemDto>> GetRecentAsync(int count = 50)` <!-- S3-05 [done]: IShareService 已存在 -->
- [x] T010 Create `AnyDrop/Services/ShareService.cs` implementing `IShareService` using `AnyDropDbContext` <!-- S3-05 [done]: ShareService 已存在 -->
- [x] T011 Create `AnyDrop/Services/IFileStorageService.cs` and `AnyDrop/Services/LocalFileStorageService.cs` (empty implementation, respect `Storage:BasePath`) <!-- S3-05 [done]: IFileStorageService + LocalFileStorageService 已存在 -->
- [x] T012 Create `AnyDrop/Hubs/ShareHub.cs` (SignalR Hub) that depends only on `IShareService` and broadcasts new items <!-- S3-05 [done]: ShareHub 已存在 -->
- [x] T013 Add DI registrations in `Program.cs` for DbContext, `IShareService`, `IFileStorageService`, and register SignalR hub and OpenAPI <!-- S3-05 [done]: DI 注册齐全 -->
- [x] T014 Create `AnyDrop/Api/ShareItemEndpoints.cs` to map Minimal API routes `/api/v1/share-items` (POST for send, GET for recent) and wire to `IShareService` <!-- S3-05 [done]: ShareItemEndpoints 已存在（/api/v1/share-items） -->

---

## Phase 3: User Story 1 - 双端实时文本共享 (Priority: P1) 🎯 MVP

**Goal**: 实现文本发送并通过 SignalR 实时广播，历史载入最近 50 条

**Independent Test**: 两个客户端同时打开页面，发送文本，另一端在 1 秒内看到消息；数据库插入记录

- [x] T015 [US1] Add unit tests: `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` for `SendTextAsync` and `GetRecentAsync` <!-- S3-05 [done]: ShareServiceTests 已存在 -->
- [x] T016 [US1] Add Moq-based test: `AnyDrop.Tests.Unit/Hubs/ShareHubDispatchTests.cs` to verify hub calls `Clients.All.SendAsync` when new item created (or verify via `IHubContext<ShareHub>` usage) <!-- S3-05 [evolved]: 无独立 ShareHubDispatchTests；广播断言在 ShareServiceTests（IHubContext mock） -->
- [x] T017 [US1] Implement `AnyDrop/Api/ShareItemEndpoints.cs` POST `/api/v1/share-items` to accept text payload and call `IShareService.SendTextAsync` <!-- S3-05 [done]: POST /api/v1/share-items 已存在 -->
- [x] T018 [US1] Implement SignalR server-side broadcast in `ShareService` or via `ShareHub` so that after persistence the new `ShareItemDto` is broadcast <!-- S3-05 [done]: 持久化后经 IHubContext 广播 -->
- [x] T019 [US1] Update `AnyDrop/Components/Pages/Home.razor` to connect to SignalR hub (client) and display message list; add input box + send button UI that posts to `/api/v1/share-items` <!-- S3-05 [done]: Home.razor 接 SignalR 并显示消息列表 -->
- [x] T020 [US1] Ensure client-side validation: prevent empty/whitespace messages and max length 10000 characters <!-- S3-05 [done]: 10000 字符上限在 ShareService 与 Home.razor maxlength -->
- [x] T021 [US1] Add Playwright E2E test `AnyDrop.Tests.E2E/Tests/RealtimeSharingTests.cs` to verify send→receive across two browser contexts <!-- S3-05 [evolved]: RealtimeSharingTests 不存在；E2E RealtimePushTests 覆盖双端收发 -->

---

## Phase 4: User Story 2 - 基础 UI 骨架 (Priority: P2)

**Goal**: 提供侧边栏 + 主聊天区骨架，响应式布局

- [x] T022 [US2] Modify `AnyDrop/Components/Layout/MainLayout.razor` to include `<aside>` (sidebar placeholder) and `<main>` (chat area) using Fluent UI components <!-- S3-05 [done]: MainLayout 含 <aside> 侧边栏（非 FluentUI） -->
- [ ] T023 [US2] Ensure Fluent providers are present in layout (FluentToastProvider, FluentDialogProvider, etc.) per project convention <!-- S3-05 [not-done]: 服务端从未采用 FluentUI，无 Fluent providers -->
- [x] T024 [US2] Add CSS responsive rules to `wwwroot/app.css` or component-specific CSS for mobile breakpoint (<768px) <!-- S3-05 [done]: app.css 有 @media (max-width: 768px) -->
- [x] T025 [US2] Add unit/UI test (optional) to validate layout renders without horizontal scrollbar in key viewports (Playwright visual / accessibility smoke) <!-- S3-05 [evolved]: E2E LayoutTests 覆盖侧边栏可见性与无横向滚动 -->

---

## Phase 5: User Story 3 - ShareItem 数据模型 (Priority: P2)

**Goal**: 定义 `ShareItem` 实体及 `ContentType` 枚举；保证可扩展到文件/图片

- [x] T026 [US3] Define `AnyDrop/Models/ShareContentType.cs` enum with `Text, File, Image, Video, Link` <!-- S3-05 [done]: ShareContentType 枚举已存在 -->
- [x] T027 [US3] Add EF Core configuration (Fluent API) in `AnyDrop/Data/AnyDropDbContext.cs` for `ShareItem` schema and indexes (CreatedAt index) <!-- S3-05 [done]: Fluent API + CreatedAt 索引已存在 -->
- [x] T028 [US3] Add migration and ensure `ShareItems` table created in SQLite <!-- S3-05 [done]: ShareItems 表由基线迁移创建 -->
- [x] T029 [US3] Add unit tests verifying DB mapping and that `SendTextAsync` persists `ContentType.Text` <!-- S3-05 [done]: DB 映射与 ContentType.Text 持久化有测试覆盖 -->

---

## Phase N: Polish & Cross-Cutting Concerns

- [x] T030 [P] Add OpenAPI (Swagger) registration and ensure `/openapi/v1.json` available <!-- S3-05 [done]: /openapi/v1.json（Swashbuckle，RouteTemplate openapi/{documentName}.json） -->
- [x] T031 [P] Add `Dockerfile` (multi-stage) in repo root to build and run AnyDrop (use `linux/amd64` runtime image) <!-- S3-05 [done]: 多阶段 Dockerfile 已存在 -->
- [x] T032 [P] Add GitHub Actions workflow `.github/workflows/ci.yml` to run `dotnet build` and `dotnet test` and optionally Playwright E2E (matrix: windows, ubuntu) <!-- S3-05 [done]: .github/workflows/ci.yml 跑 build + test -->
- [x] T033 [P] Add `docs/quickstart.md` using `specs/main/quickstart.md` and include run steps <!-- S3-05 [evolved]: docs/quickstart.md 不存在；根 README 快速开始覆盖 -->
- [x] T034 [P] Add security checklist: validate file uploads, do not hardcode secrets, use `Storage:DatabasePath` from env <!-- S3-05 [evolved]: 无独立安全清单文档；审计报告（52 项）+ S3-02 加固取代之 -->
- [x] T035 [P] Run `dotnet build` and `dotnet test` and fix issues until passing <!-- S3-05 [done]: 构建 0 错，测试 552/552 绿 -->

---

## Dependencies & Execution Order

- Setup (Phase 1) → Foundational (Phase 2) → User Stories (Phase 3+) → Polish
- User Story 1 (P1) should be implemented and validated first (MVP)
- Tests (unit & E2E) placed alongside implementation tasks; unit tests should be written for service layer before implementation when feasible

---

## Summary

- Total tasks: 35 (T001–T035)
- Suggested MVP scope: Complete through T021 (US1 end-to-end), then release/demo
- Next action: implement Phase 1 and Phase 2 tasks in `feat/001-core-infra-mvp` branch

---
