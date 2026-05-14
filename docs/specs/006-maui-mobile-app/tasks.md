# Tasks: AnyDrop 移动端 App（MAUI Blazor Hybrid）

**Input**: Design documents from `/specs/006-maui-mobile-app/`
**Prerequisites**: plan.md ✅ spec.md ✅ data-model.md ✅ contracts/api-endpoints.md ✅ contracts/signalr-events.md ✅ research.md ✅ quickstart.md ✅

**Tests**: AnyDrop Constitution (v2.0.0) Principle IV 强制要求所有 Service 类有对应 xUnit 单元测试，HttpClient / SignalR 通过 Moq 隔离。测试任务已内嵌在各用户故事 Phase 中，按"测试先于实现"顺序排列。

**Organization**: 任务按用户故事组织，每个故事独立可测试、独立可交付。

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel（不同文件、无未完成依赖）
- **[Story]**: 对应 spec.md 用户故事（US1–US7）
- 描述中包含完整文件路径

## Path Conventions（AnyDrop MAUI Blazor Hybrid）

- **App 源码**: `AnyDrop.App/`（Models, Services, Infrastructure, Components, Platforms, wwwroot）
- **单元测试**: `AnyDrop.Tests.Unit/`
- **E2E 测试**: `AnyDrop.Tests.E2E/`（本期不在范围内）

---

## Phase 1: Setup（项目初始化）

**Purpose**: 搭建 MAUI Blazor Hybrid 工程骨架、依赖包、平台配置与 wwwroot 入口，所有后续工作的前提。

- [ ] T001 配置 `AnyDrop.App/AnyDrop.App.csproj`：设置 `<TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>`，引入 NuGet 包（`Microsoft.AspNetCore.Components.WebView.Maui`、`Microsoft.AspNetCore.SignalR.Client`、`Microsoft.Extensions.Http`、`Plugin.LocalNotification 10.1.x`），确认 `UseMauiApp<App>` 与 `BlazorWebView` 已启用
- [ ] T002 创建 `AnyDrop.App/wwwroot/index.html`：BlazorWebView HTML shell，引入 Tailwind CSS Play CDN（`darkMode: 'class'`），添加 `<meta name="viewport" content="width=device-width, initial-scale=1, user-scalable=yes">`，注入 `ThemeManager` JS 对象（`setTheme(mode)` 切换 `<html>` 上的 `dark` class），设置安全区域 meta
- [ ] T003 [P] 创建 `AnyDrop.App/wwwroot/app.css`：自定义 CSS 层，包含 `env(safe-area-inset-*)` 安全区域 padding、iOS/Android 底部导航条高度补偿、移动端触控滚动优化（`-webkit-overflow-scrolling: touch`）、`touch-action: pinch-zoom` 全屏预览基础样式
- [ ] T004 [P] 配置 `AnyDrop.App/Platforms/Android/AndroidManifest.xml`：添加 `android:usesCleartextTraffic="true"`（HTTP 局域网访问）、`android.permission.INTERNET`、`android.permission.POST_NOTIFICATIONS`（API 33+）权限声明骨架（Intent Filter 在 Phase 8 US6 补充完整）
- [ ] T005 [P] 配置 `AnyDrop.App/Platforms/iOS/Info.plist`：添加 `NSPhotoLibraryUsageDescription`、`NSCameraUsageDescription`、`NSMicrophoneUsageDescription` 使用描述字符串骨架（UTTypes / CFBundleDocumentTypes 在 Phase 8 US6 补充完整）
- [ ] T006 配置 `AnyDrop.Tests.Unit/` 测试项目：确认 `AnyDrop.Tests.Unit.csproj` 引用 `xUnit`、`FluentAssertions`、`Moq`，并添加对 `AnyDrop.App` 项目的 `ProjectReference`
- [ ] T007 [P] 将 `AnyDrop.App` 与 `AnyDrop.Tests.Unit` 加入 `AnyDrop.slnx` 解决方案，执行 `dotnet restore AnyDrop.App/AnyDrop.App.csproj` 验证依赖可还原，执行 `dotnet build AnyDrop.App -f net10.0-android -c Debug` 确认骨架可编译
- [ ] T008 初始化 `AnyDrop.App/MauiProgram.cs`：创建 `CreateMauiApp()` 骨架，调用 `UseMauiApp<App>()`、`ConfigureFonts()`、`AddMauiBlazorWebView()`，预留服务注册注释区块（Foundational 和各 Phase 分批填入）

---

## Phase 2: Foundational（阻断性基础设施）

**Purpose**: 全部用户故事所依赖的数据模型、事件总线、安全存储、HTTP 客户端、SignalR 连接管理器、Blazor 布局骨架。**必须在任何用户故事开始前全部完成。**

**⚠️ CRITICAL**: 此 Phase 未完成前，任何用户故事任务均不可开始。

### 数据模型（Models/）

- [ ] T009 创建 `AnyDrop.App/Models/AuthModels.cs`：定义 `SetupStatusDto`、`SetupRequest`、`LoginRequest`、`LoginResponse`、`UserProfileDto`（均为 `sealed record`，命名空间 `AnyDrop.App.Models`）
- [ ] T010 [P] 创建 `AnyDrop.App/Models/TopicModels.cs`：定义 `TopicDto`、`CreateTopicRequest`、`UpdateTopicRequest`、`UpdateTopicIconRequest`、`PinTopicRequest`、`ArchiveTopicRequest`、`ReorderTopicsRequest`、`TopicOrderItem`、`TopicMessagesResponse`（含 `HasMore`、`NextCursor`）
- [ ] T011 [P] 创建 `AnyDrop.App/Models/ShareItemModels.cs`：定义 `ShareContentType` 枚举（Text=0, File=1, Image=2, Video=3, Link=4）、`ShareItemDto`（含 `LinkTitle`、`LinkDescription`、`ExpiresAt`）、`CreateTextShareItemRequest`、`ActiveDatesResponse`
- [ ] T012 [P] 创建 `AnyDrop.App/Models/SettingsModels.cs`：定义 `SecuritySettingsDto`、`UpdateSecuritySettingsRequest`、`UpdateNicknameRequest`、`UpdatePasswordRequest`
- [ ] T013 [P] 创建 `AnyDrop.App/Models/ApiResponse.cs`：定义泛型 `ApiResponse<T>(bool Success, T? Data, string? Error)` 用于统一解析服务端 `{ success, data, error }` envelope；添加 `SharedContent(string? Text, IReadOnlyList<string> FilePaths, string? MimeType)` 跨平台分享数据记录

### 基础设施（Infrastructure/）

