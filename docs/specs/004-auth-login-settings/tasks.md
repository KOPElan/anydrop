---
description: "Task list for Feature 004-auth-login-settings: 认证、登录与设置功能"
---

# Tasks: 认证、登录与设置功能

**Input**: Design documents from `/specs/004-auth-login-settings/`
**Prerequisites**: `plan.md`、`spec.md`、`research.md`、`data-model.md`、`contracts/api-contracts.md`、`quickstart.md`

**Tests**: AnyDrop Constitution v2.0.0 要求 Service 层必须有 xUnit 单测；本特性包含鉴权主路径，需补充 Playwright E2E 登录/重定向回归。

**Organization**: 按用户故事分组，确保每个故事可独立实现与测试。

## Format: `[ID] [P?] [Story?] Description`

- `[P]`：可并行（不同文件且无前置依赖）
- `[Story]`：仅在用户故事阶段标注（`[US1]...[US5]`）
- 每条任务包含明确文件路径

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: 配置与脚手架准备

- [X] T001 [P] 在 `AnyDrop/appsettings.json` 新增 `Auth` 配置节（`JwtIssuer`、`JwtAudience`、`TokenExpiryHours`、`LoginMaxFailures`、`LoginCooldownSeconds`）
- [X] T002 [P] 在 `AnyDrop/appsettings.Development.json` 新增本地 `Auth` 配置并保留 `JwtSecret` 环境变量优先说明
- [X] T003 [P] 在 `AnyDrop/Components/_Imports.razor` 增加认证相关 using（`Microsoft.AspNetCore.Authorization`、认证 DTO 命名空间）
- [X] T004 [P] 在 `AnyDrop/Components/Pages/` 创建页面骨架文件 `Login.razor`、`Setup.razor`、`Settings.razor`
- [X] T005 [P] 在 `AnyDrop/Api/` 创建端点骨架文件 `AuthEndpoints.cs` 和 `SettingsEndpoints.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: 所有用户故事共享的模型、鉴权框架、核心服务接口与实现

**⚠️ CRITICAL**: 该阶段完成前不得开始任何用户故事实现

### 2a. 数据模型与持久化

- [X] T006 [P] 创建 `AnyDrop/Models/User.cs`（`Id`、`Nickname`、`PasswordHash`、`PasswordSalt`、`SessionVersion`、`CreatedAt`、`LastLoginAt`、`UpdatedAt`）
- [X] T007 [P] 创建 `AnyDrop/Models/SystemSettings.cs`（`Id`、`AutoFetchLinkPreview`、`UpdatedAt`）
- [X] T008 [P] 创建 `AnyDrop/Models/AuthDtos.cs`（`SetupRequest`、`LoginRequest`、`LoginResponse`、`UserProfileDto`、`UpdateNicknameRequest`、`UpdatePasswordRequest`、`SecuritySettingsDto`、`UpdateSecuritySettingsRequest`）
- [X] T009 修改 `AnyDrop/Data/AnyDropDbContext.cs`：新增 `DbSet<User>`、`DbSet<SystemSettings>`、单用户约束与 `SystemSettings` 默认数据初始化配置
- [X] T010 执行 `dotnet ef migrations add AddAuthAndSystemSettings --project AnyDrop --startup-project AnyDrop` 生成迁移文件到 `AnyDrop/Migrations/`
- [X] T011 修改 `AnyDrop/Data/DatabaseMigrationExtensions.cs`，确保新表迁移与默认系统设置初始化在启动阶段可执行

### 2b. 认证与安全基础服务

- [X] T012 [P] 创建 `AnyDrop/Services/IPasswordHasherService.cs`（哈希与校验接口）
- [X] T013 [P] 创建 `AnyDrop/Services/PasswordHasherService.cs`（PBKDF2 + Salt 实现）
- [X] T014 [P] 创建 `AnyDrop/Services/ITokenService.cs`（JWT 生成与声明构建接口）
- [X] T015 [P] 创建 `AnyDrop/Services/TokenService.cs`（包含 `sub`、`sessionVersion`、`exp` 声明）
- [X] T016 [P] 创建 `AnyDrop/Services/ILoginRateLimiter.cs`（失败计数与冷却窗口接口）
- [X] T017 [P] 创建 `AnyDrop/Services/LoginRateLimiter.cs`（基于内存缓存实现 5 次失败/60 秒冷却）
- [X] T018 [P] 创建 `AnyDrop/Services/IUserService.cs`（用户查询与资料更新接口）
- [X] T019 [P] 创建 `AnyDrop/Services/ISystemSettingsService.cs`（安全设置读取与更新接口）
- [X] T020 [P] 创建 `AnyDrop/Services/IAuthService.cs`（Setup/Login/Logout/SessionVersion 校验接口）
- [X] T021 创建 `AnyDrop/Services/UserService.cs`
- [X] T022 创建 `AnyDrop/Services/SystemSettingsService.cs`
- [X] T023 创建 `AnyDrop/Services/AuthService.cs`（整合 hasher、token、rate limiter、SessionVersion）

### 2c. 应用管道与注册

- [X] T024 修改 `AnyDrop/Program.cs`：注册所有新增服务到 DI（Scoped/Singleton）
- [X] T025 修改 `AnyDrop/Program.cs`：配置 Cookie 认证（页面）和 JWT Bearer 认证（API）
- [X] T026 修改 `AnyDrop/Program.cs`：配置授权策略与默认挑战行为（未登录跳转 `/login`）
- [X] T027 修改 `AnyDrop/Program.cs`：在 JWT `OnTokenValidated` 中校验 `sessionVersion` 与数据库当前版本一致
- [X] T028 修改 `AnyDrop/Program.cs`：注册 `MapAuthEndpoints()`、`MapSettingsEndpoints()` 并保留 `/api/v1/*` 约定

### 2d. Foundational 测试

- [X] T029 [P] 在 `AnyDrop.Tests.Unit/Services/PasswordHasherServiceTests.cs` 新增哈希/校验单测
- [X] T030 [P] 在 `AnyDrop.Tests.Unit/Services/TokenServiceTests.cs` 新增 JWT 声明与过期时间单测
- [X] T031 [P] 在 `AnyDrop.Tests.Unit/Services/LoginRateLimiterTests.cs` 新增失败计数与冷却窗口单测
- [X] T032 [P] 在 `AnyDrop.Tests.Unit/Services/UserServiceTests.cs` 新增用户读写与昵称校验单测
- [X] T033 [P] 在 `AnyDrop.Tests.Unit/Services/SystemSettingsServiceTests.cs` 新增默认值初始化和更新持久化单测
- [X] T034 [P] 在 `AnyDrop.Tests.Unit/Services/AuthServiceTests.cs` 新增 setup 并发与单用户约束单测

**Checkpoint**: 认证基础设施就绪，P1/P2 故事可启动

---

## Phase 3: User Story 1 - 首次配置向导 (Priority: P1) 🎯 MVP

**Goal**: 无用户时自动进入 `/setup`，创建唯一用户并自动登录

**Independent Test**: 全新库访问任意页自动跳转 `/setup`，提交后进入首页

### Tests for US1

- [X] T035 [P] [US1] 在 `AnyDrop.Tests.Unit/Services/AuthServiceTests.cs` 新增 `SetupAsync_WhenNoUser_CreatesSingleUserAndReturnsToken` 测试
- [X] T036 [P] [US1] 在 `AnyDrop.Tests.Unit/Api/AuthEndpointsTests.cs` 新增 `POST /api/v1/auth/setup` 成功与 409 场景测试

### Implementation for US1

- [X] T037 [US1] 在 `AnyDrop/Api/AuthEndpoints.cs` 实现 `GET /api/v1/auth/setup-status`
- [X] T038 [US1] 在 `AnyDrop/Api/AuthEndpoints.cs` 实现 `POST /api/v1/auth/setup`
- [X] T039 [US1] 在 `AnyDrop/Components/Pages/Setup.razor` 实现向导表单（昵称、密码、确认密码）
- [X] T040 [US1] 在 `AnyDrop/Components/Pages/Setup.razor.cs`（或 `@code`）实现提交与自动登录跳转逻辑
- [X] T041 [US1] 在 `AnyDrop/Program.cs` 增加首次初始化重定向中间件（无用户时统一引导 `/setup`）
- [X] T042 [US1] 在 `AnyDrop/Components/Routes.razor` 配置 `/setup` 路由访问约束（已有用户时禁止再次进入）

**Checkpoint**: US1 可独立验证完成

---

## Phase 4: User Story 2 - 登录与登出 (Priority: P1) 🎯 MVP

**Goal**: 提供登录页、统一错误提示、登出后凭证即时失效

**Independent Test**: 正确密码登录成功，错误密码提示，登出后不可访问受保护资源

### Tests for US2

- [X] T043 [P] [US2] 在 `AnyDrop.Tests.Unit/Services/AuthServiceTests.cs` 新增 `LoginAsync` 成功/失败/冷却测试
- [X] T044 [P] [US2] 在 `AnyDrop.Tests.Unit/Api/AuthEndpointsTests.cs` 新增 `POST /api/v1/auth/login` 与 `POST /api/v1/auth/logout` 测试

### Implementation for US2

- [X] T045 [US2] 在 `AnyDrop/Api/AuthEndpoints.cs` 实现 `POST /api/v1/auth/login`
- [X] T046 [US2] 在 `AnyDrop/Api/AuthEndpoints.cs` 实现 `POST /api/v1/auth/logout`
- [X] T047 [US2] 在 `AnyDrop/Api/AuthEndpoints.cs` 实现 `GET /api/v1/auth/me`
- [X] T048 [US2] 在 `AnyDrop/Components/Pages/Login.razor` 实现登录表单与统一错误提示
- [X] T049 [US2] 在 `AnyDrop/Components/Pages/Login.razor.cs`（或 `@code`）实现 `returnUrl` 回跳与已登录重定向
- [X] T050 [US2] 在 `AnyDrop/Components/Layout/MainLayout.razor`（或导航组件）添加登出按钮并调用登出端点
- [X] T051 [US2] 在 `AnyDrop/Services/AuthService.cs` 实现登出递增 `SessionVersion` 与 Cookie/JWT 失效联动

**Checkpoint**: US2 可独立验证完成

---

## Phase 5: User Story 3 - 访问控制与页面/API 保护 (Priority: P1) 🎯 MVP

**Goal**: 未登录用户无法访问主页/共享内容；API 无 token 返回 401

**Independent Test**: 未登录访问 `/` 自动跳转 `/login`；无 token 访问受保护 API 返回 401

### Tests for US3

- [X] T052 [P] [US3] 在 `AnyDrop.Tests.Unit/Api/AuthEndpointsTests.cs` 新增受保护端点 401 行为测试
- [X] T053 [P] [US3] 在 `AnyDrop.Tests.E2E/Tests/AuthFlowTests.cs` 新增未登录访问主页自动跳转登录测试

### Implementation for US3

- [X] T054 [US3] 在 `AnyDrop/Components/Pages/Home.razor` 添加授权标记（`[Authorize]` 或等效路由鉴权）
- [X] T055 [US3] 在 `AnyDrop/Components/Pages/NotFound.razor`（若存在）处理未登录可见性与跳转一致性
- [X] T056 [US3] 在 `AnyDrop/Api/TopicEndpoints.cs` 对现有受保护端点加授权要求
- [X] T057 [US3] 在 `AnyDrop/Api/ShareItemEndpoints.cs` 对现有受保护端点加授权要求
- [X] T058 [US3] 在 `AnyDrop/Program.cs` 调整中间件顺序（`UseAuthentication` → `UseAuthorization` → 路由）
- [X] T059 [US3] 在 `AnyDrop/Program.cs` 配置登录页 `returnUrl` 参数透传，保障登录后回跳

**Checkpoint**: US3 可独立验证完成

---

## Phase 6: User Story 4 - 设置页：昵称与密码修改 (Priority: P2)

**Goal**: 已登录用户可修改昵称与密码，密码修改后旧密码失效

**Independent Test**: 修改昵称立即反映；修改密码后登出再登录需新密码

### Tests for US4

- [X] T060 [P] [US4] 在 `AnyDrop.Tests.Unit/Services/UserServiceTests.cs` 新增昵称更新规则测试
- [X] T061 [P] [US4] 在 `AnyDrop.Tests.Unit/Services/AuthServiceTests.cs` 新增密码修改与 `SessionVersion++` 测试
- [X] T062 [P] [US4] 在 `AnyDrop.Tests.Unit/Api/AuthEndpointsTests.cs` 新增 `PUT /api/v1/settings/profile` 与 `PUT /api/v1/settings/password` 测试

### Implementation for US4

- [X] T063 [US4] 在 `AnyDrop/Api/SettingsEndpoints.cs` 实现 `PUT /api/v1/settings/profile`
- [X] T064 [US4] 在 `AnyDrop/Api/SettingsEndpoints.cs` 实现 `PUT /api/v1/settings/password`
- [X] T065 [US4] 在 `AnyDrop/Components/Pages/Settings.razor` 实现“账号设置”分区（昵称、当前密码、新密码、确认密码）
- [X] T066 [US4] 在 `AnyDrop/Components/Pages/Settings.razor.cs`（或 `@code`）实现保存逻辑与错误提示绑定
- [X] T067 [US4] 在 `AnyDrop/Components/Layout/MainLayout.razor` 绑定展示昵称（更新后实时刷新）

**Checkpoint**: US4 可独立验证完成

---

## Phase 7: User Story 5 - 设置页：安全开关 (Priority: P2)

**Goal**: 可开关 `AutoFetchLinkPreview`，并立即影响链接预览行为

**Independent Test**: 关闭后发送 URL 不抓取预览；开启后恢复抓取

### Tests for US5

- [X] T068 [P] [US5] 在 `AnyDrop.Tests.Unit/Services/SystemSettingsServiceTests.cs` 新增 `AutoFetchLinkPreview` 更新与读取测试
- [X] T069 [P] [US5] 在 `AnyDrop.Tests.Unit/Services/ShareServiceTests.cs` 新增开关关闭时不触发链接预览任务测试
- [X] T070 [P] [US5] 在 `AnyDrop.Tests.Unit/Api/AuthEndpointsTests.cs` 新增 `GET/PUT /api/v1/settings/security` 测试

### Implementation for US5

- [X] T071 [US5] 在 `AnyDrop/Api/SettingsEndpoints.cs` 实现 `GET /api/v1/settings/security`
- [X] T072 [US5] 在 `AnyDrop/Api/SettingsEndpoints.cs` 实现 `PUT /api/v1/settings/security`
- [X] T073 [US5] 在 `AnyDrop/Components/Pages/Settings.razor` 实现“安全设置”分区（`AutoFetchLinkPreview` 开关）
- [X] T074 [US5] 在 `AnyDrop/Services/ShareService.cs` 链接预览调度前接入 `ISystemSettingsService` 开关判定
- [X] T075 [US5] 在 `AnyDrop/Services/LinkPreviewService.cs`（或调用入口）补充开关关闭时短路逻辑

**Checkpoint**: US5 可独立验证完成

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: 收尾、回归、文档一致性与质量闸门

- [X] T076 [P] 在 `AnyDrop.Tests.E2E/Tests/AuthFlowTests.cs` 增加完整首配→登录→登出→401 回归用例
- [X] T077 [P] 在 `AnyDrop.Tests.E2E/Tests/AuthFlowTests.cs` 增加密码修改后旧 token 失效用例
- [X] T078 [P] 在 `AnyDrop.Tests.Unit/Api/ShareItemEndpointsTests.cs` 补充鉴权后回归（不破坏既有分享功能）
- [X] T079 在 `AnyDrop/appsettings.json` 与 `AnyDrop/appsettings.Development.json` 统一补充 `Auth` 配置注释与默认值说明
- [X] T080 [P] 在 `AnyDrop/Program.cs` 与 `AnyDrop/Api/*.cs` 进行安全头与响应一致性复查（401/409 envelope 统一）
- [X] T081 [P] 运行 `dotnet build` 并修复编译告警（涉及新增认证代码）
- [X] T082 [P] 运行 `dotnet test AnyDrop.Tests.Unit` 并修复失败测试
- [X] T083 [P] 运行 `dotnet test AnyDrop.Tests.E2E` 并修复失败测试
- [X] T084 [P] 按 `specs/004-auth-login-settings/quickstart.md` 执行人工验收清单并记录结果

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: 无依赖
- **Phase 2 (Foundational)**: 依赖 Phase 1，阻塞全部用户故事
- **Phase 3-7 (User Stories)**: 全部依赖 Phase 2
- **Phase 8 (Polish)**: 依赖所有目标用户故事完成

### User Story Dependencies

- **US1 (P1)**: 仅依赖 Foundational
- **US2 (P1)**: 依赖 US1 已创建用户 + Foundational
- **US3 (P1)**: 依赖 US2 登录流程 + Foundational
- **US4 (P2)**: 依赖 US2（已登录）
- **US5 (P2)**: 依赖 US4 设置页基础 + 003 链接预览入口

### Within Each User Story

- 先写测试任务，再实现服务与端点，再接入页面
- 模型/DTO 先于服务，服务先于 API/UI
- 故事完成后即具备独立验收能力

---

## Parallel Opportunities

- Setup 阶段的 T001-T005 可并行
- Foundational 阶段接口与服务骨架（T012-T020）可并行
- 单测任务（T029-T034、T035/T036、T043/T044、T060-T062、T068-T070）可并行
- US4 与 US5 的页面 UI 任务可并行（不同区块）

## Parallel Example: User Story 2

```bash
Task: T043 [US2] AuthService 登录/限流测试
Task: T044 [US2] AuthEndpoints 登录登出 API 测试
Task: T048 [US2] Login.razor 页面实现
```

---

## Implementation Strategy

### MVP First (P1)

1. 完成 Phase 1-2（认证基础）
2. 完成 US1（首次配置）
3. 完成 US2（登录登出）
4. 完成 US3（访问保护）

### Incremental Delivery

1. **Iteration A (MVP)**: T001-T059
2. **Iteration B (Account Settings)**: T060-T067
3. **Iteration C (Security Toggle + Regression)**: T068-T084

### Validation Gate

- 每次迭代结束都执行：`dotnet build` + 单测 + 关键 E2E
- 通过后再进入下一迭代
