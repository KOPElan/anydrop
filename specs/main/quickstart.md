# Quickstart: 核心基础设施与最小 MVP

**Feature**: feat/001-core-infra-mvp  
**Date**: 2026-04-18（更新: Tailwind CSS v4 替换 Fluent UI）  
**Target**: 开发者本地启动 + 测试环境初始化

---

## 前置要求

| 工具 | 最低版本 | 安装方式 |
|---|---|---|
| .NET SDK | 10.0 | https://dotnet.microsoft.com/download |
| dotnet-ef CLI | 10.x | `dotnet tool install -g dotnet-ef` |
| Node.js（Tailwind CLI + E2E 测试） | 18+ | https://nodejs.org |

验证安装：

```powershell
dotnet --version        # 应显示 10.x
dotnet ef --version     # 应显示 10.x
node --version          # 应显示 v18+
```

---

## 步骤 1：添加 NuGet 包

在仓库根目录运行：

```powershell
# EF Core + SQLite
dotnet add AnyDrop package Microsoft.EntityFrameworkCore.Sqlite
dotnet add AnyDrop package Microsoft.EntityFrameworkCore.Design
```

> ⚠️ **不要**添加 `Microsoft.AspNetCore.OpenApi`、`Scalar.AspNetCore` 或任何 Fluent UI 包——本版本不使用。

---

## 步骤 2：安装并配置 Tailwind CSS v4

```powershell
# 在仓库根目录初始化 package.json（如尚未初始化）
npm init -y

# 安装 Tailwind v4 CLI
npm install -D @tailwindcss/cli
```

编辑 `AnyDrop/wwwroot/app.css`，将内容替换为：

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
  .btn-primary {
    @apply bg-[--color-brand] hover:bg-[--color-brand-hover]
           text-white px-4 py-2 rounded-lg text-sm font-medium transition-colors;
  }
  .app-shell {
    @apply grid h-screen;
    grid-template-columns: 16rem 1fr;
  }
  @media (max-width: 768px) {
    .app-shell { grid-template-columns: 1fr; }
    .sidebar { display: none; }
  }
}
```

构建 Tailwind CSS（**在启动 Blazor 应用前必须先运行**）：

```powershell
# 一次性构建
npx @tailwindcss/cli -i ./AnyDrop/wwwroot/app.css -o ./AnyDrop/wwwroot/tailwind.css

# 开发监听（推荐：与 dotnet run 同时运行）
npx @tailwindcss/cli -i ./AnyDrop/wwwroot/app.css -o ./AnyDrop/wwwroot/tailwind.css --watch
```

在 `AnyDrop/Components/App.razor` 中引用构建产物：

```razor
<link rel="stylesheet" href="tailwind.css" />
```

> **提示**：建议将 `AnyDrop/wwwroot/tailwind.css` 加入 `.gitignore`（由 CLI 生成），或保留在仓库中（简化 CI）。若 gitignore 排除，CI 管道需在 `dotnet build` 前先运行 Tailwind CLI。

---

## 步骤 3：创建 EF Core Migration

确保 `AnyDropDbContext` 和 `ShareItem` 模型已实现，然后：

```powershell
# 在 repo 根目录运行
dotnet ef migrations add InitialCreate -p AnyDrop -s AnyDrop

# 验证 Migration 文件已生成
Get-ChildItem AnyDrop/Migrations/
```

**注意**: 启动时 `Program.cs` 会自动调用 `db.Database.MigrateAsync()`，无需手动 `dotnet ef database update`。

---

## 步骤 4：配置本地开发环境

`AnyDrop/appsettings.Development.json` 中确认以下配置：

```json
{
  "Storage": {
    "DatabasePath": "data/anydrop.db",
    "BasePath": "data/files"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

---

## 步骤 5：启动应用

**推荐（两个终端并行）**：

```powershell
# 终端 1：Tailwind CSS 监听
npx @tailwindcss/cli -i ./AnyDrop/wwwroot/app.css -o ./AnyDrop/wwwroot/tailwind.css --watch

# 终端 2：Blazor 应用
dotnet run --project AnyDrop
```

访问 http://localhost:5002 验证应用正常启动。

---

## 步骤 6：运行测试

```powershell
# 单元测试
dotnet test AnyDrop.Tests.Unit

# E2E 测试（需先安装 Playwright 浏览器）
dotnet build AnyDrop.Tests.E2E
pwsh AnyDrop.Tests.E2E/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
dotnet test AnyDrop.Tests.E2E
```

---

## 步骤 7：验证 Tailwind CSS 正常工作

1. 打开 http://localhost:5002
2. 确认页面有侧边栏 + 主内容区布局（桌面端）
3. 将浏览器窗口缩窄至 < 768px，确认侧边栏隐藏、主内容全宽
4. 打开 DevTools → Network，确认 `tailwind.css` 加载成功（HTTP 200）

---

## 常见问题

| 问题 | 解决方案 |
|---|---|
| `tailwind.css` 404 | 先运行 `npx @tailwindcss/cli ...` 生成文件，再启动 Blazor |
| 样式不更新 | 确认 CLI 监听进程（`--watch`）正在运行 |
| EF Core Migration 失败 | 确认 `AnyDropDbContext` 已在 `Program.cs` 注册 |
| SQLite 数据库文件不存在 | 应用首次启动时 `MigrateAsync()` 自动创建；确认 `data/` 目录有写入权限 |