- [ ] T014 创建 `AnyDrop.App/Infrastructure/AppEventBus.cs`：Singleton 事件总线，暴露 `event Action? AuthExpired`、`event Action<bool>? ConnectivityChanged`，提供 `RaiseAuthExpired()` 和 `RaiseConnectivityChanged(bool isOnline)` 方法
- [ ] T015 [P] 创建 `AnyDrop.App/Services/ISecureTokenStorage.cs` + `SecureTokenStorage.cs`：接口含 `SaveTokenAsync(string token, DateTimeOffset expiresAt)`、`GetTokenAsync()` (null if expired/absent, 提前 30s 判定过期)、`IsAuthenticatedAsync()`、`ClearTokenAsync()`；实现用 `SecureStorage` (Android Keystore / iOS Keychain)，读写加 `SemaphoreSlim(1,1)` 保护，iOS Keychain 异常 try-catch fallback
- [ ] T016 [P] 创建 `AnyDrop.App/Services/IServerConfigService.cs` + `ServerConfigService.cs`：`GetBaseUrl()`、`SetBaseUrlAsync(string url)`、`HasBaseUrl()`、`GetHubUrl()` (拼接 `/hubs/share`)，底层存 `Preferences["anydrop_base_url"]`，`GetBaseUrl()` 确保结尾无多余斜杠
- [ ] T017 [P] 创建 `AnyDrop.App/Services/IConnectivityService.cs` + `ConnectivityService.cs`：`bool IsOnline { get; }`，订阅 `Connectivity.ConnectivityChanged`，变更时触发 `AppEventBus.RaiseConnectivityChanged()`；Singleton 生命周期
- [ ] T018 创建 `AnyDrop.App/Infrastructure/AuthDelegatingHandler.cs`：Transient DelegatingHandler；`SendAsync` 中从 `ISecureTokenStorage.GetTokenAsync()` 获取 Token 注入 `Authorization: Bearer` 头；响应 401 时调用 `ISecureTokenStorage.ClearTokenAsync()` + `AppEventBus.RaiseAuthExpired()`，返回原始 response 不抛异常（让调用方感知 401 而不崩溃）
- [ ] T019 创建 `AnyDrop.App/Infrastructure/HubConnectionManager.cs`：Singleton；构造时用 `HubConnectionBuilder` 配置 Hub URL（来自 `IServerConfigService.GetHubUrl()`）、`AccessTokenProvider`（`ISecureTokenStorage.GetTokenAsync`）、`WithAutomaticReconnect(ExponentialBackoffRetryPolicy)`（0ms→2s→10s→30s cap）；实现 `IAsyncDisposable`；提供 `HubConnection Connection { get; }` 属性；BaseUrl 变更后需调用 `DisposeAsync()` + 重建（由 `ISignalRService` 处理）

### MauiProgram.cs 服务注册

- [ ] T020 完善 `AnyDrop.App/MauiProgram.cs`：注册全部基础设施服务：`AppEventBus` (Singleton)、`ISecureTokenStorage` (Singleton)、`IServerConfigService` (Singleton)、`IConnectivityService` (Singleton)；配置命名 HttpClient `"api"`（BaseAddress 动态从 `IServerConfigService.GetBaseUrl()` 取，Timeout 30s），挂载 `AuthDelegatingHandler` (Transient)；注册 `HubConnectionManager` (Singleton)；调用 `builder.UseLocalNotification()`
- [ ] T021 [P] 注册 Blazor/Scoped 服务占位到 `AnyDrop.App/MauiProgram.cs`：`IAppStateService` (Scoped) 占位注释，各业务 Service 在对应 Phase 内按故事分批补入（避免因缺少实现类导致编译失败）

### Blazor 路由与布局骨架

- [ ] T022 创建 `AnyDrop.App/Components/_Imports.razor`：全局 `@using` 指令（`AnyDrop.App.Models`、`AnyDrop.App.Services`、`AnyDrop.App.Infrastructure`、`Microsoft.AspNetCore.Components`、`Microsoft.AspNetCore.Components.Routing`）
- [ ] T023 创建 `AnyDrop.App/Components/Routes.razor`：Blazor Router 配置（`AppAssembly`、`NotFound` fallback），用 `<AuthorizeRouteView>` 或自定义 `<RouteGuard>` 检查 `ISecureTokenStorage.IsAuthenticatedAsync()`，未认证时重定向至 `/login`；保留 `PendingSharedContent` 检测占位（Phase 8 补全）
- [ ] T024 创建 `AnyDrop.App/Components/Layout/AuthLayout.razor`：无底部导航栏的纯白布局，用于 `/setup`、`/login`、`/setup-account` 页面，居中显示 App Logo + 标题
- [ ] T025 创建 `AnyDrop.App/Components/Layout/MainLayout.razor`：包含：① 顶部/底部 SignalR 连接状态横幅（初始隐藏）② 底部导航栏三标签（主页 `/`、搜索 `/search`、设置 `/settings`，含激活态高亮）③ `@Body` 内容区 ④ 订阅 `AppEventBus.AuthExpired` → `NavigationManager.NavigateTo("/login")` ⑤ 订阅 `AppEventBus.ConnectivityChanged` → 显示/隐藏离线横幅
- [ ] T026 创建 `AnyDrop.App/Services/IAppStateService.cs` + `AppStateService.cs`（Scoped）：字段参见 data-model.md Section 5：`CurrentTopicId`、`Topics`、`Messages`、`HasMoreMessages`、`MessageCursor`、`SignalRState`、`event Action? OnChange`、`NotifyStateChanged()`

### 启动路由

- [ ] T027 实现 `AnyDrop.App/App.xaml.cs` `OnStart()` 启动路由决策：调用 `ISecureTokenStorage.IsAuthenticatedAsync()`→ 有效 Token → `Shell.Current.GoToAsync("//home")`；无 BaseUrl → `"//setup"`；有 BaseUrl 无 Token → `"//login"`
- [ ] T028 配置 `AnyDrop.App/MainPage.xaml` / `MainPage.xaml.cs`：BlazorWebView 宿主 MAUI 页面，`HostPage="wwwroot/index.html"`，`RootComponent ComponentType="typeof(Routes)"` 绑定至 `#app` 选择器

**Checkpoint**: Foundation ready — 执行 `dotnet build -f net10.0-android`，确认 0 错误。用户故事实现可开始。

---

## Phase 3: User Story 1 — 首次配置与登录（P1）🎯 MVP

**Goal**: 用户首次安装 App 后，输入服务端地址、完成首次账号创建或登录，成功跳转主界面。

**Independent Test**: 在全新设备/模拟器上安装 App → 进入服务端配置页 → 输入有效 URL → 跳转登录/初始化页 → 完成登录 → 看到主界面（底部导航可见），即为 MVP 完整路径。

