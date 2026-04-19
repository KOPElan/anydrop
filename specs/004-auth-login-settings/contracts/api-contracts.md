# API Contracts: 认证、登录与设置功能

Base path: `/api/v1`

All responses follow envelope:

```json
{
  "success": true,
  "data": {},
  "error": null
}
```

## 1. Initialize Setup Status

### GET `/api/v1/auth/setup-status`

- Auth: Public
- Purpose: 判断是否需要首次配置
- Response `200`:

```json
{
  "success": true,
  "data": {
    "requiresSetup": true
  },
  "error": null
}
```

## 2. Run Initial Setup

### POST `/api/v1/auth/setup`

- Auth: Public（仅当无用户时可用）
- Request:

```json
{
  "nickname": "Admin",
  "password": "P@ssw0rd!",
  "confirmPassword": "P@ssw0rd!"
}
```

- Success `201`:

```json
{
  "success": true,
  "data": {
    "user": {
      "nickname": "Admin",
      "lastLoginAt": "2026-04-19T09:00:00Z"
    },
    "accessToken": "<jwt>",
    "expiresAt": "2026-04-20T09:00:00Z"
  },
  "error": null
}
```

- Error:
  - `409` setup already completed
  - `400` validation failed

## 3. Login

### POST `/api/v1/auth/login`

- Auth: Public
- Request:

```json
{
  "password": "P@ssw0rd!",
  "returnUrl": "/"
}
```

- Success `200`: same payload shape as setup
- Error:
  - `401` invalid credentials / cooldown active（统一错误信息）

## 4. Logout

### POST `/api/v1/auth/logout`

- Auth: Required (Cookie or Bearer)
- Behavior: `SessionVersion++`，使已签发 JWT 失效
- Success `200`:

```json
{
  "success": true,
  "data": {
    "loggedOut": true
  },
  "error": null
}
```

## 5. Get Current Profile

### GET `/api/v1/auth/me`

- Auth: Required (Cookie or Bearer)
- Success `200`:

```json
{
  "success": true,
  "data": {
    "nickname": "Admin",
    "lastLoginAt": "2026-04-19T09:00:00Z"
  },
  "error": null
}
```

## 6. Update Nickname

### PUT `/api/v1/settings/profile`

- Auth: Required
- Request:

```json
{
  "nickname": "AnyDrop Owner"
}
```

- Success `200`: returns updated profile
- Error:
  - `400` invalid nickname length

## 7. Update Password

### PUT `/api/v1/settings/password`

- Auth: Required
- Request:

```json
{
  "currentPassword": "old-pass",
  "newPassword": "new-pass-123",
  "confirmPassword": "new-pass-123"
}
```

- Success `200`: `{ "success": true, "data": { "updated": true }, "error": null }`
- Side effects: rotate hash+salt, `SessionVersion++`
- Error:
  - `400` validation failed
  - `401` current password invalid

## 8. Get Security Settings

### GET `/api/v1/settings/security`

- Auth: Required
- Success `200`:

```json
{
  "success": true,
  "data": {
    "autoFetchLinkPreview": true
  },
  "error": null
}
```

## 9. Update Security Settings

### PUT `/api/v1/settings/security`

- Auth: Required
- Request:

```json
{
  "autoFetchLinkPreview": false
}
```

- Success `200`: returns updated security settings

---

## Auth Behavior Contract

- Missing/invalid/expired bearer token => `401 Unauthorized`
- JWT payload must include:
  - `sub` (user id)
  - `sessionVersion` (int)
  - `exp`
- On each authenticated API request, server must compare token `sessionVersion` with current user `SessionVersion`; mismatch => `401`
- Protected page routes redirect unauthenticated users to `/login?returnUrl=<encoded-path>`
