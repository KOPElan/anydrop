# Implementation Plan: 核心基础设施与最小 MVP

**Branch**: `feat/001-core-infra-mvp` | **Date**: 2026-04-18 | **Spec**: [specs/001-core-infra-mvp/spec.md](../001-core-infra-mvp/spec.md)  
**Input**: Feature specification from `/specs/001-core-infra-mvp/spec.md`

---

## Summary

构建 AnyDrop 的核心基础设施层，实现最小 MVP：EF Core + SQLite 数据持久化、SignalR 实时广播、Tailwind CSS v4 UI 骨架（侧边栏 + 主聊天区域）、两端实时文本共享。技术决策核心：Tailwind CSS v4（取代 Fluent UI）+ EF Core 10 + Blazor Server Interactive + SignalR。

---

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: Blazor Server (Interactive Server), EF Core 10.x + SQLite, SignalR (built-in), Tailwind CSS v4 (CLI build)  
**Storage**: SQLite via EF Core；文件路径从 `Storage:DatabasePath` 配置读取，默认 `./data/anydrop.db`  
**Testing**: xUnit + FluentAssertions + Moq（单元）；Playwright（E2E）；EF Core InMemory / SQLite InMemory（测试隔离）  
**Target Platform**: Linux (amd64)，Kestrel HTTP，容器化部署；开发: `http://localhost:5002`  
**Project Type**: Blazor Server 单体 Web 应用  
**Performance Goals**: 文本广播延迟 < 1s（局域网）；50 条历史消息加载 < 3s  
**Constraints**: 无认证（MVP 阶段）；无 Minimal API；无 OpenAPI（延迟至后续版本）；无 Fluent UI 组件  
**Scale/Scope**: 单实例私有部署；同时在线设备 < 10 台

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Verify the following gates against `.specify/memory/constitution.md` (AnyDrop v2.0.0):

- [x] **I. 单体架构分离**：服务层实现于 `Services/`，Razor 组件仅调用 DI 注入的服务，无内联业务逻辑
- [x] **II. 技术栈合规**：仅使用 .NET 10 + Blazor Server + Tailwind CSS v4 + SQLite/EF Core + SignalR；无 Fluent UI；无 Bootstrap
- [x] **III. 命名规范**：所有新增异步方法以 `Async` 结尾，接口以 `I` 开头，PascalCase 一致
- [x] **IV. 测试覆盖**：Service 层方法有 xUnit 单元测试，SignalR 分发逻辑有 Moq 验证，E2E 链路有 Playwright 用例
- [x] **V. 安全合规**：无硬编码凭证，路径通过配置注入
- [x] **VI. 容器化**：持久化数据通过 Volume 挂载，配置通过环境变量注入
- [x] **VII. RESTful API**：本版本不实现（已 Deferred），Constitution Check 通过

*Post-design re-check*: 所有 Phase 1 设计产物（data-model.md、布局结构）均符合上述约束。✅

---

## Project Structure

### Documentation (this feature)

```text
specs/main/
├── plan.md              # 本文件
├── research.md          # Phase 0 研究报告
├── data-model.md        # Phase 1 数据模型
├── quickstart.md        # Phase 1 快速启动指南
└── tasks.md             # Phase 2 任务列表（/speckit.tasks 输出）
```

### Source Code (repository root)

```text
AnyDrop/
├── Components/
│   ├── Pages/
│   │   ├── Home.razor            # 聊天主界面（消息列表 + 输入框）
│   │   ├── Home.razor.cs         # code-behind（IAsyncDisposable）
│   │   └── Home.razor.css        # 页面级局部样式（可选）
│   ├── Layout/
│   │   ├── MainLayout.razor      # 应用骨架（侧边栏 + 主内容区）
│   │   ├── MainLayout.razor.css  # 布局样式（app-shell grid）
│   │   ├── ReconnectModal.razor  # 已存在
│   │   └── ReconnectModal.razor.css  # 已存在
│   ├── App.razor                 # 引入 tailwind.css
│   └── _Imports.razor            # 全局 using（移除 FluentUI，保留 JSInterop/AnyDrop.*）
├── Data/
│   └── AnyDropDbContext.cs       # EF Core DbContext
├── Hubs/
│   └── ShareHub.cs               # SignalR Hub（空类）
├── Models/
│   ├── ShareContentType.cs       # 枚举
│   ├── ShareItem.cs              # EF Core 实体
│   └── ShareItemDto.cs           # DTO (sealed record)
├── Services/
│   ├── IShareService.cs
│   ├── ShareService.cs
│   ├── IFileStorageService.cs
│   └── LocalFileStorageService.cs  # 空实现（NotImplementedException）
├── wwwroot/
│   ├── app.css                   # Tailwind v4 输入源（@import "tailwindcss" + @theme）
│   └── tailwind.css              # CLI 构建产物（gitignore 可选；publish 时包含）
├── appsettings.json
├── appsettings.Development.json
└── Program.cs                    # DI 注册、中间件管道、MigrateAsync

AnyDrop.Tests.Unit/
├── Services/
│   ├── ShareServiceTests.cs
│   └── LocalFileStorageServiceTests.cs
└── AnyDrop.Tests.Unit.csproj

AnyDrop.Tests.E2E/
├── Tests/
│   └── ShareFlowTests.cs         # Playwright 端到端
└── AnyDrop.Tests.E2E.csproj
```