### 单元测试（US1）— 先写后实现

- [ ] T029 [P] [US1] 创建 `AnyDrop.Tests.Unit/AuthServiceTests.cs`：覆盖 `GetSetupStatusAsync`（requiresSetup true/false 两路）、`SetupAsync`（成功保存 Token）、`LoginAsync`（密码正确保存 Token / 密码错误返回错误 / 401 事件触发）、`LogoutAsync`（调用 API + 清除 Token）、`GetCurrentUserAsync`，通过 `Moq` Mock `IHttpClientFactory` / `ISecureTokenStorage`
- [ ] T030 [P] [US1] 创建 `AnyDrop.Tests.Unit/ServerConfigServiceTests.cs`：覆盖 `HasBaseUrl`（有/无 Preferences）、`SetBaseUrlAsync`（去尾斜杠、写入 Preferences）、`GetHubUrl`（正确拼接 `/hubs/share`）

### 实现（US1）

- [ ] T031 [P] [US1] 创建 `AnyDrop.App/Services/IAuthService.cs` + `AuthService.cs`：方法：`GetSetupStatusAsync()→SetupStatusDto`、`SetupAsync(SetupRequest)→LoginResponse`、`LoginAsync(LoginRequest)→LoginResponse`、`LogoutAsync()`、`GetCurrentUserAsync()→UserProfileDto`；注入 `IHttpClientFactory`（取命名 Client `"api"`）+ `ISecureTokenStorage`；登录/Setup 成功后调用 `_tokenStorage.SaveTokenAsync()`
- [ ] T032 [US1] 完善 `AnyDrop.App/Services/ServerConfigService.cs`：在 `SetBaseUrlAsync` 中标准化 URL（trim trailing slash、补 `http://` 前缀校验），提供 `ValidateUrlAsync()` 方法（发起 `HEAD {baseUrl}/api/v1/auth/setup-status`，成功则有效）
- [ ] T033 [US1] 创建 `AnyDrop.App/Components/Pages/Setup/ServerSetupPage.razor`：`@page "/setup"`，`@layout AuthLayout`；输入框绑定服务端 URL，格式校验（必须含协议前缀）；点击"连接"调用 `ServerConfigService.ValidateUrlAsync()`，成功 → 保存 → `NavigationManager.NavigateTo("/login")`；失败显示"无法连接到服务端"内联错误提示；加载状态 spinner
- [ ] T034 [P] [US1] 创建 `AnyDrop.App/Components/Pages/Auth/LoginPage.razor`：`@page "/login"`，`@layout AuthLayout`；`OnInitializedAsync` 调用 `IAuthService.GetSetupStatusAsync()`，`requiresSetup=true` → redirect `/setup-account`；密码输入框 + 登录按钮；调用 `LoginAsync` → 成功：启动 SignalR（`ISignalRService.StartAsync()`）→ `NavigationManager.NavigateTo("/")`；失败：显示友好错误文字，保持页面，不清除 Token
- [ ] T035 [P] [US1] 创建 `AnyDrop.App/Components/Pages/Auth/SetupAccountPage.razor`：`@page "/setup-account"`，`@layout AuthLayout`；昵称 + 密码 + 确认密码表单；调用 `AuthService.SetupAsync()` → 成功保存 Token → `NavigationManager.NavigateTo("/")`；前端校验密码一致性；服务端错误显示 `error` 字段内容
- [ ] T036 [US1] 在 `AnyDrop.App/MauiProgram.cs` 注册 `IAuthService`（Scoped）；更新 `App.xaml.cs` `OnStart()` 完整逻辑（调用 `IServerConfigService.HasBaseUrl()` 条件分支）
- [ ] T037 [US1] 验证 `AuthDelegatingHandler.cs` 中 401 → `AppEventBus.RaiseAuthExpired()` → `MainLayout.razor` 订阅 → `NavigationManager.NavigateTo("/login", forceLoad: false)` 端到端流程（手动测试：用过期 Token 访问任意 API 端点，应跳转登录页）

**Checkpoint**: US1 完成 — 新设备安装后完整登录流程可走通，主界面可见。**MVP 路径 1/2 达成。**

---

## Phase 4: User Story 2 — 消息收发（主聊天界面）（P1）🎯 MVP

**Goal**: 用户在主界面可查看主题列表、切换主题、浏览历史消息（分页）、发送文本消息、上传文件，并通过 SignalR 实时接收其他端推送的新消息；支持图片全屏预览。

**Independent Test**: 选中一个主题 → 发送一条文本消息 → 消息出现在列表底部；在另一设备发送消息 → 本端列表自动追加，即为独立可测试核心交互。

### 单元测试（US2）— 先写后实现

- [ ] T038 [P] [US2] 创建 `AnyDrop.Tests.Unit/SignalRServiceTests.cs`：覆盖 `StartAsync`（首次连接）、`StopAsync`、`StateChanged` 事件触发（Connected / Reconnecting / Disconnected）、`TopicsUpdated` 事件正确 invoke；通过 `Moq` Mock `HubConnectionManager` 的 `HubConnection`
- [ ] T039 [P] [US2] 创建 `AnyDrop.Tests.Unit/ShareServiceTests.cs`：覆盖 `GetMessagesAsync`（首次加载、分页 `before` 参数、`hasMore=false` 停止）、`SendTextAsync`（201 返回正确 DTO）、`DownloadFileAsync`（返回流），通过 Moq Mock `IHttpClientFactory`
- [ ] T040 [P] [US2] 创建 `AnyDrop.Tests.Unit/FileUploadServiceTests.cs`：覆盖上传成功返回 `ShareItemDto`、`IProgress<double>` 回调被正确调用、文件流式上传（不读入 MemoryStream），通过 Moq Mock `IHttpClientFactory` + `IPickerService`

### 实现（US2）

