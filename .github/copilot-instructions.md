# AnyDrop — Copilot Instructions

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
| UI Components | [Microsoft Fluent UI for Blazor](https://www.fluentui-blazor.net/) v4.13.2 | Do NOT use Bootstrap or Tailwind |
| Icons | `Microsoft.FluentUI.AspNetCore.Components.Icons` | Use Fluent icon names, e.g. `<FluentIcon Value="@(new Icons.Regular.Size24.Document())" />` |
| Styling | CSS Variables (Fluent Design Tokens) | See `wwwroot/app.css`; avoid hardcoded colors |
| Hosting | Kestrel (HTTP) | Dev: `http://localhost:5002` |

---

## Architecture

```
AnyDrop/
├── Components/
│   ├── Pages/          # Route pages (add new @page components here)
│   ├── Layout/         # MainLayout.razor, modal/overlay components
│   └── _Imports.razor  # Global usings — add new shared namespaces here
├── wwwroot/
│   └── app.css         # Global styles using Fluent Design Tokens
└── Program.cs          # DI registration and middleware pipeline
```

**Render mode**: All components run as Interactive Server (SignalR). Do not add `@rendermode InteractiveWebAssembly`.

**Routing**: 404 is handled via `UseStatusCodePagesWithReExecute("/not-found")` → `Pages/NotFound.razor`.

**Global usings** (already in `_Imports.razor`): `Microsoft.FluentUI.AspNetCore.Components`, `Microsoft.JSInterop`, `AnyDrop.*`.

---

## Build & Run

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

### Components
- Add new pages in `Components/Pages/` with `@page "/route"` directive
- Add new layout elements (nav, modals, drawers) in `Components/Layout/`
- Prefer code-behind files (`ComponentName.razor.cs`) for complex logic; keep `.razor` files focused on markup
- Register services in `Program.cs` using `builder.Services.Add*` before `builder.Build()`

### UI
- Use `<Fluent*>` components from FluentUI; avoid raw HTML equivalents when a Fluent component exists
- Use Fluent Design Tokens for color/spacing (e.g. `var(--neutral-foreground-rest)`) instead of hardcoded values
- NavMenu icon visibility is controlled via `.navmenu-icon` CSS class (currently hidden; enable via CSS when nav is implemented)

### Data & State
- Use Blazor's built-in DI (`@inject`) to access services in components
- For cross-component state, use scoped services or Blazor Cascading Values — avoid static state
- File uploads: use `<FluentInputFile>` component; validate MIME type and size at service boundary

### Containerization
- Target `linux/amd64` (alpine-based .NET runtime image preferred for size)
- App should read configuration via environment variables for container deployments (e.g., `Storage:BasePath`, `Auth:Password`)
- Persistent data (uploaded files, database) must be mounted via Docker volumes — do not store in the container layer

---

## Security Considerations

- This is a **single-user or small-group private app** — implement simple but effective auth (e.g., password-based session, or OIDC via a proxy)
- Never expose admin endpoints without authentication
- Validate all uploaded file types and enforce size limits to prevent abuse
- Use `Content-Disposition: attachment` for file downloads to prevent unsafe content execution in browser
