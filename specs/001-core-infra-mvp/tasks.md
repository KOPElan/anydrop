---

description: "Task list for Feature 001-core-infra-mvp: 核心基础设施与最小 MVP"
---

# Tasks: 核心基础设施与最小 MVP

**Input**: Design documents from `/specs/001-core-infra-mvp/` and `/specs/main/` (plan.md, spec.md, data-model.md, contracts/)

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 [P] Create feature folder `AnyDrop/Api`, `AnyDrop/Models`, `AnyDrop/Services`, `AnyDrop/Hubs`, `AnyDrop/Data` per plan.md
- [ ] T002 [P] Update `AnyDrop.csproj` to add dependencies: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore` (or Scalar OpenAPI package) and FluentUI if missing
- [ ] T003 [P] Add configuration keys to `appsettings.json`: `Storage:DatabasePath` default `./data/anydrop.db`, `Storage:BasePath` default `./data` (file path in AnyDrop/appsettings.json)
- [ ] T004 [P] Create `specs/001-core-infra-mvp/README.md` with quickstart notes from `specs/main/quickstart.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

- [ ] T005 Implement `AnyDrop/Data/AnyDropDbContext.cs` (DbSet<ShareItem>)
- [ ] T006 [P] Add EF Core migrations setup and initial migration (SQLite) in `AnyDrop/` (dotnet-ef scaffold/migrations)
- [ ] T007 Create `AnyDrop/Models/ShareItem.cs` per spec (Id Guid, ContentType enum, Content, FileName?, FileSize?, CreatedAt)
- [ ] T008 Create `AnyDrop/Models/ShareItemDto.cs` for API and SignalR broadcasting
- [ ] T009 Create `AnyDrop/Services/IShareService.cs` with `Task<ShareItemDto> SendTextAsync(string text)`, `Task<IEnumerable<ShareItemDto>> GetRecentAsync(int count = 50)`
- [ ] T010 Create `AnyDrop/Services/ShareService.cs` implementing `IShareService` using `AnyDropDbContext`
- [ ] T011 Create `AnyDrop/Services/IFileStorageService.cs` and `AnyDrop/Services/LocalFileStorageService.cs` (empty implementation, respect `Storage:BasePath`)
- [ ] T012 Create `AnyDrop/Hubs/ShareHub.cs` (SignalR Hub) that depends only on `IShareService` and broadcasts new items
- [ ] T013 Add DI registrations in `Program.cs` for DbContext, `IShareService`, `IFileStorageService`, and register SignalR hub and OpenAPI
- [ ] T014 Create `AnyDrop/Api/ShareItemEndpoints.cs` to map Minimal API routes `/api/v1/share-items` (POST for send, GET for recent) and wire to `IShareService`

---

## Phase 3: User Story 1 - 双端实时文本共享 (Priority: P1) 🎯 MVP

**Goal**: 实现文本发送并通过 SignalR 实时广播，历史载入最近 50 条

**Independent Test**: 两个客户端同时打开页面，发送文本，另一端在 1 秒内看到消息；数据库插入记录

- [ ] T015 [US1] Add unit tests: `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` for `SendTextAsync` and `GetRecentAsync`
- [ ] T016 [US1] Add Moq-based test: `AnyDrop.Tests.Unit/Hubs/ShareHubDispatchTests.cs` to verify hub calls `Clients.All.SendAsync` when new item created (or verify via `IHubContext<ShareHub>` usage)
- [ ] T017 [US1] Implement `AnyDrop/Api/ShareItemEndpoints.cs` POST `/api/v1/share-items` to accept text payload and call `IShareService.SendTextAsync`
- [ ] T018 [US1] Implement SignalR server-side broadcast in `ShareService` or via `ShareHub` so that after persistence the new `ShareItemDto` is broadcast
- [ ] T019 [US1] Update `AnyDrop/Components/Pages/Home.razor` to connect to SignalR hub (client) and display message list; add input box + send button UI that posts to `/api/v1/share-items`
- [ ] T020 [US1] Ensure client-side validation: prevent empty/whitespace messages and max length 10000 characters
- [ ] T021 [US1] Add Playwright E2E test `AnyDrop.Tests.E2E/Tests/RealtimeSharingTests.cs` to verify send→receive across two browser contexts

---

## Phase 4: User Story 2 - 基础 UI 骨架 (Priority: P2)

**Goal**: 提供侧边栏 + 主聊天区骨架，响应式布局

- [ ] T022 [US2] Modify `AnyDrop/Components/Layout/MainLayout.razor` to include `<aside>` (sidebar placeholder) and `<main>` (chat area) using Fluent UI components
- [ ] T023 [US2] Ensure Fluent providers are present in layout (FluentToastProvider, FluentDialogProvider, etc.) per project convention
- [ ] T024 [US2] Add CSS responsive rules to `wwwroot/app.css` or component-specific CSS for mobile breakpoint (<768px)
- [ ] T025 [US2] Add unit/UI test (optional) to validate layout renders without horizontal scrollbar in key viewports (Playwright visual / accessibility smoke)

---

## Phase 5: User Story 3 - ShareItem 数据模型 (Priority: P2)

**Goal**: 定义 `ShareItem` 实体及 `ContentType` 枚举；保证可扩展到文件/图片

- [ ] T026 [US3] Define `AnyDrop/Models/ShareContentType.cs` enum with `Text, File, Image, Video, Link`
- [ ] T027 [US3] Add EF Core configuration (Fluent API) in `AnyDrop/Data/AnyDropDbContext.cs` for `ShareItem` schema and indexes (CreatedAt index)
- [ ] T028 [US3] Add migration and ensure `ShareItems` table created in SQLite
- [ ] T029 [US3] Add unit tests verifying DB mapping and that `SendTextAsync` persists `ContentType.Text`

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] T030 [P] Add OpenAPI (Swagger) registration and ensure `/openapi/v1.json` available
- [ ] T031 [P] Add `Dockerfile` (multi-stage) in repo root to build and run AnyDrop (use `linux/amd64` runtime image)
- [ ] T032 [P] Add GitHub Actions workflow `.github/workflows/ci.yml` to run `dotnet build` and `dotnet test` and optionally Playwright E2E (matrix: windows, ubuntu)
- [ ] T033 [P] Add `docs/quickstart.md` using `specs/main/quickstart.md` and include run steps
- [ ] T034 [P] Add security checklist: validate file uploads, do not hardcode secrets, use `Storage:DatabasePath` from env
- [ ] T035 [P] Run `dotnet build` and `dotnet test` and fix issues until passing

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