- [ ] T041 [P] [US2] 创建 `AnyDrop.App/Services/ISignalRService.cs` + `SignalRService.cs`：`StartAsync(CancellationToken ct)`（调用 `HubConnectionManager.Connection.StartAsync()`）、`StopAsync()`、`HubConnectionState State { get; }`、`event Action<HubConnectionState>? StateChanged`（订阅 `Reconnecting` / `Reconnected` / `Closed` 委托）、`event Action<IReadOnlyList<TopicDto>>? TopicsUpdated`（注册 `connection.On<IReadOnlyList<TopicDto>>("TopicsUpdated", ...)`）；Closed 时若因 401 触发 `AppEventBus.RaiseAuthExpired()`
- [ ] T042 [P] [US2] 创建 `AnyDrop.App/Services/IShareService.cs` + `ShareService.cs`：`GetMessagesAsync(Guid topicId, string? before, int limit=30)→TopicMessagesResponse`、`SendTextAsync(CreateTextShareItemRequest)→ShareItemDto`、`DownloadFileAsync(Guid id)→Stream`（`GET /api/v1/share-items/{id}/file?download=true`）
- [ ] T043 [P] [US2] 创建 `AnyDrop.App/Services/IFileUploadService.cs` + `FileUploadService.cs`：`UploadFileAsync(Stream fileStream, string fileName, string mimeType, Guid topicId, IProgress<double>? progress)→ShareItemDto`；使用 `StreamContent` + `MultipartFormDataContent` 构造 multipart 请求至 `POST /api/v1/files`；大文件流式上传不缓存 MemoryStream
- [ ] T044 [P] [US2] 创建 `AnyDrop.App/Services/IPickerService.cs` + `PickerService.cs`：`PickFileAsync()→(Stream, string fileName, string mimeType)?`（`FilePicker.PickAsync`）、`PickPhotoAsync()→(Stream, string fileName, string mimeType)?`（`MediaPicker.PickPhotoAsync`）、`PickVideoAsync()→(Stream, string fileName, string mimeType)?`（`MediaPicker.PickVideoAsync`）；必须在 UI 线程调用（Blazor 事件处理器天然满足）
- [ ] T045 [US2] 在 `AnyDrop.App/MauiProgram.cs` 补充注册：`ISignalRService` (Singleton)、`IShareService` (Scoped)、`IFileUploadService` (Scoped)、`IPickerService` (Singleton)
- [ ] T046 [US2] 创建 `AnyDrop.App/Components/Pages/Home/HomePage.razor`：`@page "/"`，`@layout MainLayout`；`OnInitializedAsync` 加载主题列表（`ITopicService.GetTopicsAsync()`，TopicService 在 US3 完整实现，此处先用 `IShareService` 获取首条消息作占位）；启动 SignalR（若 State=Disconnected）；响应式布局：手机竖屏时 TopicSidebar 抽屉式显示，横屏/平板时侧边栏常驻
- [ ] T047 [US2] 创建 `AnyDrop.App/Components/Pages/Home/TopicSidebar.razor`：渲染置顶主题区块 + 普通主题区块；每项显示图标 + 名称 + 未读/最新消息预览；点击主题 → 更新 `AppStateService.CurrentTopicId` → 触发消息列表加载；长按触发 `TopicActionMenu`（US3 补全）
- [ ] T048 [US2] 创建 `AnyDrop.App/Components/Pages/Home/MessageList.razor`：`OnParametersSetAsync` 中当 `CurrentTopicId` 变更时调用 `IShareService.GetMessagesAsync()` 重置消息列表；滚动容器用 `overflow-y: auto` + 初始滚动至底部（`JS interop scrollTo`）；在列表顶部放置"加载更多"触发区（Intersection Observer 或手动按钮），加载时附带 `AppStateService.MessageCursor` 作为 `before` 参数，`HasMore=false` 时隐藏按钮；订阅 `ISignalRService.TopicsUpdated` → 比较 `MessageCount` → 按需拉取新消息尾部；`IAsyncDisposable` 中取消订阅
- [ ] T049 [US2] 创建 `AnyDrop.App/Components/Pages/Home/MessageItem.razor`：接收 `ShareItemDto` 参数；依 `ContentType` 分支渲染：Text（`<pre>` 保留换行）、Image（`<img>` 缩略图 + 点击导航 `/image-preview/{id}`）、Video（缩略图 + 播放图标 + 点击 `Launcher.OpenAsync`）、File（文件图标 + 文件名 + 大小 + 下载按钮调 `IShareService.DownloadFileAsync`）、Link（OGP 卡片：LinkTitle + LinkDescription + 域名，点击 `Browser.OpenAsync`）；所有需认证的图片 URL 通过 `{BaseUrl}/api/v1/share-items/{id}/file` 带 Authorization 头请求（使用 JS fetch + Blob URL 或服务端代理 Token）
- [ ] T050 [US2] 创建 `AnyDrop.App/Components/Pages/Home/MessageInput.razor`：文本输入框（`textarea` + `@bind`）+ 发送按钮（点击调 `IShareService.SendTextAsync`，成功追加至 `AppStateService.Messages` + `NotifyStateChanged`）；文件/图片/视频选择按钮（调 `IPickerService`，取到流后调 `IFileUploadService.UploadFileAsync`）；上传进度条（`<progress value>` 绑定 `IProgress<double>` 回调更新的状态值）；上传期间禁用发送按钮
- [ ] T051 [US2] 创建 `AnyDrop.App/Components/Pages/Home/ImagePreview.razor`：`@page "/image-preview/{Id}"` + `@layout AuthLayout`（全屏，无底部导航）；用 CSS `position:fixed; inset:0; background:black; touch-action:pinch-zoom` 覆盖全屏；`<img>` 加载 `{BaseUrl}/api/v1/share-items/{Id}/file`；右上角关闭按钮 / 系统返回手势退出预览
- [ ] T052 [US2] 实现 `AppStateService` 消息去重逻辑：`TopicsUpdated` 到达时，若当前主题 `MessageCount > Messages.Count`，调用 `GetMessagesAsync(currentTopicId, before: null)` 拉取最新一批，过滤已存在 `Id` 后 `AddRange`，调用 `NotifyStateChanged()`
- [ ] T053 [US2] 完善 `MainLayout.razor` SignalR 状态横幅：订阅 `ISignalRService.StateChanged`；`Reconnecting` → 顶部黄色横幅"正在重新连接…"；`Disconnected` → 红色横幅"已断开连接"+ 手动重试按钮（调 `ISignalRService.StartAsync()`）；`Connected` → 隐藏横幅

**Checkpoint**: US2 完成 — 完整消息收发 + 实时推送可测试。**MVP 路径 2/2 达成。P1 用户故事全部完成。**

---

## Phase 5: User Story 3 — 主题管理（P2）

**Goal**: 用户可创建、重命名、更改图标、置顶、归档、删除主题，并拖拽调整排序。

**Independent Test**: 从空主题列表创建新主题 → 选中该主题 → 发送一条消息 → 消息出现，即为独立路径。

### 单元测试（US3）— 先写后实现

- [ ] T054 [P] [US3] 创建 `AnyDrop.Tests.Unit/TopicServiceTests.cs`：覆盖 `GetTopicsAsync`（返回列表 + 置顶/普通分区）、`GetArchivedTopicsAsync`、`CreateTopicAsync`（201 返回 DTO）、`UpdateTopicAsync`（rename）、`UpdateTopicIconAsync`、`PinTopicAsync`（isPinned=true/false）、`ArchiveTopicAsync`、`ReorderTopicsAsync`（批量 PUT）、`DeleteTopicAsync`（204/200），通过 Moq Mock `IHttpClientFactory`

