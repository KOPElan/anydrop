# Quickstart: 核心基础设施与最小 MVP

**Feature**: feat/001-core-infra-mvp  
**Date**: 2026-04-18  
**Target**: 开发者本地启动 + 测试环境初始化

---

## 前置要求

| 工具 | 最低版本 | 安装方式 |
|---|---|---|
| .NET SDK | 10.0 | https://dotnet.microsoft.com/download |
| dotnet-ef CLI | 10.x | `dotnet tool install -g dotnet-ef` |
| Node.js（E2E 测试） | 18+ | https://nodejs.org |

验证安装：

```powershell
dotnet --version        # 应显示 10.x
dotnet ef --version     # 应显示 10.x
```

---

## 步骤 1：添加 NuGet 包

在 `AnyDrop/` 项目目录中运行：

```powershell
# EF Core + SQLite
dotnet add AnyDrop package Microsoft.EntityFrameworkCore.Sqlite
dotnet add AnyDrop package Microsoft.EntityFrameworkCore.Design

# OpenAPI（.NET 10 内置，但需显式引用）
dotnet add AnyDrop package Microsoft.AspNetCore.OpenApi

# Scalar UI（Swagger 替代品）
dotnet add AnyDrop package Scalar.AspNetCore
```

---

## 步骤 2：创建 EF Core Migration

确保 `AnyDropDbContext` 和 `ShareItem` 模型已实现，然后：

```powershell
# 在 repo 根目录运行（-p 指定主项目，-s 指定启动项目）
dotnet ef migrations add InitialCreate -p AnyDrop -s AnyDrop

# 验证 Migration 文件已生成
ls AnyDrop/Migrations/
```

**注意**: 启动时 `Program.cs` 会自动调用 `db.Database.MigrateAsync()`，无需手动 `dotnet ef database update`。

---

## 步骤 3：配置本地开发环境

`appsettings.Development.json` 中已有默认配置，可按需调整：

```json
{
  "Storage": {
    "DatabasePath": "data/anydrop.db",
    "BasePath": "data/files"
  }
}
```

容器化部署时通过环境变量覆盖（12-Factor 原则）：

```bash
Storage__DatabasePath=/data/anydrop.db
Storage__BasePath=/data/files
```

---

## 步骤 4：本地启动

```powershell
# 从 repo 根目录
dotnet run --project AnyDrop

# 开发服务器地址
# http://localhost:5002
# OpenAPI: http://localhost:5002/openapi/v1.json
# Scalar UI: http://localhost:5002/scalar/v1
```

---

## 步骤 5：初始化测试项目

### 单元测试（xUnit）

```powershell
# 创建测试项目
dotnet new xunit -n AnyDrop.Tests.Unit -o AnyDrop.Tests.Unit
dotnet sln AnyDrop.slnx add AnyDrop.Tests.Unit

# 添加主项目引用和测试包
dotnet add AnyDrop.Tests.Unit reference AnyDrop
dotnet add AnyDrop.Tests.Unit package FluentAssertions
dotnet add AnyDrop.Tests.Unit package Moq
dotnet add AnyDrop.Tests.Unit package Microsoft.EntityFrameworkCore.InMemory
```

### E2E 测试（Playwright）

```powershell
# 创建测试项目
dotnet new nunit -n AnyDrop.Tests.E2E -o AnyDrop.Tests.E2E
dotnet sln AnyDrop.slnx add AnyDrop.Tests.E2E

# 添加 Playwright 包
dotnet add AnyDrop.Tests.E2E package Microsoft.Playwright.NUnit

# 安装 Playwright 浏览器（首次运行需要）
dotnet build AnyDrop.Tests.E2E
pwsh AnyDrop.Tests.E2E/bin/Debug/net10.0/playwright.ps1 install
```

---

## 步骤 6：运行测试

```powershell
# 单元测试
dotnet test AnyDrop.Tests.Unit

# E2E 测试（需先启动 dev server）
dotnet run --project AnyDrop &
dotnet test AnyDrop.Tests.E2E
```

---

## 快速验证清单

完成上述步骤后，验证以下功能正常：

- [ ] `dotnet build AnyDrop` 零警告、零错误
- [ ] `http://localhost:5002` 可打开 Blazor 页面
- [ ] `http://localhost:5002/scalar/v1` 可访问 Scalar API UI
- [ ] 在 Home.razor 输入文本并发送，页面实时显示新消息
- [ ] 打开第二个浏览器标签页，第一个页面发送后第二个页面同步更新
- [ ] `dotnet test AnyDrop.Tests.Unit` 全部通过