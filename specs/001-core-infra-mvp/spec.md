# Feature Specification: 核心基础设施与最小 MVP

**Feature Branch**: `feat/001-core-infra-mvp`  
**Created**: 2026-04-18  
**Status**: Draft  
**Input**: 给项目设计基础设施层，包括核心模型（ShareItem 实体）、数据契约（EF Core 配置）、实时 Hub、文件服务，并初始化项目骨架（侧边栏 + 主聊天区域 UI），实现最小 MVP：两台设备可打开页面、发送文本内容并同步推送。

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 双端实时文本共享（Priority: P1）🎯 MVP

用户 A 在设备甲（桌面浏览器）打开 AnyDrop，输入一段文字并发送；此时用户 B 在设备乙（手机/平板浏览器）也已打开页面，可以**立即**在主聊天区域看到这条内容，无需刷新页面。

**Why this priority**：这是 AnyDrop 的核心价值主张——跨端即时共享。其余所有功能都依赖此链路通畅。

**Independent Test**：打开两个浏览器标签（或两台设备），在其中一个发送文本，另一个无需刷新即可看到内容，即视为 MVP 达成。

**Acceptance Scenarios**:

1. **Given** 两个客户端均已打开 AnyDrop 页面并建立 SignalR 连接，**When** 客户端 A 在输入框输入文字并点击发送，**Then** 文字消息在 1 秒内出现在客户端 A 和客户端 B 的聊天列表中，且数据库已持久化该条记录。
2. **Given** 系统已有若干历史消息，**When** 新客户端 C 首次打开页面，**Then** 页面加载完成后自动显示最近 50 条历史记录（最新的在下方）。
3. **Given** 客户端 A 正常发送文字，**When** 内容为空字符串或仅含空白字符，**Then** 系统拒绝发送并在 UI 给出提示，不向服务端提交任何请求。

---

### User Story 2 — 基础 UI 骨架（侧边栏 + 主聊天区域）（Priority: P2）

作为用户，我打开 AnyDrop 后看到一个清晰的界面结构：左侧是设备/频道导航侧边栏（初期可以是静态占位），右侧主区域显示共享内容的时间线（聊天流）。UI 结构需为后续功能扩展预留位置。

**Why this priority**：良好的 UI 骨架是后续所有功能开发的"画布"，P1 功能也需要依托此骨架展示。

**Independent Test**：打开首页，可以看到侧边栏与主聊天区域的布局；侧边栏存在但不需要有实际导航逻辑，主区域显示聊天输入框和消息列表即可。

**Acceptance Scenarios**:

1. **Given** 用户访问首页（`/`），**When** 页面渲染完成，**Then** 页面左侧显示侧边栏区域，右侧显示主聊天区域，整体布局在桌面端（≥768px）和移动端（<768px）均可正常呈现，无水平滚动条。
2. **Given** 用户在移动端（窄屏）访问，**When** 页面渲染完成，**Then** 布局自适应：侧边栏可折叠或隐藏，主内容区域占满宽度。

---

### User Story 3 — ShareItem 核心数据模型（Priority: P2）

系统能够持久化各种类型的共享内容（文本、文件引用、多媒体引用、网页链接），并能区分内容类型，以便未来渲染不同的 UI。

**Why this priority**：数据模型是所有功能的基础，P1 的文本发送功能也依赖此实体；但模型可以随 P1 一同交付，不需要提前单独交付。

**Independent Test**：可以通过数据库检查验证：发送一条文本消息后，数据库 ShareItems 表中存在对应记录，字段类型正确标记为 `Text`。

**Acceptance Scenarios**:

1. **Given** 系统数据库已初始化，**When** 用户发送一条文本消息，**Then** 数据库中存在一条 ShareItem 记录，其 `ContentType` 为 `Text`，`Content` 为消息原文，`CreatedAt` 为服务端时间戳。
2. **Given** ShareItem 实体已定义，**When** 将来需要添加文件类型支持，**Then** 只需在 `ContentType` 枚举中增加值，无需修改已有字段结构（可扩展性验证）。

---

### Edge Cases

- 发送方在消息写入数据库后、推送完成前断线：其他客户端下次重连时能通过历史加载获得此条消息。
- 两个客户端同时发送消息：服务端按接收顺序持久化，两侧均能看到正确的顺序（以服务端时间戳为准）。
- 输入框内容超过 10,000 字符：系统拒绝并给出长度提示（客户端验证为主，服务端兜底）。
- SignalR 连接中断后自动重连：Blazor 内置重连机制处理，UI 显示"重连中"状态（已有 `ReconnectModal`）。

---

## Requirements *(mandatory)*

> AnyDrop Constitution v1.1.0 约束适用：服务层与 UI 层分离、.NET 10 + Blazor Server + SignalR + SQLite/EF Core、PascalCase + Async 后缀、xUnit + Moq + Playwright 测试、容器化就绪。

