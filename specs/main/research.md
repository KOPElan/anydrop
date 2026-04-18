# Research: 核心基础设施与最小 MVP

**Phase**: 0 — Research & Unknowns Resolution  
**Date**: 2026-04-18  
**Feature**: feat/001-core-infra-mvp

---

## R-001: EF Core + SQLite 配置（.NET 10）

**Decision**: 使用 `Microsoft.EntityFrameworkCore.Sqlite`（10.x），通过 `UseSqlite(connectionString)` 配置 `AnyDropDbContext`；连接字符串格式 `Data Source=<path>/anydrop.db`；路径从 `IConfiguration["Storage:DatabasePath"]` 读取。

**Rationale**:
- EF Core 10.x 与 .NET 10 同步发布，LTS 支持期内；SQLite 适合单实例私有部署，无需额外服务进程。
- `AddDbContext<AnyDropDbContext>` 使用 `ServiceLifetime.Scoped`，与 Blazor Server 的每连接 Scoped 生命周期契合，避免跨连接的 DbContext 共享（线程安全问题）。
- Migration 通过 `dotnet ef migrations add InitialCreate` 生成，启动时调用 `db.Database.MigrateAsync()` 确保数据库自动升级。

**Alternatives considered**:
- Dapper：需手写 SQL，违反宪法"禁止裸 SQL 拼接"精神；不支持 Migration。
- InMemory Provider：仅用于测试隔离，生产环境不持久化。

**Key implementation notes**:
```csharp
// Program.cs
var dbPath = builder.Configuration["Storage:DatabasePath"] ?? "data/anydrop.db";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
builder.Services.AddDbContext<AnyDropDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
// 启动时迁移
using var scope = app.Services.CreateScope();
await scope.ServiceProvider.GetRequiredService<AnyDropDbContext>().Database.MigrateAsync();
```

---

## R-002: SignalR Hub 广播模式

**Decision**: 在 `Services/ShareService.cs` 中注入 `IHubContext<ShareHub>`，消息持久化后调用 `_hubContext.Clients.All.SendAsync("ReceiveShareItem", dto)` 广播；Hub 类 `ShareHub.cs` 仅继承 `Hub`，不含任何业务逻辑。

**Rationale**:
- Blazor Server 内置 SignalR，无需额外 NuGet 包；`IHubContext<T>` 是线程安全的 Singleton，可在 Scoped 服务中安全注入。
- "All clients" 广播符合 MVP 场景（所有设备共享同一内容流）；后续可按 Group 细化。
- Hub 零业务逻辑确保符合宪法 Principle I。

**Alternatives considered**:
- 在 Razor 组件直接调用 JS `HubConnection`：复杂度高，Blazor Server 已内置 SignalR 连接，不需要客户端 JS Hub。
- 在 Hub 内部写数据库：违反宪法 Principle I，否决。

**Key implementation notes**:
```csharp
// Hubs/ShareHub.cs — 故意保持空，仅作命名标识
public class ShareHub : Hub { }

// Services/ShareService.cs
public class ShareService(AnyDropDbContext db, IHubContext<ShareHub> hub) : IShareService
{
    public async Task<ShareItemDto> SendTextAsync(string content)
    {
        var item = new ShareItem { Content = content, ContentType = ShareContentType.Text, CreatedAt = DateTimeOffset.UtcNow };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();
        var dto = item.ToDto();
        await hub.Clients.All.SendAsync("ReceiveShareItem", dto);
        return dto;
    }
}
```

**SignalR Registration**:
```csharp
builder.Services.AddSignalR();
app.MapHub<ShareHub>("/hubs/share");
```

---

## R-003: Fluent UI Blazor 布局骨架

**Decision**: `MainLayout.razor` 使用 CSS Grid 实现"侧边栏 + 主内容"两列布局，所有颜色通过 Fluent Design Tokens（CSS 变量）定义；移动端通过 CSS media query 隐藏侧边栏。

