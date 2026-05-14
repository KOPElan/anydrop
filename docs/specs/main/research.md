# Research: 核心基础设施与最小 MVP

**Phase**: 0 — Research & Unknowns Resolution  
**Date**: 2026-04-18 (更新: Tailwind CSS v4 替换 Fluent UI)  
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

## R-003: Tailwind CSS v4 + Blazor Server 集成

**Decision**: 使用 Tailwind CSS v4 作为唯一 UI 样式方案。通过独立 CLI（`@tailwindcss/cli`）构建 CSS；`wwwroot/app.css` 作为输入源，输出到 `wwwroot/tailwind.css`；在 `App.razor` 中通过 `<link>` 引用输出文件。开发阶段可使用 Tailwind v4 Play CDN 快速验证。

**Rationale**:
- Tailwind CSS v4 引入全新构建引擎（Oxide，Rust 实现），比 v3 快 3.5–10×；零配置文件（`tailwind.config.js` 被废弃），通过 CSS `@theme` 块在 `app.css` 内完成定制。
- `.NET Blazor` 使用静态文件服务（`wwwroot/`），Tailwind CLI 构建产物直接放置于此即可；不依赖 Webpack/Vite，符合最小化原则。
- Heroicons（内联 SVG）是 Tailwind 生态的标准图标方案，无需引入任何 JS 包，直接复制 SVG 代码到 Razor 组件。

**Alternatives considered**:
- Fluent UI for Blazor：已由宪法 v2.0.0 明确禁止。
- Tailwind CDN（Play CDN）生产使用：Play CDN 会生成运行时 JIT 样式表，体积大、无法缓存，仅适合开发/原型；生产环境必须使用构建链。
- Bootstrap：禁止，宪法明确排除。

**Tailwind v4 重要变更（相对 v3）**:
| 特性 | v3 | v4 |
|---|---|---|
| 配置文件 | `tailwind.config.js` | 废弃，改用 CSS `@theme {}` |
| CSS 入口 | `@tailwind base/components/utilities` | `@import "tailwindcss"` |
| CLI 包名 | `tailwindcss` (含 CLI) | `@tailwindcss/cli`（独立 CLI） |
| 内容扫描 | 需显式配置 `content: [...]` | 自动扫描，零配置 |
| Play CDN | `cdn.tailwindcss.com` | `@tailwindcss/browser@4` |

**集成方案（生产推荐）**:
```bash
# 安装 Tailwind CLI（全局或本地）
npm install -D @tailwindcss/cli

# 构建（一次性）
npx @tailwindcss/cli -i ./AnyDrop/wwwroot/app.css -o ./AnyDrop/wwwroot/tailwind.css

# 开发监听
npx @tailwindcss/cli -i ./AnyDrop/wwwroot/app.css -o ./AnyDrop/wwwroot/tailwind.css --watch
```

**app.css 结构**（输入源）:
```css
@import "tailwindcss";

@theme {
  /* 项目级 Design Token — 覆盖 Tailwind 默认值或新增自定义变量 */
  --color-brand: #6366f1;        /* 主色调：靛蓝 */
  --color-brand-hover: #4f46e5;
  --color-surface: #f9fafb;      /* 背景层 */
  --color-sidebar: #f3f4f6;
}

@layer base {
  html, body { @apply h-full antialiased; }
}

@layer components {
  .btn-primary {
    @apply bg-[--color-brand] hover:bg-[--color-brand-hover] text-white
           px-4 py-2 rounded-lg text-sm font-medium transition-colors;
  }
  .app-shell {
    @apply grid h-screen;
    grid-template-columns: 16rem 1fr;   /* 侧边栏 256px + 主内容区 */
  }
  @media (max-width: 768px) {
    .app-shell { grid-template-columns: 1fr; }
    .sidebar { display: none; }
  }
}
```

**App.razor 引用**:
```razor
<!-- 生产构建引用 -->
<link rel="stylesheet" href="tailwind.css" />

<!-- 开发调试时可临时替换为 Play CDN（不得用于生产） -->
<!-- <script src="https://cdn.jsdelivr.net/npm/@tailwindcss/browser@4"></script> -->
```

**MainLayout.razor 布局**:
```razor
<div class="app-shell">
    <aside class="sidebar bg-[--color-sidebar] border-r border-gray-200 overflow-y-auto">
        <!-- 导航区域（MVP 静态占位） -->
        <div class="p-4 text-sm font-semibold text-gray-600">AnyDrop</div>
    </aside>
    <main class="flex flex-col overflow-hidden bg-[--color-surface]">
        @Body
    </main>
</div>
```

---

## R-004: 文件存储服务抽象

**Decision**: MVP 阶段仅定义 `IFileStorageService` 接口和 `LocalFileStorageService` 空实现（方法体抛 `NotImplementedException`），不暴露文件上传 UI。文件存储根路径从 `Storage:BasePath` 配置读取。

**Rationale**:
- 接口先行（Interface-first）保证后续文件上传功能可直接实现而无需修改服务层契约。
- 空实现用 `NotImplementedException` 便于测试时通过 Moq 验证调用链但不需要真实文件系统。

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
| R-003 | UI 布局骨架框架选型 | **Tailwind CSS v4**（替换 Fluent UI）；CLI 构建链；`@theme` 定制；Heroicons 内联 SVG | ✅ Resolved |
| R-004 | 文件存储服务 MVP 范围 | 仅定义接口 + 空实现（`NotImplementedException`），文件 UI 延迟 | ✅ Resolved |
| —— | Minimal API / OpenAPI | 本版本不实现，延迟至后续 Feature | ⏭ Deferred |