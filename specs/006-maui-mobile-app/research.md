# Research: AnyDrop 移动端 App（MAUI Blazor Hybrid）

**Feature**: `006-maui-mobile-app`  
**Phase**: 0 — Outline & Research  
**Date**: 2026-04-29

---

## R-001: SignalR Client 在 MAUI Blazor Hybrid 中的集成模式

**Decision**: 使用 `Microsoft.AspNetCore.SignalR.Client` 10.0.x，注册为 **Singleton** `HubConnectionManager`，连接生命周期由服务管理，JWT 通过 `AccessTokenProvider` 异步注入。

**Rationale**:
- SignalR 客户端与 Blazor 组件无关，适合单例生命周期（全局一条连接）
- 连接需在用户登录后、导航至主界面前启动，不可在 `MauiProgram.cs` 中提前建立（此时无 Token）
- `WithAutomaticReconnect(IRetryPolicy)` 支持指数退避，避免在网络不稳时频繁重试

**Key Implementation Pattern**:
```csharp
// Infrastructure/HubConnectionManager.cs
public sealed class HubConnectionManager : IAsyncDisposable
{
    public HubConnection Connection { get; }

    public HubConnectionManager(ISecureTokenStorage tokenStorage, IServerConfigService config)
    {
        Connection = new HubConnectionBuilder()
            .WithUrl(config.GetHubUrl(), options =>
            {
                options.AccessTokenProvider = tokenStorage.GetTokenAsync;
            })
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .Build();
    }
}

// ExponentialBackoffRetryPolicy: 0ms → 2s → 10s → 30s → 30s (cap)
```

**Alternatives Considered**:
- MauiWebView 内的 JavaScript SignalR Client：不适用，MAUI Blazor Hybrid 的 WebView 不直接暴露 JS signalR 对象给 .NET
- Scoped 生命周期：会导致每个 Blazor 渲染周期建立新连接，不可行

**Pitfalls**:
- Hub URL 依赖 Preferences 中的 BaseUrl，连接对象在 BaseUrl 变更后需重建（需处理 DisposeAsync + 重新注册）
- 重连时 Token 可能已过期，`AccessTokenProvider` 每次重连都会重新调用，确保提供最新 Token

---

## R-002: JWT Token 安全存储（SecureStorage）

**Decision**: 封装 `ISecureTokenStorage` 接口，底层使用 MAUI `SecureStorage`（Android Keystore / iOS Keychain），Token 和过期时间分别以独立键值存储，读写受 `SemaphoreSlim` 保护。

**Rationale**:
- `SecureStorage` 是 MAUI 对平台 Keychain/Keystore 的统一抽象，满足 FR-004（不得明文写入普通文件或 Preferences）
- 客户端侧过期检查（提前 30 秒判定为过期）可减少无效 API 请求
- 接口抽象利于单元测试（Mock 注入）

**Key Implementation Pattern**:
```csharp
public interface ISecureTokenStorage
{
    Task SaveTokenAsync(string token, DateTimeOffset expiresAt);
    Task<string?> GetTokenAsync();                 // returns null if expired or absent
    Task<bool> IsAuthenticatedAsync();
    Task ClearTokenAsync();
}
```

**Alternatives Considered**:
- `Preferences.Set`：不加密，明文存储，违反 FR-004
- 内存存储：App 切换到后台被系统终止后 Token 丢失，需重新登录

**Pitfalls**:
- iOS Keychain 在设备锁定时可能返回异常，需 try-catch 并 fallback 到重新登录
- 卸载 App 后 iOS Keychain 内容被清除，Android 不一定清除（行为差异）

---

## R-003: IHttpClientFactory + JWT DelegatingHandler

**Decision**: 在 `MauiProgram.cs` 注册命名 HttpClient `"api"`，挂载 `AuthDelegatingHandler`（Transient），Handler 从 `ISecureTokenStorage` 获取 Token 注入 `Authorization: Bearer` 头；收到 401 时通过 `AppEventBus` 触发 `AuthExpiredEvent`，Blazor 根布局订阅事件后导航至 `/login`。

**Rationale**:
- DelegatingHandler 是横切关注点的标准解法，避免每个 Service 手动添加 Header
- Handler 不直接引用 `NavigationManager`（属于 Blazor Scoped，Handler 是 Transient），通过事件总线解耦
- `IHttpClientFactory` 管理连接池，防止 socket 耗尽