### Functional Requirements

- **FR-001**：系统 MUST 定义 `ShareItem` 实体，支持 `Text`、`File`、`Image`、`Video`、`Link` 五种内容类型，并通过 `ContentType` 枚举字段区分。
- **FR-002**：系统 MUST 使用 EF Core + SQLite 持久化 `ShareItem`，数据库文件路径 MUST 通过配置注入（`Storage:DatabasePath`），不得硬编码。
- **FR-003**：系统 MUST 实现 SignalR Hub（`ShareHub`），当新 `ShareItem` 被创建后，向所有已连接客户端广播该条目，延迟目标 < 1 秒（局域网内）。
- **FR-004**：系统 MUST 实现 `IShareService`，提供 `SendTextAsync`（发送文本）、`GetRecentAsync`（获取最近 N 条）两个方法作为 MVP 最小接口。
- **FR-005**：系统 MUST 实现 `IFileStorageService`，提供文件保存与读取的物理存储抽象；MVP 阶段可仅实现接口，不需要文件上传 UI，但接口 MUST 存在以供后续实现。
- **FR-006**：系统 MUST 初始化项目骨架：`MainLayout` 包含侧边栏区域（`<aside>`）与主内容区域（`<main>`），使用 Fluent UI 组件和 Fluent Design Tokens 实现，MUST NOT 使用硬编码颜色。
- **FR-007**：首页（`/`）MUST 提供聊天式主界面：上方为消息时间线列表，下方为文本输入框 + 发送按钮；发送后输入框自动清空，消息列表自动滚动到最新。
- **FR-008**：系统 MUST 在 `Program.cs` 完成所有 DI 注册和中间件配置，包括 EF Core、SignalR、服务注册。
- **FR-009**：`ShareHub` MUST 仅调用 `IShareService`，不含业务逻辑；Razor 组件 MUST 通过 `@inject IShareService` 使用服务，不得直接操作数据库。

### Key Entities

- **ShareItem**：核心共享内容实体。字段：`Id`（Guid，主键）、`ContentType`（枚举：Text/File/Image/Video/Link）、`Content`（字符串，文本内容或文件路径/URL）、`FileName`（可空，文件类型用）、`FileSize`（可空，字节数）、`CreatedAt`（DateTimeOffset，服务端时间）。
- **ShareItemDto**：用于 SignalR 广播的数据传输对象，包含 `ShareItem` 的所有公开字段，不暴露导航属性。

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**：两台设备同时打开 AnyDrop，在其中一台发送文本，另一台在 **1 秒内**（局域网条件下）无需刷新即看到内容。
- **SC-002**：新设备打开页面后，**3 秒内**加载完最近 50 条历史消息并正确渲染。
- **SC-003**：`IShareService` 和 `IFileStorageService` 的所有公开方法均有对应单元测试，测试通过率 **100%**。
- **SC-004**：至少 **1 条 Playwright E2E 测试**验证"发送端发送文本 → 服务端 → 接收端收到推送"的完整链路。
- **SC-005**：UI 骨架在 360px（移动端最小宽度）至 1920px（桌面端）范围内均无布局错乱，无水平滚动条。
- **SC-006**：`dotnet build` 零警告、零错误；`dotnet test` 全部通过。

---

## Assumptions

- 本 Feature 为项目冷启动，从零初始化所有基础设施；后续 Feature 均基于本次建立的骨架扩展。
- MVP 阶段仅支持**文本类型**的发送与接收；文件、图片、链接的上传 UI 在后续 Feature 中实现，但实体和服务接口 MUST 在本次预先定义。
- 认证（Authentication）在本 Feature **暂不实现**；AnyDrop 目标为内网私有部署，初期允许无认证访问，但架构设计应为后续加入认证留出扩展点（如中间件位置）。
- `IFileStorageService` 本次仅定义接口和空实现（`LocalFileStorageService`），不实现文件上传入口。
- 历史消息分页策略：MVP 阶段固定加载最近 50 条，不实现无限滚动；该限制应在 `IShareService.GetRecentAsync(int count = 50)` 接口签名中体现。
- Fluent UI 版本固定为当前项目已引用版本（v4.14.0），不升级。
- 数据库文件默认路径：`./data/anydrop.db`（通过 `appsettings.json` 的 `Storage:DatabasePath` 配置）。
- Minimal API 端点和 OpenAPI/Scalar 集成**不在本版本实现**，延迟至后续 Feature 中引入（本版本 `Program.cs` 无需注册任何 HTTP API 路由或 OpenAPI 中间件）。
---

## Clarifications

### Session 2026-04-18

- Q: FR-008 中的"API 路由（含 OpenAPI）"应如何处理？ → A: 完全删除，FR-008 只保留 EF Core/SignalR/Services DI 注册（选项 A）。