### 实现（US3）

- [ ] T055 [P] [US3] 创建 `AnyDrop.App/Services/ITopicService.cs` + `TopicService.cs`：`GetTopicsAsync()→IReadOnlyList<TopicDto>`（`GET /api/v1/topics`）、`GetArchivedTopicsAsync()→IReadOnlyList<TopicDto>`（`GET /api/v1/topics/archived`）、`CreateTopicAsync(CreateTopicRequest)→TopicDto`、`UpdateTopicAsync(Guid, UpdateTopicRequest)`、`UpdateTopicIconAsync(Guid, UpdateTopicIconRequest)`、`PinTopicAsync(Guid, PinTopicRequest)`、`ArchiveTopicAsync(Guid, ArchiveTopicRequest)`、`ReorderTopicsAsync(ReorderTopicsRequest)`、`DeleteTopicAsync(Guid)`
- [ ] T056 [US3] 在 `AnyDrop.App/MauiProgram.cs` 补充注册 `ITopicService`（Scoped）；更新 `HomePage.razor` 的 `OnInitializedAsync` 改用 `ITopicService.GetTopicsAsync()` 填充 `AppStateService.Topics`
- [ ] T057 [US3] 创建 `AnyDrop.App/Components/Pages/Home/TopicActionMenu.razor`：长按主题项弹出底部操作表（Bottom Sheet）或下拉菜单，选项：重命名（内联编辑输入框 → `UpdateTopicAsync`）、更改图标（Emoji 选择器 → `UpdateTopicIconAsync`）、置顶/取消置顶（`PinTopicAsync`）、归档/取消归档（`ArchiveTopicAsync`）、删除（弹二次确认 dialog → `DeleteTopicAsync` → 从 `AppStateService.Topics` 移除 + `NotifyStateChanged`）
- [ ] T058 [US3] 在 `HomePage.razor` / `TopicSidebar.razor` 添加"新建主题"入口（"+" 按钮）：弹出 Bottom Sheet 含名称输入框 → `ITopicService.CreateTopicAsync()` → 将新 TopicDto prepend/append 至 `AppStateService.Topics` → `NotifyStateChanged`
- [ ] T059 [US3] 在 `TopicSidebar.razor` 实现拖拽排序：用 HTML5 `draggable` 属性 + `ondragover` / `ondrop` 事件（或 touch `touchstart`/`touchmove`/`touchend`）；拖拽完成后收集新 `SortOrder` 序列 → 调 `ITopicService.ReorderTopicsAsync()` → 更新 `AppStateService.Topics` 本地顺序
- [ ] T060 [US3] 在 `TopicSidebar.razor` 底部添加"已归档主题"入口：点击展开/折叠归档列表（调 `ITopicService.GetArchivedTopicsAsync()`，懒加载），每项支持"取消归档"操作（`ArchiveTopicAsync(id, IsArchived: false)`）

**Checkpoint**: US3 完成 — 主题全生命周期管理可验证。

---

## Phase 6: User Story 4 — 搜索（P2）

**Goal**: 用户在独立搜索页通过文本关键词、日期、内容类型查找历史消息，结果支持分页。

**Independent Test**: 搜索页输入关键词 → 结果列表展示匹配消息，即为独立路径。

### 单元测试（US4）— 先写后实现

- [ ] T061 [P] [US4] 创建 `AnyDrop.Tests.Unit/SearchServiceTests.cs`：覆盖 `SearchAsync`（有结果 / 无结果空列表 / 分页 before 参数）、`GetByDateAsync`（返回指定日消息）、`GetActiveDatesAsync`（year+month 参数、返回 DateOnly 列表）、`GetByTypeAsync`（Image/File 等类型筛选 + 分页），通过 Moq Mock `IHttpClientFactory`

### 实现（US4）

- [ ] T062 [P] [US4] 创建 `AnyDrop.App/Services/ISearchService.cs` + `SearchService.cs`：`SearchAsync(Guid topicId, string q, int limit=20, string? before=null)→IReadOnlyList<ShareItemDto>`（`GET /api/v1/topics/{id}/messages/search?q=&limit=&before=`）；`GetByDateAsync(Guid topicId, DateOnly date)→IReadOnlyList<ShareItemDto>`；`GetActiveDatesAsync(Guid topicId, int year, int month)→IReadOnlyList<DateOnly>`；`GetByTypeAsync(Guid topicId, ShareContentType type, int limit=20, string? before=null)→IReadOnlyList<ShareItemDto>`
- [ ] T063 [US4] 在 `AnyDrop.App/MauiProgram.cs` 补充注册 `ISearchService`（Scoped）
- [ ] T064 [US4] 创建 `AnyDrop.App/Components/Pages/Search/SearchPage.razor`：`@page "/search"`，`@layout MainLayout`；顶部搜索模式 Tab（关键词 / 日期 / 类型）；关键词模式：`<input>` 输入触发 `ISearchService.SearchAsync`（debounce 300ms）+ 分页加载更多；日期模式：嵌入 `ActiveDateCalendar.razor`，选中日期调 `GetByDateAsync`；类型模式：5 个类型 Tab（文本/图片/视频/文件/链接）→ `GetByTypeAsync`；无结果时展示"未找到相关内容"空状态插图
- [ ] T065 [P] [US4] 创建 `AnyDrop.App/Components/Pages/Search/SearchResultItem.razor`：复用 `MessageItem.razor` 内容结构，但增加所属主题名称显示；点击跳转至 `HomePage` 并选中对应主题（通过 `AppStateService.CurrentTopicId` + NavigationManager）
- [ ] T066 [P] [US4] 创建 `AnyDrop.App/Components/Pages/Search/ActiveDateCalendar.razor`：月份视图网格；`OnParametersSetAsync` 中调 `ISearchService.GetActiveDatesAsync` 获取当月有消息日期并高亮（CSS 圆点标记）；左右箭头切换月份（每次切换重新获取高亮日期）；点击高亮日期触发 `OnDateSelected` 回调供父组件使用

**Checkpoint**: US4 完成 — 搜索功能三种模式独立可验证。

---

## Phase 7: User Story 5 — 设置（P2）

**Goal**: 用户可修改昵称/密码、调整安全设置、切换语言与主题，手动清理旧消息，变更服务端地址，退出登录。

**Independent Test**: 进入设置页修改昵称并保存 → 主界面显示更新后昵称，即为独立路径。

### 单元测试（US5）— 先写后实现