**Key Implementation Pattern**:
```csharp
// MauiProgram.cs
builder.Services.AddTransient<AuthDelegatingHandler>();
builder.Services.AddHttpClient("api", (sp, c) =>
{
    var cfg = sp.GetRequiredService<IServerConfigService>();
    c.BaseAddress = new Uri(cfg.GetBaseUrl());
    c.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<AuthDelegatingHandler>();

// Infrastructure/AuthDelegatingHandler.cs
protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken ct)
{
    var token = await _tokenStorage.GetTokenAsync();
    if (token is not null)
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    
    var response = await base.SendAsync(request, ct);
    
    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
        await _tokenStorage.ClearTokenAsync();
        _eventBus.RaiseAuthExpired();
    }
    return response;
}
```

**Alternatives Considered**:
- Typed HttpClient：减少"api"字符串耦合，但 BaseUrl 动态变更时需额外处理
- 每次请求手动获取 Token：代码重复，易遗漏

**Pitfalls**:
- BaseUrl 在运行时修改（设置页更换服务端地址）后 `HttpClient.BaseAddress` 不变，需重建 Client 或改用完整 URL
- `AuthDelegatingHandler` MUST 为 Transient，防止 HttpClient 生命周期问题

---

## R-004: Android 系统分享（Share Intent）

**Decision**: 在 `AndroidManifest.xml` 声明 `ACTION_SEND` / `ACTION_SEND_MULTIPLE` Intent Filter；在 `MainActivity.OnNewIntent` 中解析分享内容，存入静态 `PendingSharedContent`；登录后的 Blazor `Routes.razor` 在 `OnInitializedAsync` 中检测并跳转至分享界面。

**Key AndroidManifest.xml 声明**:
```xml
<activity android:name=".MainActivity" android:exported="true"
          android:launchMode="singleTask">
    <!-- 文字分享 -->
    <intent-filter>
        <action android:name="android.intent.action.SEND" />
        <category android:name="android.intent.category.DEFAULT" />
        <data android:mimeType="text/*" />
    </intent-filter>
    <!-- 文件/图片/视频分享 -->
    <intent-filter>
        <action android:name="android.intent.action.SEND" />
        <action android:name="android.intent.action.SEND_MULTIPLE" />
        <category android:name="android.intent.category.DEFAULT" />
        <data android:mimeType="image/*" />
        <data android:mimeType="video/*" />
        <data android:mimeType="application/*" />
    </intent-filter>
</activity>
```

**Pitfalls**:
- Android 11+ 需使用 `ContentResolver` 读取 `content://` URI，不能直接用文件路径
- 大文件必须 copy 到 App cache 目录，不可长时间持有 URI（权限可能被系统撤销）
- `launchMode="singleTask"` 确保分享时复用已有实例，触发 `OnNewIntent` 而非创建新 Activity

---

## R-005: iOS 文件类型注册（Info.plist）

**Decision**: 在 `Info.plist` 中注册 `CFBundleDocumentTypes`（接收公开文件类型），对应 iOS Share Sheet 的 "Open in AnyDrop" 选项；通过 `AppDelegate.OpenUrl` 处理传入的文件 URL。

**Pitfalls**:
- iOS Share Extension（独立进程）超出本期范围（P3）；本期仅实现 "Open In" 方式（App Extension 作为增强项）
- Photo Library 访问需 `NSPhotoLibraryUsageDescription` 描述字符串

---

## R-006: 本地推送通知（P3 功能）

**Decision**: 使用 `Plugin.LocalNotification`（v10.1.x），在登录成功后请求 `Permissions.PostNotifications`，当 SignalR 收到新消息且 App 处于后台（`AppState.Background`）时触发本地通知。

**Rationale**:
- 本地通知（非 APNs/FCM 远程推送）符合自托管场景，不需要推送服务器
- `Plugin.LocalNotification` 同时支持 Android NotificationCompat 和 iOS UNUserNotificationCenter

**Pitfalls**:
- Android 13+（API 33+）需运行时请求 `POST_NOTIFICATIONS` 权限
- 当 App 完全被 kill（非 background）时，SignalR 连接断开，无法收到实时消息；本期 scope 仅覆盖 App 在后台运行的场景
- 如无通知权限，静默失败，不重复请求（符合 spec 假设）

---

## R-007: Tailwind CSS 在 BlazorWebView 中的集成

