# Quickstart: 认证、登录与设置功能

## 1. Prerequisites

- .NET 10 SDK
- Node.js（用于 Tailwind 构建，若本地已有）
- SQLite 可写目录（默认由应用自动管理）

## 2. 配置项

在 `appsettings.Development.json` 或环境变量中配置：

- `Auth:JwtIssuer`
- `Auth:JwtAudience`
- `Auth:JwtSecret`（生产环境必须走环境变量）
- `Auth:TokenExpiryHours`（默认 24）
- `Auth:LoginMaxFailures`（默认 5）
- `Auth:LoginCooldownSeconds`（默认 60）

## 3. 本地运行

```bash
dotnet run --project AnyDrop
```

## 4. 首次配置验收

1. 清空数据库（或使用全新实例）
2. 打开应用任意页面，应跳转 `/setup`
3. 创建昵称与密码
4. 成功后自动登录并进入主页

## 5. 登录与页面保护验收

1. 登出后访问 `/`，应跳转 `/login?returnUrl=/`
2. 输入错误密码应返回统一错误信息
3. 输入正确密码应回跳 `returnUrl`

## 6. API JWT 验收

### 6.1 未携带 token

```bash
curl -i http://localhost:5002/api/v1/settings/security
```

期望：`401 Unauthorized`

### 6.2 携带有效 token

```bash
curl -i http://localhost:5002/api/v1/settings/security \
  -H "Authorization: Bearer <token>"
```

期望：`200` 且返回 `autoFetchLinkPreview`

### 6.3 登出后 token 立即失效

1. 使用 token 调一次受保护 API（200）
2. 调用 `/api/v1/auth/logout`
3. 再用同一个 token 调受保护 API，期望 `401`

## 7. 设置页验收

1. 修改昵称后，页面显示立即更新
2. 修改密码后，旧密码无法再次登录
3. 关闭“自动获取链接预览”后，发送 URL 不再触发链接预览

## 8. 测试命令

```bash
dotnet test AnyDrop.Tests.Unit
dotnet test AnyDrop.Tests.E2E
```

## 9. 回归检查点

- 003 功能中的链接预览仅在 `AutoFetchLinkPreview=true` 时触发
- Topic/Share 既有 API 在认证后行为不变
- 所有新增 API 统一 envelope 格式