- [ ] T067 [P] [US5] 创建 `AnyDrop.Tests.Unit/SettingsServiceTests.cs`：覆盖 `GetSecuritySettingsAsync`（返回 DTO）、`UpdateSecuritySettingsAsync`（PUT 请求正确构造）、`UpdateNicknameAsync`（PUT /settings/profile）、`UpdatePasswordAsync`（成功 / 400 当前密码错误）、`CleanupOldMessagesAsync`（返回 deletedCount）

### 实现（US5）

- [ ] T068 [P] [US5] 创建 `AnyDrop.App/Services/ISettingsService.cs` + `SettingsService.cs`：`GetSecuritySettingsAsync()→SecuritySettingsDto`（`GET /api/v1/settings/security`）、`UpdateSecuritySettingsAsync(UpdateSecuritySettingsRequest)`（`PUT /api/v1/settings/security`）、`UpdateNicknameAsync(UpdateNicknameRequest)`（`PUT /api/v1/settings/profile`）、`UpdatePasswordAsync(UpdatePasswordRequest)`（`PUT /api/v1/settings/password`）、`CleanupOldMessagesAsync(int months)→int`（`DELETE /api/v1/share-items/cleanup?months={months}`，返回 `deletedCount`）
- [ ] T069 [US5] 在 `AnyDrop.App/MauiProgram.cs` 补充注册 `ISettingsService`（Scoped）
- [ ] T070 [US5] 创建 `AnyDrop.App/Components/Pages/Settings/SettingsPage.razor`：`@page "/settings"`，`@layout MainLayout`；`OnInitializedAsync` 加载当前用户信息（`IAuthService.GetCurrentUserAsync`）与安全设置（`ISettingsService.GetSecuritySettingsAsync`）；按功能分组：个人资料、安全设置、外观与语言、危险区域
- [ ] T071 [US5] 实现设置页"个人资料"区域：昵称内联编辑（点击进入 edit 模式 → 修改 → 调 `UpdateNicknameAsync` → 更新 `Preferences["anydrop_nickname"]` + 顶部显示新昵称）；密码修改表单（currentPassword + newPassword + confirmPassword → `UpdatePasswordAsync`，错误文字高亮）
- [ ] T072 [US5] 实现设置页"安全设置"区域：自动获取链接预览 Toggle、阅后即焚时长 Select（0/1/5/10/30 分钟）、自动清理 Toggle + 保留月数 Select → 表单修改后点"保存" → `UpdateSecuritySettingsAsync`；手动清理按钮 → 输入保留月数 → 确认 dialog → `CleanupOldMessagesAsync` → Toast 反馈（共删除 N 条）
- [ ] T073 [US5] 实现设置页"外观与语言"区域：
  - 语言 Toggle（中文/English）→ `Preferences["anydrop_language"]` → `CultureInfo.CurrentCulture`/`CurrentUICulture` 切换 → `IStringLocalizer` 或资源字典重载（含中英文 JSON 资源文件 `AnyDrop.App/Resources/Strings/zh-CN.resx` + `en.resx`）
  - 深色/浅色主题 Toggle → `Preferences["anydrop_theme"]` → `JSRuntime.InvokeVoidAsync("ThemeManager.setTheme", mode)`
- [ ] T074 [US5] 实现设置页"危险区域"：修改服务端地址（输入框预填当前 URL → 保存 → `IServerConfigService.SetBaseUrlAsync` → `ISignalRService.StopAsync` → `ISecureTokenStorage.ClearTokenAsync` → `NavigationManager.NavigateTo("/setup")`）；退出登录（确认 dialog → `IAuthService.LogoutAsync` → `ISecureTokenStorage.ClearTokenAsync` → `NavigationManager.NavigateTo("/login")`）
- [ ] T075 [US5] 创建本地化资源文件 `AnyDrop.App/Resources/Strings/AppStrings.zh-CN.resx` + `AppStrings.en.resx`，覆盖所有静态 UI 文本（页面标题、按钮标签、错误提示、Toast 消息），在 `MauiProgram.cs` 注册 `IStringLocalizer<AppStrings>`

**Checkpoint**: US5 完成 — 设置页所有子功能独立可验证。

---

## Phase 8: User Story 6 — 接收外部分享内容（P3）

**Goal**: 用户从其他 App 通过系统分享功能将文字/图片/文件分享至 AnyDrop，选择目标主题后发送成功。

**Independent Test**: 系统相册选择图片 → 分享给 AnyDrop → 选择目标主题 → 发送成功消息出现在主题列表，即为完整路径。

- [ ] T076 [P] [US6] 完善 `AnyDrop.App/Platforms/Android/AndroidManifest.xml`：在 `<activity>` 上添加 `android:launchMode="singleTask"`；声明 `ACTION_SEND`（`text/*`）、`ACTION_SEND`（`image/*`、`video/*`、`application/*`）、`ACTION_SEND_MULTIPLE`（`image/*`、`application/*`）Intent Filter
- [ ] T077 [P] [US6] 完善 `AnyDrop.App/Platforms/iOS/Info.plist`：添加 `CFBundleDocumentTypes`（`public.image`、`public.movie`、`public.data`）和对应 UTI，使 App 在 iOS Share Sheet 的"Open In"菜单中出现
- [ ] T078 [US6] 实现 `AnyDrop.App/Platforms/Android/MainActivity.cs` `OnNewIntent`：解析 `Intent.Action.Send` / `SendMultiple`；文字内容取 `Intent.GetStringExtra("android.intent.extra.TEXT")`；文件/图片取 `intent.GetParcelableExtra(Intent.ExtraStream)` 的 `Uri`，用 `ContentResolver.OpenInputStream(uri)` 读取后 copy 到 `CacheDir`；将结果存入 `PendingSharedContent`（全局静态字段），触发 App 前台 + Blazor 检测
- [ ] T079 [US6] 实现 `AnyDrop.App/Platforms/iOS/AppDelegate.cs` `OpenUrl` / `ContinueUserActivity`：接收文件 URL，copy 到 App 沙盒 Cache 目录，填充 `PendingSharedContent`
- [ ] T080 [US6] 创建 `AnyDrop.App/Components/Pages/Share/ExternalSharePage.razor`：`@page "/share"`，`@layout AuthLayout`；`OnInitializedAsync` 读取 `PendingSharedContent`：显示内容预览（文字 `<pre>` 或图片缩略图或文件名+大小）+ 主题下拉选择器（从 `ITopicService.GetTopicsAsync()` 加载）+ "发送"按钮；点击发送：文字 → `IShareService.SendTextAsync`，文件 → `IFileUploadService.UploadFileAsync`；发送成功后清空 `PendingSharedContent`，关闭界面（`NavigationManager.NavigateTo("/")`）；发送失败显示错误 Toast，允许重试
- [ ] T081 [US6] 在 `AnyDrop.App/Components/Routes.razor` `OnInitializedAsync` 中检测 `PendingSharedContent != null`：已认证 → `NavigationManager.NavigateTo("/share")`；未认证 → 存储 `returnTo="/share"` 标志，跳转 `/login`，登录成功后自动导航至 `/share`（在 `LoginPage.razor` 补充 returnTo 逻辑）
- [ ] T082 [US6] 处理分享时 App 处于离线状态：`ExternalSharePage.razor` 检查 `IConnectivityService.IsOnline`，离线时显示"无网络连接，内容已暂存"提示，"重试"按钮（重连后重新发送，`PendingSharedContent` 保持有效直到发送成功）

