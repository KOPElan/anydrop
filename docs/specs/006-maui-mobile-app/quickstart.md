# Quickstart: AnyDrop 移动端 App（MAUI Blazor Hybrid）

**Feature**: `006-maui-mobile-app`  
**Date**: 2026-04-29

---

## 前提条件

| 工具 | 版本要求 | 安装方式 |
|------|---------|---------|
| .NET SDK | 10.0 | `winget install Microsoft.DotNet.SDK.10` / dotnet.microsoft.com |
| MAUI Workload | 随 .NET 10 | `dotnet workload install maui` |
| Android SDK | API 35 (推荐) / API 24 (最低) | Android Studio / VS 2022 / VS Code + MAUI 扩展 |
| Xcode | 16+ | macOS 专用，iOS 构建需要 |
| AnyDrop 服务端 | 已部署并可访问 | `docker-compose up -d`（参见服务端 README） |

---

## 克隆并构建

```bash
# 克隆仓库（若尚未克隆）
git clone https://github.com/KOPElan/anydrop.git
cd anydrop
git checkout 006-maui-mobile-app

# 还原依赖（首次需 MAUI Workload 已安装）
dotnet restore AnyDrop.App/AnyDrop.App.csproj

# 构建 Android 版本（验证无编译错误）
dotnet build AnyDrop.App/AnyDrop.App.csproj \
    -f net10.0-android \
    -c Debug

# 构建 iOS 版本（需 macOS + Xcode）
dotnet build AnyDrop.App/AnyDrop.App.csproj \
    -f net10.0-ios \
    -c Debug
```

---

## 运行（调试）

### Android 模拟器

```bash
# 列出可用模拟器
dotnet build AnyDrop.App/AnyDrop.App.csproj -f net10.0-android -t:Run \
    /p:AndroidSdkDirectory=$ANDROID_HOME
```

或在 Visual Studio / Rider 中：
1. 设置启动项目为 `AnyDrop.App`
2. 选择目标框架 `net10.0-android`
3. 选择模拟器或真机，点击运行（▶）

### iOS 模拟器（macOS）

```bash
dotnet build AnyDrop.App/AnyDrop.App.csproj -f net10.0-ios -t:Run \
    /p:_DeviceName=:v2:udid=<SIMULATOR_UDID>
```

---

## 首次配置（App 内）

1. 启动 App → 自动进入**服务端配置页**（`/setup`）
2. 输入服务端地址，例如：
   - 本地 Docker：`http://192.168.1.100:5002`（使用局域网 IP，不能用 `localhost`）
   - 外网部署：`https://your-anydrop.example.com`
3. 点击**连接**，App 验证地址可达性后保存至 `Preferences`
4. 根据服务端状态自动跳转：
   - 首次部署（`requiresSetup: true`）→ **账号初始化页** `/setup-account`
   - 已有账号 → **登录页** `/login`
5. 完成登录后进入主界面，SignalR 自动建立连接

---

## 项目结构一览

```
AnyDrop.App/
├── MauiProgram.cs          # DI 注册入口（HttpClient, Services, SignalR）
├── App.xaml.cs             # 启动路由决策（Token 检查 → GoToAsync）
├── Models/                 # 客户端 DTO（镜像服务端 Models/）
├── Services/               # 业务逻辑服务（均有 I 前缀接口）
├── Infrastructure/         # 横切关注点（AuthHandler, EventBus, HubManager）
├── Components/
│   ├── Pages/              # Blazor 路由页面（@page 指令）
│   └── Layout/             # 布局组件（导航栏、认证守卫、离线横幅）
├── Platforms/
│   ├── Android/            # AndroidManifest.xml, MainActivity.cs
│   └── iOS/                # Info.plist, AppDelegate.cs
└── wwwroot/
    ├── index.html          # BlazorWebView HTML shell + Tailwind CDN
    └── app.css             # 自定义 CSS
```

---

## 关键 DI 注册（MauiProgram.cs 概览）

