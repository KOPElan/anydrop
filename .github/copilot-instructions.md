# AnyDrop — Copilot Instructions

## 语言约定 / Language Convention

**AI 协同开发首选语言为简体中文。**
- 所有回复、代码注释建议、文档输出均优先使用中文。
- 若必须使用英文术语（如 API 名称、框架关键字），须在首次出现时紧跟中文说明。
- 代码本身（变量名、方法名）遵循 C# PascalCase 规范，不受此约束。

> AI collaborative development defaults to **Simplified Chinese**. English is only used for code identifiers and proper nouns, and must be accompanied by a Chinese explanation on first use.

---

## Project Overview

**AnyDrop** is a private, self-hosted cross-device content sharing app built with Blazor.  
Core value: let users securely save and retrieve text snippets, images, and files from any device via a browser.

Key design goals:
- **Private & self-hosted** — no third-party cloud dependency; users deploy their own instance
- **Cross-device** — real-time sync via Blazor's SignalR connection (Interactive Server mode)
- **Containerized** — designed to be packaged as a Docker image and deployed to any container host

---

## Tech Stack

| Layer | Choice | Notes |
|-------|--------|-------|
| Framework | .NET 10, Blazor Web App | Interactive Server render mode (no WASM) |
| UI Components | [Microsoft Fluent UI for Blazor](https://www.fluentui-blazor.net/) v4.14.0 | Do NOT use Bootstrap or Tailwind |
| Icons | `Microsoft.FluentUI.AspNetCore.Components.Icons` | Use Fluent icon names, e.g. `<FluentIcon Value="@(new Icons.Regular.Size24.Document())" />` |
| Styling | CSS Variables (Fluent Design Tokens) | See `wwwroot/app.css`; avoid hardcoded colors |
| Mobile API | .NET 10 Minimal API | RESTful, prefix `/api/v1/`, MUST NOT use MVC Controllers |
| Hosting | Kestrel (HTTP) | Dev: `http://localhost:5002` |

---

## Architecture

```
AnyDrop/├── Api/                  # Minimal API 端点扩展方法（如 MessageEndpoints.cs）├── Components/
│   ├── Pages/          # 路由页面（所有新页面加 @page 指令）
│   ├── Layout/         # MainLayout.razor、模态/抽屉组件
│   └── _Imports.razor  # 全局 using — 新增命名空间在此注册
├── Models/             # EF Core 实体与 DTO
├── Services/           # 业务逻辑服务（接口 + 实现），禁止在此依赖 Razor 组件
├── Hubs/               # SignalR Hub 类，仅调用 Service，不含业务逻辑
├── wwwroot/
│   └── app.css         # Fluent Design Token 全局样式
└── Program.cs          # DI 注册与中间件管道

AnyDrop.Tests.Unit/     # xUnit + FluentAssertions + Moq 单元测试
AnyDrop.Tests.E2E/      # Playwright 端到端测试
```

**Render mode**: All components run as Interactive Server (SignalR). Do not add `@rendermode InteractiveWebAssembly`.

**Routing**: 404 is handled via `UseStatusCodePagesWithReExecute("/not-found")` → `Pages/NotFound.razor`.

**Global usings** (already in `_Imports.razor`): `Microsoft.FluentUI.AspNetCore.Components`, `Microsoft.JSInterop`, `AnyDrop.*`.

**Architecture rule（架构规则）**: Service 层(`Services/`) 禁止依赖 Razor 组件；Hub(`Hubs/`) 只调用 Service，不含业务逻辑；Razor 组件通过 `@inject` 使用 Service。

---

## Build & Run（构建与运行）

```bash
# Restore & run locally
dotnet run --project AnyDrop

# Build for release
dotnet publish AnyDrop -c Release -o ./publish

# Build container image (Dockerfile to be added)
docker build -t anydrop .
docker run -p 8080:8080 anydrop
```

The solution file is `AnyDrop.slnx` (the new XML solution format).

---

## Conventions

### Components（组件规范）
- Add new pages in `Components/Pages/` with `@page "/route"` directive
- Add new layout elements (nav, modals, drawers) in `Components/Layout/`
- Prefer code-behind files (`ComponentName.razor.cs`) for complex logic; keep `.razor` files focused on markup
- Register services in `Program.cs` using `builder.Services.Add*` before `builder.Build()`

### Naming（命名规范）
- 所有 C# 代码遵循 PascalCase；所有异步方法必须以 `Async` 结尾（如 `SendMessageAsync`）
- 接口以大写 `I` 开头（如 `IMessageService`）

### UI
- Use `<Fluent*>` components from FluentUI; avoid raw HTML equivalents when a Fluent component exists
- Use Fluent Design Tokens for color/spacing (e.g. `var(--neutral-foreground-rest)`) instead of hardcoded values
- NavMenu icon visibility is controlled via `.navmenu-icon` CSS class (currently hidden; enable via CSS when nav is implemented)

### Data & State（数据与状态）
- Use Blazor's built-in DI (`@inject`) to access services in components
- For cross-component state, use scoped services or Blazor Cascading Values — avoid static state
- File uploads: use `<FluentInputFile>` component; validate MIME type and size at service boundary

### API 规范（Minimal API）
- 所有 HTTP API 端点使用 `app.Map*` Minimal API 注册，MUST NOT 使用 `[ApiController]` MVC 模式
- 路径规范：`/api/v1/{resource}` 复数 kebab-case；组织在 `Api/` 目录的扩展方法中并在 `Program.cs` 注册
- 响应体：统一使用 `{ success, data, error }` JSON 结构
- API 端点 MUST 复用 `Services/` 中的服务接口，不得内联业务逻辑
- 开发环境 MUST 集成 OpenAPI（`/openapi/v1.json`）与 Swagger UI

### Testing（测试规范）
- 后端 Service 层：xUnit + FluentAssertions；所有涉及 EF Core 和业务逻辑的方法必须有单元测试
- SignalR 消息分发：使用 Moq 模拟 `IHubContext<T>`，验证消息分发逻辑
- E2E：使用 Playwright，必须有至少一个验证"发送端→服务端→接收端"跨端推送链路的用例
- 测试命名格式：`[方法名]_[场景]_[期望结果]`

### Containerization（容器化）
- Target `linux/amd64` (alpine-based .NET runtime image preferred for size)
- App should read configuration via environment variables for container deployments (e.g., `Storage:BasePath`, `Auth:Password`)
- Persistent data (uploaded files, database) must be mounted via Docker volumes — do not store in the container layer

---

## Security Considerations（安全注意事项）

- This is a **single-user or small-group private app** — implement simple but effective auth (e.g., password-based session, or OIDC via a proxy)
- Never expose admin endpoints without authentication
- Validate all uploaded file types and enforce size limits to prevent abuse
- Use `Content-Disposition: attachment` for file downloads to prevent unsafe content execution in browser
