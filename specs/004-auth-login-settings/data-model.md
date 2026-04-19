# Data Model: 认证、登录与设置功能

## 1. User（唯一用户）

### Purpose

系统唯一账号，负责页面与 API 认证身份。

### Fields

- `Id` (Guid, PK)
- `Nickname` (string, required, 1..50)
- `PasswordHash` (string, required)
- `PasswordSalt` (string, required)
- `SessionVersion` (int, required, default 1)
- `CreatedAt` (DateTimeOffset, required)
- `LastLoginAt` (DateTimeOffset?, nullable)
- `UpdatedAt` (DateTimeOffset, required)

### Validation Rules

- `Nickname` 不能为空且最大 50 字符
- 密码明文不落库，只用于校验与生成哈希
- 系统仅允许一条 User 记录存在

### State Transitions

- 首次初始化：无用户 → 创建用户（SessionVersion=1）
- 登录成功：更新 `LastLoginAt`
- 登出：`SessionVersion = SessionVersion + 1`
- 修改密码成功：更新 `PasswordHash/PasswordSalt` 且 `SessionVersion + 1`

---

## 2. SystemSettings（全局系统设置）

### Purpose

保存系统级安全开关。

### Fields

- `Id` (Guid, PK)
- `AutoFetchLinkPreview` (bool, required, default true)
- `UpdatedAt` (DateTimeOffset, required)

### Validation Rules

- 系统仅允许一条有效配置记录

### Behavior

- 应用启动时若为空，自动插入默认行（`AutoFetchLinkPreview=true`）
- 设置页保存后立即生效并持久化

---

## 3. LoginAttemptWindow（可选内存/持久化限流模型）

### Purpose

跟踪失败登录次数与冷却窗口，用于 FR-017。

### Candidate Fields

- `Key` (string: `user-or-ip`)
- `FailedCount` (int)
- `FirstFailedAt` (DateTimeOffset)
- `LockedUntil` (DateTimeOffset?)

### Notes

- 可先采用内存缓存（单实例局域网场景）
- 若后续需要多实例，可迁移到持久化表/分布式缓存

---

## DTO Changes

### Setup DTOs

- `SetupRequest`: `Nickname`, `Password`, `ConfirmPassword`
- `SetupResponse`: `Success`, `UserProfileDto`, `TokenInfo`

### Auth DTOs

- `LoginRequest`: `Password`, `ReturnUrl?`
- `LoginResponse`: `Success`, `AccessToken`, `ExpiresAt`, `UserProfileDto`
- `LogoutResponse`: `Success`

### Settings DTOs

- `UserProfileDto`: `Nickname`, `LastLoginAt`
- `UpdateNicknameRequest`: `Nickname`
- `UpdatePasswordRequest`: `CurrentPassword`, `NewPassword`, `ConfirmPassword`
- `SecuritySettingsDto`: `AutoFetchLinkPreview`
- `UpdateSecuritySettingsRequest`: `AutoFetchLinkPreview`

---

## Relation to Existing Models

- `ShareItem` / `Topic` 不新增外键（保持当前业务简单）
- 认证状态与系统设置由新增 `User`、`SystemSettings` 独立管理
- 003 中链接预览流程在执行前检查 `SystemSettings.AutoFetchLinkPreview`

---

## EF Core Migration Summary

Planned migration: `AddAuthAndSystemSettings`

- Add table `Users`
- Add table `SystemSettings`
- Add index/constraint to ensure single-user semantics
- Seed default `SystemSettings` row