```csharp
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
               .ConfigureFonts(/* ... */);

        builder.Services.AddMauiBlazorWebView();

        // ── 基础设施 ──────────────────────────────────
        builder.Services.AddSingleton<AppEventBus>();
        builder.Services.AddSingleton<ISecureTokenStorage, SecureTokenStorage>();
        builder.Services.AddSingleton<IServerConfigService, ServerConfigService>();
        builder.Services.AddSingleton<IConnectivityService, ConnectivityService>();

        // ── HTTP Client ───────────────────────────────
        builder.Services.AddTransient<AuthDelegatingHandler>();
        builder.Services.AddHttpClient("api", (sp, c) =>
        {
            c.BaseAddress = new Uri(sp.GetRequiredService<IServerConfigService>().GetBaseUrl());
        }).AddHttpMessageHandler<AuthDelegatingHandler>();

        // ── SignalR ───────────────────────────────────
        builder.Services.AddSingleton<HubConnectionManager>();
        builder.Services.AddSingleton<ISignalRService, SignalRService>();

        // ── 业务服务（均为 Scoped，每次 BlazorWebView 渲染周期一个实例）───
        builder.Services.AddScoped<IAppStateService, AppStateService>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<ITopicService, TopicService>();
        builder.Services.AddScoped<IShareService, ShareService>();
        builder.Services.AddScoped<IFileUploadService, FileUploadService>();
        builder.Services.AddScoped<ISearchService, SearchService>();
        builder.Services.AddScoped<ISettingsService, SettingsService>();

        // ── 平台 API ──────────────────────────────────
        builder.Services.AddSingleton<IPickerService, PickerService>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();

        // ── 本地通知 ──────────────────────────────────
        builder.UseLocalNotification();

        return builder.Build();
    }
}
```

---

## 页面导航路由表

| URL | 组件 | 认证要求 | 描述 |
|-----|------|---------|------|
| `/setup` | `ServerSetupPage.razor` | ❌ | 服务端 URL 配置 |
| `/login` | `LoginPage.razor` | ❌ | 密码登录 |
| `/setup-account` | `SetupAccountPage.razor` | ❌ | 首次账号创建 |
| `/` | `HomePage.razor` | ✅ | 主聊天界面 |
| `/search` | `SearchPage.razor` | ✅ | 消息搜索 |
| `/settings` | `SettingsPage.razor` | ✅ | 应用设置 |
| `/image-preview/{id}` | `ImagePreview.razor` | ✅ | 全屏图片预览 |
| `/share` | `ExternalSharePage.razor` | ✅ | 外部分享接收 |

---

## 单元测试运行

```bash
# 运行所有单元测试
dotnet test AnyDrop.Tests.Unit/AnyDrop.Tests.Unit.csproj

# 运行指定测试类
dotnet test AnyDrop.Tests.Unit/ --filter "FullyQualifiedName~AuthServiceTests"
```

**测试覆盖范围**:
- `AuthService`：登录/登出/Token 存储/过期检查
- `TopicService`：CRUD、排序、置顶/归档
- `ShareService`：消息分页、发送文本、下载
- `SearchService`：关键词/日期/类型搜索
- `AuthDelegatingHandler`：Token 注入、401 处理、事件触发

---

## 常见问题

### Q: Android 无法访问局域网服务端

**A**: `android:usesCleartextTraffic="true"` 需在 `AndroidManifest.xml` 中设置（仅用于 HTTP，HTTPS 不需要）。生产环境使用 HTTPS 可移除此配置。

### Q: iOS 相册权限被拒绝

**A**: 确认 `Info.plist` 中有 `NSPhotoLibraryUsageDescription` 字符串。真机首次使用时需运行时授权。

### Q: Tailwind 样式在离线时不加载

**A**: 开发阶段使用 Play CDN 正常，生产打包时应用 Tailwind CLI 预编译 CSS 并打包至 `wwwroot/app.css`，移除 CDN 引用。

### Q: SignalR 连接一直处于 Reconnecting 状态

**A**: 检查 `Preferences["anydrop_base_url"]` 是否正确，确认服务端 `/hubs/share` 路由可达。可在 DevTools（Android Debug Bridge + Chrome）中查看 WebSocket 握手日志。

### Q: Android 分享图片后 App 崩溃

**A**: 确认在 `OnNewIntent` 中使用 `ContentResolver.OpenInputStream(uri)` 读取内容，并 copy 到 App cache 目录（`CacheDir`），不直接持有 `content://` URI。

---

## 参考资料

- 服务端 API 文档：`{BaseUrl}/swagger`（开发环境自动开放）
- API 契约：`specs/006-maui-mobile-app/contracts/api-endpoints.md`
- SignalR 事件契约：`specs/006-maui-mobile-app/contracts/signalr-events.md`
- 数据模型：`specs/006-maui-mobile-app/data-model.md`
- MAUI Blazor Hybrid 官方文档：https://learn.microsoft.com/dotnet/maui/user-interface/controls/blazorwebview
