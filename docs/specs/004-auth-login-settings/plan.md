# Implementation Plan: 认证、登录与设置功能

**Branch**: `main` | **Date**: 2026-04-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-auth-login-settings/spec.md`

## Summary

为 AnyDrop 增加单用户认证体系：首次配置向导创建唯一用户、登录登出、页面与 API 访问控制、设置页账号管理（昵称/密码）以及安全开关（是否自动抓取链接预览）。技术实现基于现有 .NET 10 + Blazor Server + Minimal API + SQLite/EF Core + SignalR 架构，采用 Cookie（页面）+ JWT（API）双认证模型，并用 SessionVersion 方案实现 JWT 立即吊销。

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: ASP.NET Core Authentication/Authorization, JWT Bearer, EF Core (SQLite), Blazor Server, SignalR  
**Storage**: SQLite（EF Core Migration 管理）  
**Testing**: xUnit + FluentAssertions + Moq + Playwright  
**Target Platform**: Linux/Windows 自托管（局域网优先）  
**Project Type**: Monolithic Web Application（Blazor Server + Minimal API）  
**Performance Goals**: 登录成功后主页可交互 < 3s；未认证重定向 < 300ms；401 返回 < 100ms  
**Constraints**: 单用户；禁止硬编码密钥；API 必须 JWT 认证；未登录不可访问主页与共享内容  
**Scale/Scope**: 单实例、单用户、局域网部署，日常并发低到中等（< 50 并发连接）

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Verify the following gates against `.specify/memory/constitution.md` (AnyDrop v2.0.0):

- [x] **I. 单体架构分离**：认证、用户、设置逻辑落在 `Services/`，组件仅调用服务
- [x] **II. 技术栈合规**：仅使用现有 .NET 10 + Blazor Server + Tailwind + SQLite/EF Core + SignalR
- [x] **III. 命名规范**：新增异步方法统一 `Async` 后缀，接口 `I*` 命名
- [x] **IV. 测试覆盖**：计划覆盖 Service 单测、SignalR Moq、认证链路 E2E
- [x] **V. 安全合规**：密码哈希+salt、JWT 密钥配置化、错误信息最小泄露、API 401
- [x] **VI. 容器化**：通过环境变量注入 `Auth:*` 配置，持久化仍由 Volume 承载
- [x] **VII. RESTful API**：新增认证与设置端点采用 Minimal API，路径 `/api/v1/*`

**Post-Design Re-check**: PASS（Phase 1 设计产物未引入任何宪法违例）

## Project Structure

### Documentation (this feature)

```text
specs/004-auth-login-settings/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── api-contracts.md
└── tasks.md            # 由 /speckit.tasks 生成
```

### Source Code (repository root)

```text
AnyDrop/
├── Api/
│   ├── ShareItemEndpoints.cs
│   ├── TopicEndpoints.cs
│   └── (new) AuthEndpoints.cs
├── Components/
│   ├── Pages/
│   │   ├── Home.razor
│   │   ├── (new) Login.razor
│   │   ├── (new) Setup.razor
│   │   └── (new) Settings.razor
│   └── Layout/
├── Data/
│   └── AnyDropDbContext.cs
├── Models/
│   ├── Topic.cs
│   ├── ShareItem.cs
│   └── (new) User.cs / SystemSettings.cs / DTOs
├── Services/
│   ├── IShareService.cs
│   ├── ITopicService.cs
│   └── (new) IAuthService.cs / IUserService.cs / ISystemSettingsService.cs
├── Program.cs
└── appsettings*.json

AnyDrop.Tests.Unit/
├── Services/
│   └── (new) AuthServiceTests.cs / UserServiceTests.cs / SystemSettingsServiceTests.cs
└── Api/
   └── (new) AuthEndpointsTests.cs

AnyDrop.Tests.E2E/
└── Tests/
   └── (new) AuthFlowTests.cs
```

**Structure Decision**: 继续沿用单体项目结构，在既有 `Api/`、`Services/`、`Components/Pages/` 内扩展认证与设置功能，不新增子项目。

## Phase 0: Research Focus

1. Cookie + JWT 双认证在 Blazor Server + Minimal API 共存的最佳实践
2. 单用户首次引导（Setup Wizard）幂等创建与并发保护策略
3. SessionVersion 驱动的 JWT 即时吊销策略（登出/改密触发）
4. 登录失败限流策略（5 次失败 / 60 秒冷却）在局域网场景的实现
5. 安全设置开关对现有链接预览异步流程（003）的接入点

## Phase 1: Design Outputs

- `data-model.md`: User/SystemSettings/AuthSession 及与现有 ShareItem/Topic 的关系
- `contracts/api-contracts.md`: setup/login/logout/settings/profile 等 API 契约 + 认证行为约束
- `quickstart.md`: 本地开发与验收步骤（首次配置→登录→API 401/JWT 验证→设置开关）

## Phase 2 Preview (for /speckit.tasks)

- 先实现 P1（Setup + Login + Route/API Guard）形成 MVP
- 再实现 P2（Settings：昵称/密码/安全开关）
- 最后补全全套单测、E2E、回归与文档

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