**Checkpoint**: US6 完成 — Android + iOS 外部分享流程可验证。

---

## Phase 9: User Story 7 — 新消息本地通知（P3）

**Goal**: App 在后台运行时，收到新消息通过系统通知提醒，点击通知跳转至对应主题。

**Independent Test**: App 切换后台 → 另一设备发送消息 → 本机通知栏出现消息通知，点击跳转对应主题，即为可测路径。

- [ ] T083 [P] [US7] 确认 `AnyDrop.App/AnyDrop.App.csproj` 已包含 `Plugin.LocalNotification` (10.1.x) 并在 `MauiProgram.cs` 调用了 `builder.UseLocalNotification()`（T001/T020 已预置，此处验证并激活）
- [ ] T084 [P] [US7] 完善 `AnyDrop.App/Platforms/Android/AndroidManifest.xml`：确认 `android.permission.POST_NOTIFICATIONS` 权限存在；在 `MainActivity.cs` `OnCreate` 中加入运行时权限请求：`await Permissions.RequestAsync<Permissions.PostNotifications>()`（仅在 API 33+ 执行）
- [ ] T085 [US7] 创建 `AnyDrop.App/Services/INotificationService.cs` + `NotificationService.cs`：`RequestPermissionAsync()→bool`（调用 `Plugin.LocalNotification` 权限 API）；`ShowMessageNotificationAsync(string topicName, string preview, Guid topicId)`（构造 `NotificationRequest`，`BadgeNumber`、`Title=topicName`、`Description=preview`，通知点击 Action 携带 `topicId`）；`CancelAllAsync()`；权限被拒绝时静默失败不抛异常
- [ ] T086 [US7] 在 `AnyDrop.App/MauiProgram.cs` 注册 `INotificationService`（Singleton）；在用户登录成功后（`LoginPage.razor` / `SetupAccountPage.razor` 登录回调中）调用 `INotificationService.RequestPermissionAsync()`
- [ ] T087 [US7] 在 `SignalRService.cs` `TopicsUpdated` 事件处理中注入并调用 `INotificationService`：当 App 处于后台（`AppState == ApplicationState.Background`，通过 MAUI `ILifecycleBuilder` / `Application.Current.MainPage` 可见性判断）且消息数增加时，调用 `ShowMessageNotificationAsync(topicName, lastMessagePreview, topicId)`
- [ ] T088 [US7] 处理通知点击深链接：在 `Platforms/Android/MainActivity.cs` `OnNewIntent` 和 `Platforms/iOS/AppDelegate.cs` 中解析通知点击携带的 `topicId`，存入全局静态字段；`Routes.razor` `OnInitializedAsync` 检测后设置 `AppStateService.CurrentTopicId` 并 `NavigationManager.NavigateTo("/")`

**Checkpoint**: US7 完成 — 后台通知能力可验证。

---

## Phase 10: Polish & Cross-Cutting Concerns（打磨与横切关注点）

**Purpose**: 跨故事的代码质量、性能、安全性与可维护性提升。

- [ ] T089 [P] 完善 `AnyDrop.App/Components/Layout/MainLayout.razor` 离线横幅交互：`IConnectivityService` 恢复网络时自动隐藏横幅 + 触发 `ISignalRService.StartAsync()`（若处于 Disconnected 状态）；横幅动画过渡（CSS `transition: max-height 0.3s ease`）
- [ ] T090 [P] 完善 `AnyDrop.App/wwwroot/app.css`：iOS notch/Dynamic Island 兼容（`padding: env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)`）；底部导航栏 `padding-bottom: env(safe-area-inset-bottom)` 避免 Home Indicator 遮挡；`MessageList` 滚动容器 `overscroll-behavior: contain` 防止穿透
- [ ] T091 [P] 创建 `AnyDrop.Tests.Unit/AuthDelegatingHandlerTests.cs`：覆盖 Token 注入到 Authorization 头、Token 为 null 时不注入、401 响应触发 `AppEventBus.AuthExpired` 事件、非 401 响应不触发事件，通过 `DelegatingHandlerTestHelper` + Moq
- [ ] T092 [P] 全局错误处理：在 `MainLayout.razor` 或 Blazor `<ErrorBoundary>` 中捕获未处理异常，显示友好 Toast（"操作失败，请重试"），不暴露原始堆栈信息；为所有 `HttpClient` 调用添加统一 `try-catch(HttpRequestException)` → `ConnectivityService.IsOnline` 判断并显示离线或网络错误提示
- [ ] T093 实现 Tailwind CSS 生产构建：安装 Tailwind CLI，扫描 `AnyDrop.App/Components/**/*.razor` 生成 `wwwroot/app-production.css`；在 `wwwroot/index.html` 生产构建时替换 CDN `<script>` 为本地 `<link rel="stylesheet" href="app-production.css">`；更新 `.csproj` 构建脚本（`BeforeBuild` Target）
- [ ] T094 [P] 性能优化：在 `Components/Routes.razor` 为非首次导航页（Search、Settings）添加 Blazor 懒加载（`@using Microsoft.AspNetCore.Components.WebAssembly.Services` 或 MAUI 等效方式）；`MessageList.razor` 超过 200 条消息时启用 `Virtualize<ShareItemDto>` 组件替代直接渲染
- [ ] T095 [P] 为 `AnyDrop.App/Services/` 所有公共接口和实现类添加 XML doc 注释（`/// <summary>`），方便 IDE 智能提示
- [ ] T096 执行 `quickstart.md` 验证清单：① `dotnet build AnyDrop.App -f net10.0-android -c Debug` 0 error ② `dotnet test AnyDrop.Tests.Unit` 全部通过 ③ 在 Android 模拟器上运行 App，验证冷启动 → ServerSetupPage 显示 ④ 输入本地服务端地址 → 登录 → 发送消息 → SignalR 收到推送 ⑤ 所有 FR-001 至 FR-036 逐一核对完成状态
- [ ] T097 安全合规终审：确认 JWT 仅存 `SecureStorage`（grep `Preferences.Set` 不含 token 字样）；确认 `AuthDelegatingHandler` 无硬编码凭证；文件上传前 MIME 类型与大小在 `FileUploadService` 中校验（大小 > 0、非空 MIME）；`SecureStorage` 异常 try-catch 已覆盖（iOS Keychain 锁定场景）

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup
    └──→ Phase 2: Foundational  ⚠️ BLOCKS 所有用户故事
              ├──→ Phase 3: US1 (P1) 🎯 MVP
              │         └──→ Phase 4: US2 (P1) 🎯 MVP  ← 依赖 US1 认证已完成
              ├──→ Phase 5: US3 (P2)  ← 依赖 Phase 4 TopicService 预留接口
              ├──→ Phase 6: US4 (P2)  ← 独立（仅依赖 Foundation + Topics）
              ├──→ Phase 7: US5 (P2)  ← 独立（仅依赖 Foundation + AuthService）
              ├──→ Phase 8: US6 (P3)  ← 独立（依赖 Phase 4 FileUploadService）
              └──→ Phase 9: US7 (P3)  ← 依赖 Phase 4 SignalRService
                        └──→ Phase 10: Polish（依赖所有故事完成）