**Decision**: 使用 **Tailwind CSS Play CDN**（`cdn.tailwindcss.com`）注入 `wwwroot/index.html`，配置 `darkMode: 'class'`；通过 JS `ThemeManager` 对象在 `<html>` 元素上切换 `dark` class；深色/浅色偏好存储于 `Preferences`。

**Rationale**:
- Play CDN 无需 npm/PostCSS 构建链，适合 MAUI 项目（不含 Node.js 工具链）
- 生产环境可通过 Tailwind CLI 预编译 + 静态 CSS 文件替换 CDN（体积优化）
- Class-based dark mode（而非 `prefers-color-scheme`）完全由 App 控制，符合 spec（手动切换）

**Pitfalls**:
- Play CDN 不支持任意值（`w-[500px]`），只能使用预设 scale
- MAUI App 中 BlazorWebView 需要网络请求 CDN；离线时样式加载失败 → 生产应打包本地 CSS
- 安全内容区域（notch）需手动加 `env(safe-area-inset-*)` padding

---

## R-008: 文件上传（MAUI FilePicker + multipart 流式上传）

**Decision**: 封装 `IPickerService`（`FilePicker.PickAsync` / `MediaPicker.PickPhotoAsync`），通过 `ProgressStream` 包装文件流上传，在 UI 组件中通过 `IProgress<double>` 更新进度条。

**Pitfalls**:
- 文件选择必须在 UI 线程调用（Blazor 事件处理器天然满足）
- 大文件（>50MB）必须流式上传，不得先读入 `MemoryStream`
- Android 需 READ_MEDIA_* 权限（API 33+），iOS 需 Photo Library 权限

---

## R-009: 启动路由决策（条件导航）

**Decision**: 在 `App.xaml.cs` `OnStart()` 中异步检查 `ISecureTokenStorage.IsAuthenticatedAsync()`，根据结果调用 `Shell.Current.GoToAsync()`（`"//setup"` / `"//login"` / `"//home"`）；服务端 setup-status 检查在 `ServerSetupPage.razor` 或 `LoginPage.razor` 的 `OnInitializedAsync` 中完成（避免 MAUI 启动时额外 HTTP 请求）。

**Rationale**:
- 启动路由只需检查本地 Token，不需要网络请求
- 服务端状态（requiresSetup）需网络访问，交由 Blazor 页面 `OnInitializedAsync` 处理，可显示加载状态

---

## R-010: 图片全屏预览 Pinch-to-Zoom

**Decision**: `<meta name="viewport" content="..., user-scalable=yes">` + CSS `touch-action: pinch-zoom` 即可在 MAUI WebView（基于 Chromium Android / WebKit iOS）中实现原生 pinch-to-zoom；全屏 overlay 通过 Blazor 组件 + `position: fixed; inset: 0` 实现。

**Alternatives Considered**:
- Hammer.js：额外 9KB，仅在 CSS 方案不满足时作为 fallback
- MAUI 原生 Image 控件：无法与 Blazor 组件树集成，放弃

---

## 技术决策汇总

| 关注点 | 决策 | NuGet / 方案 |
|--------|------|-------------|
| SignalR 实时推送 | `HubConnectionManager` Singleton + 指数退避 | `Microsoft.AspNetCore.SignalR.Client` 10.0.x |
| JWT 安全存储 | `SecureTokenStorage` 封装 `SecureStorage` | MAUI 内置 |
| HTTP 客户端 | Named HttpClient + `AuthDelegatingHandler` | `Microsoft.Extensions.Http` |
| 跨层通信 | `AppEventBus` Singleton（`Action<T>` 委托） | 无第三方依赖 |
| Android 系统分享 | Intent Filter + `OnNewIntent` | 平台原生 |
| iOS 文件接收 | CFBundleDocumentTypes + AppDelegate | 平台原生 |
| 本地通知 | `Plugin.LocalNotification` | v10.1.x |
| UI 样式 | Tailwind CSS Play CDN + dark class | CDN / 生产可换 CLI |
| 文件选择 | `IPickerService` → MAUI FilePicker | MAUI 内置 |
| 启动路由 | App.xaml.cs + Shell.GoToAsync | MAUI Shell |
| 图片预览缩放 | CSS `touch-action: pinch-zoom` + viewport meta | 无额外依赖 |
| 离线检测 | `Connectivity.NetworkAccess` → `AppEventBus` | MAUI 内置 |
