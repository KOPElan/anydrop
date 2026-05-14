# Implementation Plan: AnyDrop 移动端 App（MAUI Blazor Hybrid）

**Branch**: `006-maui-mobile-app` | **Date**: 2026-04-29 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/006-maui-mobile-app/spec.md`

## Summary

为 AnyDrop 自托管跨设备内容共享系统构建原生移动端 App（Android & iOS）。采用 .NET 10 MAUI Blazor Hybrid（BlazorWebView 嵌入原生宿主）架构，复用 Tailwind CSS 风格的 Blazor 组件实现全部 UI，通过 `IHttpClientFactory` + JWT Bearer DelegatingHandler 消费已完成的服务端 RESTful API（`/api/v1/`），通过 `Microsoft.AspNetCore.SignalR.Client` 实时接收服务端 Hub 推送。移动端特有能力（文件选择、安全存储、系统分享、本地通知）通过 MAUI 原生 API + DI 服务封装，确保 Blazor 组件层不直接依赖平台 API。

## Technical Context

**Language/Version**: C# 13 / .NET 10.0-android;net10.0-ios  
**Primary Dependencies**:
- `Microsoft.Maui.Controls` (10.0.x) — MAUI 宿主与原生 API
- `Microsoft.AspNetCore.Components.WebView.Maui` (10.0.x) — BlazorWebView
- `Microsoft.AspNetCore.SignalR.Client` (10.0.x) — SignalR 实时客户端
- `Microsoft.Extensions.Http` (10.0.x) — IHttpClientFactory
- `Plugin.LocalNotification` (10.1.x) — 本地推送通知
- Tailwind CSS Play CDN — UI 样式（wwwroot/index.html）

**Storage**:
- `SecureStorage` — JWT Token（平台 Keychain/Keystore）
- `Preferences` — 服务端 URL、语言、主题偏好（非敏感）
- 内存缓存 — 当前会话已加载消息列表（不持久化）

**Testing**:
- `xUnit` + `FluentAssertions` — Service 层单元测试（`AnyDrop.Tests.Unit`）
- `Moq` — HttpClient/SignalR 依赖 mock
- `Bunit` — Blazor 组件轻量级渲染测试（可选）

**Target Platform**: Android API 24+ (Android 7.0) / iOS 15.0+  
**Project Type**: mobile-app（MAUI Blazor Hybrid，BlazorWebView 混合应用）  
**Performance Goals**:
- 冷启动 → 主界面可交互 ≤ 4 秒（中端设备）
- 首屏消息列表加载 ≤ 2 秒（正常网络）
- SignalR 端到端消息延迟 ≤ 1 秒（局域网）

**Constraints**:
- App 不持久化消息数据库；所有内容来自服务端 API
- JWT 令牌必须存储于 `SecureStorage`，不得写入 `Preferences` 或普通文件
- 单用户系统，无多账号切换
- 语言仅支持 zh-CN 和 en

**Scale/Scope**: 5 主要页面 × 2 平台，约 30 个 Blazor 组件，8 个服务接口

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Verify the following gates against `.specify/memory/constitution.md` (AnyDrop v2.0.0):

- [x] **I. 单体架构分离**：服务层实现于 `AnyDrop.App/Services/`，Razor 组件通过 `@inject` 调用服务，无内联业务逻辑 ✅
- [~] **II. 技术栈合规**：使用 .NET 10 + MAUI Blazor Hybrid + Tailwind CSS（CDN）。**偏差**：此为移动客户端，不使用 Blazor Server / EF Core / SQLite（适用于服务端）；SignalR **Client** 替代 Hub；见 Complexity Tracking ✅（有据可查的合理偏差）
- [x] **III. 命名规范**：所有异步方法以 `Async` 结尾，接口以 `I` 开头，PascalCase 一致 ✅
- [x] **IV. 测试覆盖**：所有 Service 类有对应 xUnit 单元测试，HttpClient / SignalR 通过 Moq 隔离 ✅
- [x] **V. 安全合规**：JWT 存储于 `SecureStorage`（不明文写入 Preferences），文件上传前验证 MIME/大小，无硬编码凭证 ✅
- [~] **VI. 容器化**：**N/A** — 移动 App 不容器化；打包以 APK/IPA 形式分发 ✅（已知豁免）
- [~] **VII. RESTful API**：**N/A** — 此为 API 消费端（客户端），不提供 API 服务 ✅（已知豁免）

## Project Structure

### Documentation (this feature)

```text
specs/006-maui-mobile-app/
├── plan.md              # 本文件
├── research.md          # Phase 0 研究结论
├── data-model.md        # Phase 1 数据模型（Client DTO）
├── quickstart.md        # Phase 1 快速上手指南
├── contracts/           # Phase 1 API 集成契约
│   ├── api-endpoints.md
│   └── signalr-events.md
└── tasks.md             # Phase 2 任务清单（/speckit.tasks 生成）
```

### Source Code（AnyDrop.App/）

```text
AnyDrop.App/
├── AnyDrop.App.csproj           # .NET 10 MAUI Blazor Hybrid 项目文件
├── MauiProgram.cs               # DI 注册、HttpClient、服务注册入口
├── App.xaml / App.xaml.cs       # App 生命周期 + 启动路由决策
├── MainPage.xaml / .cs          # BlazorWebView 宿主页（唯一 MAUI 页面）
│
├── Models/                      # 客户端 DTO（镜像服务端 Models/）
│   ├── AuthModels.cs            # SetupStatusDto, LoginRequest, LoginResponse, UserProfileDto
│   ├── TopicModels.cs           # TopicDto, CreateTopicRequest, ReorderTopicsRequest, …
│   ├── ShareItemModels.cs       # ShareItemDto, ShareContentType, TopicMessagesResponse
│   └── SettingsModels.cs        # SecuritySettingsDto, UpdateNicknameRequest, …
│
├── Services/                    # 业务逻辑 + API 客户端（全部有接口）
│   ├── IServerConfigService.cs / ServerConfigService.cs   # BaseUrl 管理（Preferences）
│   ├── IAuthService.cs / AuthService.cs                   # 登录/登出/初始化/状态
│   ├── ITopicService.cs / TopicService.cs                 # 主题 CRUD + 排序
│   ├── IShareService.cs / ShareService.cs                 # 消息分页、发送文本、下载
│   ├── IFileUploadService.cs / FileUploadService.cs       # multipart 文件上传 + 进度
│   ├── ISearchService.cs / SearchService.cs               # 关键词/日期/类型搜索
│   ├── ISettingsService.cs / SettingsService.cs           # 昵称/密码/安全设置
│   ├── ISignalRService.cs / SignalRService.cs             # Hub 连接生命周期 + 事件订阅
│   ├── ISecureTokenStorage.cs / SecureTokenStorage.cs     # SecureStorage JWT 封装
│   ├── IConnectivityService.cs / ConnectivityService.cs   # 网络状态检测
│   ├── IPickerService.cs / PickerService.cs               # FilePicker / MediaPicker
│   └── INotificationService.cs / NotificationService.cs  # 本地推送通知
│
├── Infrastructure/
│   ├── AuthDelegatingHandler.cs   # JWT 注入 + 401 → 事件总线
│   ├── AppEventBus.cs             # 跨层事件总线（Singleton）
│   └── HubConnectionManager.cs   # SignalR 连接 + 指数退避重连策略
│
├── Components/
│   ├── _Imports.razor
│   ├── Routes.razor               # Blazor 路由表
│   ├── Layout/
│   │   ├── MainLayout.razor       # 离线横幅 + 底部导航栏 + 认证守卫
│   │   └── AuthLayout.razor       # 无导航栏的认证页布局
│   └── Pages/
│       ├── Setup/
│       │   └── ServerSetupPage.razor     # @page "/setup"  服务端 URL 配置
│       ├── Auth/
│       │   ├── LoginPage.razor            # @page "/login"
│       │   └── SetupAccountPage.razor     # @page "/setup-account"  首次初始化
│       ├── Home/
│       │   ├── HomePage.razor             # @page "/"  主聊天界面容器
│       │   ├── TopicSidebar.razor         # 主题列表侧边栏（支持拖拽排序）
│       │   ├── MessageList.razor          # 消息列表 + 虚拟滚动 + 分页加载
│       │   ├── MessageInput.razor         # 文本输入框 + 文件上传按钮
│       │   ├── MessageItem.razor          # 单条消息渲染（五种类型分支）
│       │   ├── ImagePreview.razor         # 全屏图片预览（touch-action: pinch-zoom）
│       │   └── TopicActionMenu.razor      # 主题右键/长按菜单（重命名/归档/删除/…）
│       ├── Search/
│       │   ├── SearchPage.razor           # @page "/search"
│       │   ├── SearchResultItem.razor
│       │   └── ActiveDateCalendar.razor   # 高亮有消息的日期
│       └── Settings/
│           └── SettingsPage.razor         # @page "/settings"
│
├── Platforms/
│   ├── Android/
│   │   ├── AndroidManifest.xml    # ACTION_SEND intent-filter + POST_NOTIFICATIONS 权限
│   │   ├── MainActivity.cs        # OnNewIntent 处理系统分享内容
│   │   └── MainApplication.cs
│   └── iOS/
│       ├── Info.plist             # UTTypes（图片/视频/data）+ NSPhotoLibraryUsageDescription
│       ├── AppDelegate.cs
│       └── Program.cs
│
└── wwwroot/
    ├── index.html                 # BlazorWebView HTML shell + Tailwind CDN + ThemeManager JS
    └── app.css                    # 自定义 CSS 层（safe-area insets、滚动优化等）