```

### User Story 依赖关系

| 用户故事 | 优先级 | 依赖 | 独立测试 |
|---------|--------|------|---------|
| **US1** 首次配置与登录 | P1 🎯 MVP | Phase 2 | ✅ 独立 |
| **US2** 消息收发 | P1 🎯 MVP | Phase 2 + US1（需认证） | ✅ 独立 |
| **US3** 主题管理 | P2 | Phase 2 + US2 的 AppStateService | ✅ 独立 |
| **US4** 搜索 | P2 | Phase 2（可与 US3 并行） | ✅ 独立 |
| **US5** 设置 | P2 | Phase 2 + US1（需 AuthService） | ✅ 独立 |
| **US6** 外部分享 | P3 | US2（FileUploadService） | ✅ 独立 |
| **US7** 本地通知 | P3 | US2（SignalRService） | ✅ 独立 |

### Phase 内依赖顺序

- 单元测试（[P] 标记）→ 并行创建，实现前必须先通过编译（预期失败）
- Models 文件（全部 [P]）→ 并行
- 接口 + 实现（[P] 标记的）→ 并行
- 注册 DI（单文件 MauiProgram.cs）→ 串行
- Blazor 页面组件 → 通常串行（依赖已注册服务）

---

## Parallel Execution Examples

### Phase 2 Foundational 并行示例

```bash
# 同时创建（互不依赖的不同文件）：
Task T009: Models/AuthModels.cs
Task T010: Models/TopicModels.cs
Task T011: Models/ShareItemModels.cs
Task T012: Models/SettingsModels.cs
Task T013: Models/ApiResponse.cs

# 完成 Models 后，同时创建基础服务：
Task T015: Services/ISecureTokenStorage.cs + SecureTokenStorage.cs
Task T016: Services/IServerConfigService.cs + ServerConfigService.cs
Task T017: Services/IConnectivityService.cs + ConnectivityService.cs
```

### Phase 3 (US1) 并行示例

```bash
# 单元测试和实现同时开始（测试先编译通过但 assert 失败）：
Task T029: AuthServiceTests.cs
Task T030: ServerConfigServiceTests.cs
Task T031: IAuthService.cs + AuthService.cs  （并行于测试文件创建）

# LoginPage 和 SetupAccountPage 互不依赖：
Task T034: LoginPage.razor
Task T035: SetupAccountPage.razor
```

### Phase 4 (US2) 并行示例

```bash
# 三组单元测试同时创建：
Task T038: SignalRServiceTests.cs
Task T039: ShareServiceTests.cs
Task T040: FileUploadServiceTests.cs

# 四个服务接口+实现同时创建：
Task T041: ISignalRService + SignalRService
Task T042: IShareService + ShareService
Task T043: IFileUploadService + FileUploadService
Task T044: IPickerService + PickerService

# MessageItem 和 ImagePreview 互不依赖：
Task T049: MessageItem.razor
Task T051: ImagePreview.razor
```

### Phase 5–7 P2 并行策略（双开发者）

```
Developer A: Phase 5 (US3 主题管理)
Developer B: Phase 6 (US4 搜索) 同步进行
Developer A 完成后: Phase 7 (US5 设置)
```

---

## Implementation Strategy

### MVP First（P1 用户故事，Phase 1–4）

1. ✅ 完成 **Phase 1**: Setup（骨架搭建，可编译）
2. ✅ 完成 **Phase 2**: Foundational（基础设施全部就绪）
3. ✅ 完成 **Phase 3**: US1 — 首次配置与登录
4. 🛑 **验证 MVP 检查点 1**: 新设备安装 → 配置服务端 → 登录 → 主界面可见
5. ✅ 完成 **Phase 4**: US2 — 消息收发 + SignalR 实时推送
6. 🛑 **验证 MVP 检查点 2**: 发送消息 → 出现列表 → 另一设备推送 → 本端实时收到
7. **🎉 MVP 完成，可演示/部署**

### Incremental Delivery（P2 用户故事叠加）

```
MVP (Phase 1-4) → 验证 → 演示
     ↓
添加 US3 (主题管理) → 独立测试 → 演示
     ↓
并行 US4 (搜索) + US5 (设置) → 各自独立测试
     ↓
全 P2 完成 → 集成测试
```

### Full Delivery（P3 扩展功能）

```
P2 全部完成
     ↓
US6 (外部分享) — Android + iOS 平台各自验证
     ↓
US7 (本地通知) — 后台推送验证
     ↓
Phase 10: Polish → 生产构建 → 正式发布
```

---

## Notes

- **[P]** 任务 = 不同文件、无未完成依赖，可多开发者/多智能体并行
- **[Story]** 标签与 spec.md 用户故事一一对应（US1–US7）
- 每个用户故事 Phase 可独立完成和验证，无需其他 P2/P3 故事完成
- **JWT 存储安全红线**：Token 只能写入 `SecureStorage`，任何任务不得将 Token 写入 `Preferences` 或普通文件
- **测试先行**：每个故事的 `[P]` 单元测试任务应先于实现创建，确保编译通过（预期 assert 失败），实现完成后全部变绿
- **DI 注册顺序**：各 Phase 在 `MauiProgram.cs` 追加注册，不覆盖已有注册；每次追加后执行 `dotnet build` 验证
- 切换主题/主题时的输入框草稿在本次会话内保留（`AppStateService.Messages` 内存态），不跨会话持久化
- 生产构建前必须将 Tailwind CDN 替换为预编译 CSS（T093），避免离线环境样式丢失