---

## Phase 0: Research Summary

所有 NEEDS CLARIFICATION 已通过 research.md 解决：

| ID | 主题 | 结论 |
|----|------|------|
| R-001 | EF Core + SQLite | `Microsoft.EntityFrameworkCore.Sqlite` 10.x，Scoped，启动时 MigrateAsync |
| R-002 | SignalR 广播 | `IHubContext<ShareHub>` 注入到 ShareService；Hub 类为空 |
| R-003 | UI 框架 | **Tailwind CSS v4**（@tailwindcss/cli）；Heroicons 内联 SVG；`app.css` + `@theme` |
| R-004 | 文件存储 | 接口先行 + 空实现（NotImplementedException）；MVP 不暴露文件上传 UI |

---

## Phase 1: Design Decisions

### 1. 数据模型

详见 [data-model.md](data-model.md)。关键决策：
- `ShareItem`：Guid 主键，`ContentType` 枚举显式赋值（保证 Migration 稳定），`ToDto()` 方法集中 DTO 转换
- `ShareItemDto`：`sealed record`，字段与 `ShareItem` 一一对应，不含导航属性
- `AnyDropDbContext`：Primary Constructor，`HasMaxLength(10_000)` for `Content`，`HasIndex(x => x.CreatedAt)`

### 2. UI 架构（Tailwind CSS v4）

**布局策略**：CSS Grid 两列（`16rem 1fr`），移动端（< 768px）隐藏侧边栏、主内容全宽。

**关键技术决策**：

| 决策项 | 选择 | 理由 |
|---|---|---|
| Tailwind 集成方式 | CLI (`@tailwindcss/cli`) | 生产环境最优；无需 JS 打包器；Blazor 静态文件友好 |
| 输入 CSS | `wwwroot/app.css` | 统一入口；`@import "tailwindcss"` + `@theme {}` |
| 输出 CSS | `wwwroot/tailwind.css` | 引用于 App.razor；publish 时可通过 MSBuild Task 自动构建 |
| 图标 | Heroicons 内联 SVG | 无 JS 依赖；可通过 Razor partial 或直接内联复用 |
| 颜色 | `@theme` CSS 变量 | 集中管理，遵循宪法"禁止硬编码颜色" |

**`app.css` 关键结构**：
```css
@import "tailwindcss";

@theme {
  --color-brand: #6366f1;
  --color-brand-hover: #4f46e5;
  --color-surface: #f9fafb;
  --color-sidebar: #f3f4f6;
}

@layer base {
  html, body { @apply h-full antialiased bg-[--color-surface]; }
}

@layer components {
  .btn-primary { @apply bg-[--color-brand] hover:bg-[--color-brand-hover] text-white px-4 py-2 rounded-lg text-sm font-medium transition-colors; }
  .app-shell { @apply grid h-screen; grid-template-columns: 16rem 1fr; }
  @media (max-width: 768px) {
    .app-shell { grid-template-columns: 1fr; }
    .sidebar { display: none; }
  }
}
```

**`App.razor` 引用**：
```razor
<link rel="stylesheet" href="tailwind.css" />
```

### 3. 服务层接口

```csharp
// Services/IShareService.cs
public interface IShareService
{
    Task<ShareItemDto> SendTextAsync(string content, CancellationToken ct = default);
    Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default);
}

// Services/IFileStorageService.cs
public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default);
    Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default);
    Task DeleteFileAsync(string storagePath, CancellationToken ct = default);
}
```

### 4. Program.cs 注册（本版本范围）

```csharp
// EF Core
builder.Services.AddDbContext<AnyDropDbContext>(opts =>
    opts.UseSqlite($"Data Source={dbPath}"));

// SignalR
builder.Services.AddSignalR();

// Services
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();

// Blazor
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// 中间件
app.MapHub<ShareHub>("/hubs/share");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// 启动时迁移
using var scope = app.Services.CreateScope();
await scope.ServiceProvider.GetRequiredService<AnyDropDbContext>().Database.MigrateAsync();
```

---

## Risk & Mitigation

| 风险 | 可能性 | 缓解措施 |
|---|---|---|
| Tailwind CLI 构建未集成到 `dotnet publish` | 中 | 手动构建 + 将 `tailwind.css` 提交到 git（可在 CI 中运行 CLI）；后续用 MSBuild Task 自动化 |
| `tailwind.css` 未生成导致生产样式缺失 | 中 | quickstart.md 明确说明必须先运行 CLI；在 .gitignore 中保留 tailwind.css 的 `!AnyDrop/wwwroot/tailwind.css` 覆盖规则 |
| Blazor `@inject` 服务空实现（FileStorageService）在测试中意外调用 | 低 | 单元测试用 Moq 替换，验证调用路径不进入空实现 |
| SQLite 并发写入（多设备同时发送） | 低 | Blazor Server Scoped DbContext 串行化写入；MVP 场景并发量极低 |

---

*Post-Phase-1 Constitution Re-Check*: ✅ 所有设计决策符合 Constitution v2.0.0 约束。