**Rationale**:
- Fluent UI 未提供专用"App Shell"布局组件；最可靠方案是 CSS Grid + Fluent Design Token 颜色变量，符合宪法"禁止硬编码颜色"要求。
- `<FluentToastProvider />`、`<FluentDialogProvider />` 等 Provider 组件 **MUST** 在 MainLayout 中声明，否则后续功能中调用相应服务时会静默失败（fluentui-blazor skill 关键规则 #2）。

**Key implementation notes**:
```razor
<!-- MainLayout.razor -->
<div class="app-shell">
    <aside class="sidebar">
        <!-- 导航区域（MVP 阶段静态占位） -->
        <FluentNavMenu>
            <FluentNavLink Icon="@(new Icons.Regular.Size20.Home())" Href="/">全部内容</FluentNavLink>
        </FluentNavMenu>
    </aside>
    <main class="main-content">
        @Body
    </main>
</div>
<FluentToastProvider />
<FluentDialogProvider />
```

```css
/* MainLayout.razor.css */
.app-shell {
    display: grid;
    grid-template-columns: 240px 1fr;
    height: 100vh;
    background-color: var(--neutral-layer-1);
}
.sidebar {
    border-right: 1px solid var(--neutral-stroke-layer-rest);
    overflow-y: auto;
    background-color: var(--neutral-layer-2);
}
.main-content {
    overflow: auto;
    display: flex;
    flex-direction: column;
}
@media (max-width: 768px) {
    .app-shell { grid-template-columns: 1fr; }
    .sidebar { display: none; }
}
```

---

## R-004: Minimal API + OpenAPI 集成（.NET 10）

**Decision**: 使用 .NET 10 内置的 `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` 提供 Swagger UI；端点统一收敛至 `Api/ShareItemEndpoints.cs` 中的扩展方法。

**Rationale**:
- .NET 9+ 的 `app.MapOpenApi()` 无需 Swashbuckle，原生生成 OpenAPI 3.1 规范；Scalar 提供现代化 UI（替代 Swagger UI）。
- 符合宪法 Principle VII：端点在 `Api/` 目录、路径 `/api/v1/`、复用 IShareService、不含业务逻辑。

**Packages needed**:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.*" />
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.*" />
<PackageReference Include="Scalar.AspNetCore" Version="2.*" />
```

**Key implementation notes**:
```csharp
// Program.cs
builder.Services.AddOpenApi();
// ...
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.MapShareItemEndpoints();
```

---

## R-005: 文件存储服务抽象

**Decision**: MVP 阶段仅定义 `IFileStorageService` 接口和 `LocalFileStorageService` 空实现（方法体抛 `NotImplementedException`），不暴露文件上传 UI。文件存储根路径从 `Storage:BasePath` 配置读取。

**Rationale**:
- 接口先行（Interface-first）保证后续文件上传功能可直接实现而无需修改服务层契约。
- 空实现用 `NotImplementedException` 而非 `throw new NotImplementedException()`，以便测试时通过 Moq 验证调用链但不需要真实文件系统。
- 注册为 Singleton 可选；考虑到文件 I/O 和配置读取，推荐 `Scoped`，与 ShareService 保持一致。

**Interface**:
```csharp
public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default);
    Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default);
    Task DeleteFileAsync(string storagePath, CancellationToken ct = default);
}
```

---

## Resolution Summary

| ID | Unknown | Resolution | Status |
|----|---------|------------|--------|
| R-001 | EF Core 版本与 SQLite 配置方式 | `Microsoft.EntityFrameworkCore.Sqlite` 10.x，连接字符串通过配置注入，启动时自动迁移 | ✅ Resolved |
| R-002 | SignalR 广播在服务层的实现方式 | 通过 `IHubContext<ShareHub>` 注入到 `ShareService`，Hub 类为空标识符 | ✅ Resolved |
| R-003 | Fluent UI 布局骨架与 Provider 要求 | CSS Grid + Design Token 变量 + MainLayout 中声明 Provider | ✅ Resolved |
| R-004 | OpenAPI/Swagger 集成方案 | `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore`，开发环境暴露 | ✅ Resolved |
| R-005 | 文件存储服务 MVP 范围 | 仅定义接口 + 空实现（`NotImplementedException`），文件 UI 延迟 | ✅ Resolved |