```

**Structure Decision**:  
采用 Option 3（Mobile + API）变体：`AnyDrop/`（服务端，已完成）+ `AnyDrop.App/`（移动客户端，本期实现）。MAUI App 无独立后端，所有数据来自服务端 API。客户端遵循与服务端相同的 Services/ + Components/ 分层规范，适配移动端场景（无 EF Core/SignalR Hub，改为 HttpClient + SignalR Client）。

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| II. Blazor Server → MAUI Blazor Hybrid | 移动 App 需运行在 Android/iOS 原生宿主，必须使用 MAUI BlazorWebView；Blazor Server 仅能在有服务器连接时使用，不适合离线场景 | Blazor Server 需网络连接到 .NET 服务器进程，无法打包为原生 App 分发 |
| II. EF Core/SQLite → 无本地 DB | 移动端设计为无本地消息缓存，所有数据来自服务端 API（spec 假设明确） | 引入本地 DB 会增加数据同步复杂度，与 spec 的"不持久化消息"假设冲突 |
| VI. 容器化 → N/A | 移动 App 以 APK/IPA 形式分发，不运行在 Docker 容器中 | 容器化原生 App 在分发链路上无意义 |
| VII. RESTful API → N/A（纯客户端） | AnyDrop.App 是 API 消费端，不提供 API 服务 | 移动端不需要暴露 HTTP 端点 |
