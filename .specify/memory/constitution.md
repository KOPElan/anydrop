<!--
SYNC IMPACT REPORT
==================
Version Change  : (new) → 1.0.0
Modified Principles : N/A — initial constitution creation
Added Sections  : Core Principles (6), Architecture Constraints, Development Workflow, Governance
Removed Sections: N/A
Templates Updated:
  ✅ .specify/memory/constitution.md — written (this file)
  ✅ .specify/templates/plan-template.md — Constitution Check section updated
  ✅ .specify/templates/spec-template.md — Requirements section aligned
  ✅ .specify/templates/tasks-template.md — test task categories aligned
Deferred TODOs  : none
-->

# AnyDrop Constitution

## Core Principles

### I. 单体应用架构 — 服务层与 UI 层强制分离

AnyDrop 采用单体应用架构（.NET 10 + Blazor Server），整个应用在同一进程内运行。

- 服务层（`Services/`）MUST NOT 直接依赖任何 Razor 组件；Razor 组件通过 DI `@inject` 使用服务。
- 业务逻辑 MUST 实现于服务层，Razor 组件仅负责 UI 呈现与用户交互，禁止在组件中内联业务逻辑。
- 禁止使用微服务拆分；所有功能在同一进程内，通过命名空间和目录结构隔离关注点。
- SignalR Hub 属于通信协调层，不得包含业务逻辑；Hub 仅调用服务层方法。

### II. 技术栈约束（不可替换）

技术选型已固定，任何功能实现 MUST 在以下栈内完成，不得引入替代方案。

- **框架**：.NET 10 + Blazor Server（Interactive Server 渲染模式，禁止 WASM）
- **UI 组件**：Microsoft Fluent UI for Blazor（禁止使用 Bootstrap、Tailwind 或其他 CSS 框架）
- **数据库**：SQLite，通过 EF Core 操作；禁止裸 SQL 字符串拼接，MUST 使用参数化查询
- **实时通信**：SignalR Hub（服务端广播）+ Blazor ComponentBase 双向绑定（表单/状态同步）；
  不得引入第三方实时通信库
- **文件存储**：存储路径通过 `appsettings.json` 或环境变量（`Storage:BasePath`）配置，禁止硬编码

### III. 命名规范（全局强制）

- 所有 C# 代码 MUST 遵循 C# 官方 PascalCase 规范（类、方法、属性、枚举）
- 所有异步方法 MUST 以 `Async` 结尾（如 `SendMessageAsync`、`GetFilesAsync`）
- 接口 MUST 以大写 `I` 前缀命名（如 `IMessageService`、`IFileStorageService`）
- 目录/命名空间布局约定：
  - `Models/` — EF Core 实体与 DTO
  - `Services/` — 业务逻辑服务及其接口
  - `Hubs/` — SignalR Hub 类
  - `Components/Pages/` — 路由页面组件
  - `Components/Layout/` — 布局与通用 UI 组件
- 测试项目命名：`AnyDrop.Tests.Unit`、`AnyDrop.Tests.E2E`

### IV. 测试优先（NON-NEGOTIABLE）

测试是交付的一部分，不是可选项。无测试的业务代码 MUST NOT 合并至主分支。

- **后端单元测试**：使用 xUnit + FluentAssertions；所有涉及 EF Core 数据库操作和业务逻辑的方法
  MUST 有对应单元测试；使用 InMemory Provider 或 SQLite InMemory 隔离数据库状态
- **SignalR 消息分发**：使用 Moq 模拟 `IHubContext<T>`，验证服务层触发正确的 Hub 方法及参数
- **端到端测试（E2E）**：使用 Playwright；MUST 有至少一个 E2E 用例验证"发送端（桌面）→
  服务端 → 接收端（模拟移动端）"的完整跨端推送链路
- 测试命名格式：`[方法名]_[场景描述]_[期望结果]`（如 `SendMessage_WhenContentIsEmpty_ThrowsException`）

### V. 安全与隐私

- 遵循 OWASP Top 10；凭证、密钥、密码 MUST 通过环境变量注入，禁止硬编码于代码或配置文件
- 文件上传 MUST 验证 MIME 类型与文件大小上限，拒绝不合规内容
- 文件下载响应头 MUST 包含 `Content-Disposition: attachment`，防止浏览器直接执行上传内容
- Web 响应 MUST 设置必要安全头（`X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY` 等）
- 认证方案（密码会话或 OIDC Proxy）MUST 覆盖所有非公开端点；管理端点不得无鉴权暴露

### VI. 容器化优先

- 应用 MUST 提供 Dockerfile，可通过 `docker run` 单命令启动
- 持久化数据（SQLite 数据库文件、上传文件目录）MUST 通过 Docker Volume 挂载，禁止存储于容器层
- 所有外部配置（存储路径、端口、认证凭证）MUST 通过环境变量注入，支持 12-Factor 原则
- 目标平台：`linux/amd64`；推荐 Alpine 基础镜像以控制镜像体积
- `docker-compose.yml` MUST 作为本地开发与集成测试的标准启动方式

## 架构约束

- **通信契约**：SignalR Hub 负责服务端向所有已连接客户端的多播推送；
  Blazor Server 双向绑定仅用于当前会话的状态同步，不可替代多端广播
- **禁用静态状态**：禁止使用 `static` 字段或属性在组件/服务间传递状态；
  跨组件状态 MUST 使用 Scoped DI 服务或 Cascading Values
- **EF Core 迁移**：数据库结构变更 MUST 通过 EF Core Migration 管理，禁止手动修改数据库文件
- **依赖注入**：所有服务 MUST 在 `Program.cs` 中通过 `builder.Services.Add*` 注册；
  禁止 `new` 直接实例化服务类
- **代码复审要点**：PR 合并前复审者 MUST 确认服务层与 UI 层分离合规、异步命名规范、测试存在性

## 开发工作流

- 每个功能 MUST 在独立的 Feature Branch 上开发，命名格式：`feat/###-feature-name`
- PR 合并至 `main` 前 MUST 通过 CI 检查：
  - 所有单元测试（xUnit）通过且无跳过
  - Playwright E2E 测试通过
  - 代码构建（`dotnet build`）无错误、无警告
- 本地开发环境使用 `docker-compose up` 启动，确保与生产行为一致
- 数据库迁移文件 MUST 随功能代码一同提交，不得单独合并

## Governance

- 本宪法是最高工程契约，优先级高于任何功能规格文档（spec.md）和实现计划（plan.md）
- 宪法修订 MUST 遵循语义版本规则递增版本号，并在 PR 描述中说明变更理由与影响范围
- 修订后 MUST 同步检查并更新 `.specify/templates/` 下的所有模板文件
- 合规性复审应在每次重要里程碑或 Sprint 结束时执行，由项目负责人确认
- 任何与宪法冲突的实现决策 MUST 先提出宪法修订并获批准，再进行实现

**Version**: 1.0.0 | **Ratified**: 2026-04-18 | **Last Amended**: 2026-04-18
