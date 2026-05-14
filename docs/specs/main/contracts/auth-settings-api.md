# API Contract: Auth & Settings

**Version**: v1  
**Base Paths**: `/api/v1/auth`, `/api/v1/settings`  
**Date**: 2026-04-29  
**Feature**: main

---

## Overview

认证与设置 API，用于用户登录、注销、个人资料管理和系统安全设置。

移动端说明：登录成功后响应体中包含 `accessToken`（JWT），移动端通过 `Authorization: Bearer <token>` 请求头携带令牌调用所有受保护端点，无需使用 Cookie。

---

## Auth Endpoints

### GET /api/v1/auth/setup-status

**Description**: 查询是否需要完成初始化设置  
**Authentication**: 无需

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": { "requiresSetup": false },
  "error": null
}
```

---

### POST /api/v1/auth/setup

**Description**: 首次初始化账号（设置昵称和密码）  
**Authentication**: 无需  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "nickname": "Admin",
  "password": "your-password",
  "confirmPassword": "your-password"
}
```

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "user": {
      "nickname": "Admin",
      "lastLoginAt": "2026-04-29T10:00:00Z"
    },
    "accessToken": "<JWT>",
    "expiresAt": "2026-04-30T10:00:00Z"
  },
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `400 Bad Request` | 参数校验失败（密码不匹配、昵称为空等） |
| `409 Conflict` | 账号已存在 |
| `429 Too Many Requests` | 超过速率限制 |

---

### POST /api/v1/auth/login

**Description**: 用户登录  
**Authentication**: 无需  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "password": "your-password",
  "returnUrl": null
}
```

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "user": {
      "nickname": "Admin",
      "lastLoginAt": "2026-04-29T10:00:00Z"
    },
    "accessToken": "<JWT Bearer Token>",
    "expiresAt": "2026-04-30T10:00:00Z"
  },
  "error": null
}
```

> **移动端说明**: 使用 `accessToken` 作为 Bearer Token 调用所有受保护 API，Web 端同时写入 Cookie。

**Error Responses**:

| Status | Scenario |
|---|---|
| `401 Unauthorized` | 密码错误 |
| `429 Too Many Requests` | 登录失败次数过多，触发冷却 |

---

### POST /api/v1/auth/logout

**Description**: 注销当前会话  
**Authentication**: 必填

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": { "loggedOut": true },
  "error": null
}
```

---

### GET /api/v1/auth/me

**Description**: 获取当前用户信息  
**Authentication**: 必填

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "nickname": "Admin",
    "lastLoginAt": "2026-04-29T10:00:00Z"
  },
  "error": null
}
```

---

## Settings Endpoints

### PUT /api/v1/settings/profile

**Description**: 更新用户昵称  
**Authentication**: 必填  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "nickname": "新昵称"
}
```

**Success Response** (200 OK): UserProfileDto

---

### PUT /api/v1/settings/password

**Description**: 修改密码（修改成功后 Cookie 会话自动失效，需重新登录）  
**Authentication**: 必填  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "currentPassword": "old-pass",
  "newPassword": "new-pass",
  "confirmPassword": "new-pass"
}
```

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": { "updated": true },
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `400 Bad Request` | 当前密码错误、新密码不匹配等 |

---

### GET /api/v1/settings/security

**Description**: 获取系统安全与偏好设置  
**Authentication**: 必填

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "autoFetchLinkPreview": true,
    "burnAfterReadingMinutes": 10,
    "language": "zh-CN",
    "autoCleanupEnabled": false,
    "autoCleanupMonths": 1
  },
  "error": null
}
```

---

### PUT /api/v1/settings/security

**Description**: 更新系统安全与偏好设置  
**Authentication**: 必填  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "autoFetchLinkPreview": true,
  "burnAfterReadingMinutes": 10,
  "language": "zh-CN",
  "autoCleanupEnabled": false,
  "autoCleanupMonths": 1
}
```

**Success Response** (200 OK): SecuritySettingsDto

---

### POST /api/v1/settings/set-culture

**Description**: 设置语言 Cookie（仅适用于 Web 端浏览器会话，移动端通过 `language` 字段控制本地化）  
**Authentication**: 必填  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "culture": "zh-CN"
}
```

**支持的语言代码**: `zh-CN`, `en-US`

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": { "culture": "zh-CN" },
  "error": null
}
```

---

## UserProfileDto Schema

```json
{
  "nickname": "Admin",
  "lastLoginAt": "2026-04-29T10:00:00Z"
}
```

## SecuritySettingsDto Schema

```json
{
  "autoFetchLinkPreview": true,
  "burnAfterReadingMinutes": 10,
  "language": "zh-CN",
  "autoCleanupEnabled": false,
  "autoCleanupMonths": 1
}
```
