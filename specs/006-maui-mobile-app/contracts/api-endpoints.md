# API Endpoints Contract: AnyDrop 移动端 App

**Feature**: `006-maui-mobile-app`  
**Phase**: 1 — Design & Contracts  
**Date**: 2026-04-29  
**Base Path**: `{BaseUrl}/api/v1`  
**Auth**: `Authorization: Bearer {jwt}` （所有需认证端点）

---

## 认证（Auth）

### GET `/auth/setup-status`

检查服务端是否需要首次初始化（无需认证）。

**Response** `200 OK`:
```json
{ "success": true, "data": { "requiresSetup": false } }
```

**Client Usage**: App 启动时 → `LoginPage.OnInitializedAsync` 调用，决定路由至 `/login` 或 `/setup-account`。

---

### POST `/auth/setup`

首次创建账号（`requiresSetup: true` 时调用，无需认证）。

**Request Body**:
```json
{
  "nickname": "admin",
  "password": "Str0ngP@ss",
  "confirmPassword": "Str0ngP@ss"
}
```

**Response** `200 OK`:
```json
{
  "success": true,
  "data": {
    "user": { "nickname": "admin", "lastLoginAt": null },
    "accessToken": "eyJ...",
    "expiresAt": "2026-05-29T00:00:00Z"
  }
}
```

**Client Usage**: `SetupAccountPage.razor` 提交后，保存 Token → 跳转主界面。

---

### POST `/auth/login`

密码登录（无需认证）。

**Request Body**:
```json
{ "password": "Str0ngP@ss" }
```

**Response** `200 OK`:
```json
{
  "success": true,
  "data": {
    "user": { "nickname": "admin", "lastLoginAt": "2026-04-28T12:00:00Z" },
    "accessToken": "eyJ...",
    "expiresAt": "2026-05-29T00:00:00Z"
  }
}
```

**Error Scenarios**:
- `401` 密码错误 → 显示友好提示，不清除已存 Token
- `429` 登录限流 → 提示稍后重试

**Client Usage**: `LoginPage.razor` 提交 → 保存 Token → 启动 SignalR → 跳转 `/`。

---

### POST `/auth/logout`

登出，清除服务端会话（需认证）。

**Response** `200 OK`:
```json
{ "success": true, "data": { "loggedOut": true } }
```

**Client Usage**: `SettingsPage` 点击退出 → 调用此接口 → 清除本地 Token → 跳转 `/login`。

---

### GET `/auth/me`

获取当前用户信息（需认证）。

**Response** `200 OK`:
```json
{
  "success": true,
  "data": { "nickname": "admin", "lastLoginAt": "2026-04-28T12:00:00Z" }
}
```

---

## 主题（Topics）

### GET `/topics`

获取活跃主题列表，按 `SortOrder` 升序排列，置顶主题排前（需认证）。

**Response** `200 OK`:
```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "默认",
      "icon": "📋",
      "sortOrder": 0,
      "isPinned": false,
      "isArchived": false,
      "messageCount": 42,
      "lastMessagePreview": "Hello..."
    }
  ]
}
```

---

### GET `/topics/archived`

获取已归档主题列表（需认证）。

---

### POST `/topics`

创建新主题（需认证）。

**Request Body**: `{ "name": "工作" }`  
**Response** `201 Created`: 返回新 `TopicDto`。

---

### PUT `/topics/reorder`

批量更新主题排序（需认证）。

**Request Body**:
```json
{
  "items": [
    { "topicId": "uuid-1", "sortOrder": 0 },
    { "topicId": "uuid-2", "sortOrder": 1 }
  ]
}
```

**Response** `200 OK`: `{ "success": true, "data": null }`

---

### PUT `/topics/{id}`

重命名主题（需认证）。

**Request Body**: `{ "name": "新名称" }`

---

### PUT `/topics/{id}/icon`

更改主题图标（需认证）。

**Request Body**: `{ "icon": "🚀" }`

---

### PUT `/topics/{id}/pin`

置顶/取消置顶主题（需认证）。

**Request Body**: `{ "isPinned": true }`

---

### PUT `/topics/{id}/archive`

归档/取消归档主题（需认证）。

**Request Body**: `{ "isArchived": true }`

---

### DELETE `/topics/{id}`

删除主题（需认证）。

**Response** `204 No Content` 或 `200 OK`  
**Client Usage**: 二次确认弹窗 → 调用 → 本地列表同步移除。

---

### GET `/topics/{id}/messages`

获取主题消息列表，分页（需认证）。

**Query Params**:
| 参数 | 类型 | 说明 |
|------|------|------|
| `before` | `string` (ISO 8601) | 游标（返回此时间之前的消息） |
| `limit` | `int` | 每页数量，默认 30 |

**Response** `200 OK`:
```json
{
  "success": true,
  "data": {
    "messages": [ /* ShareItemDto[] */ ],
    "hasMore": true,
    "nextCursor": "2026-04-28T12:00:00Z"
  }
}
```

**Pagination Strategy**: 向上滚动到顶部时，使用 `nextCursor` 作为下次请求的 `before` 参数。

---

### GET `/topics/{id}/messages/search`

关键词搜索消息（需认证）。

**Query Params**: `q=关键词&limit=20&before=游标`  
**Response**: `IReadOnlyList<ShareItemDto>`（与分页消息结构一致）

---

### GET `/topics/{id}/messages/by-date`

按日期获取消息（需认证）。

**Query Params**: `date=2026-04-28`  
**Response**: `IReadOnlyList<ShareItemDto>`

---

### GET `/topics/{id}/messages/by-type`

按内容类型筛选消息（需认证）。

**Query Params**: `type=Image&limit=20&before=游标`  
**ContentType 值**: `Text` | `Image` | `Video` | `File` | `Link`

---

### GET `/topics/{id}/active-dates`

获取该主题有消息的日期列表（用于日历高亮，需认证）。

**Query Params**: `year=2026&month=4`  
**Response**:
```json
{
  "success": true,
  "data": { "dates": ["2026-04-01", "2026-04-15", "2026-04-28"] }
}
```

---

## 消息（ShareItems）

### POST `/share-items/text`

发送文本消息（需认证）。

**Request Body**:
```json
{
  "content": "Hello from mobile!",
  "topicId": "uuid"
}
```

**Response** `201 Created`: 返回新 `ShareItemDto`。

---

### GET `/share-items/{id}/file`

获取/下载文件（需认证）。

**Query Params**: `download=true`（触发 `Content-Disposition: attachment`）  
**Response**: 文件二进制流（`Content-Type` 对应 MIME 类型）

**Client Usage**:
- Image/Video `<img>` src 使用 `?download` 不传 → 预览
- File 消息点击 "下载" → 传 `?download=true`，写入设备下载目录

---

### POST `/files`

上传文件（图片/视频/任意文件）（需认证）。

**Request**: `multipart/form-data`

| 字段 | 类型 | 说明 |
|------|------|------|
| `file` | `binary` | 文件内容 |
| `topicId` | `string (guid)` | 目标主题 ID |

**Response** `201 Created`: 返回新 `ShareItemDto`。

**Constraints**:
- 最大文件大小：由服务端配置（默认通常 100MB）
- 无效 MIME 类型时返回 `400 Bad Request`

---

### DELETE `/share-items/cleanup`

手动清理旧消息（需认证）。

**Query Params**: `months=3`（保留最近 N 个月的消息）  
**Response** `200 OK`:
```json
{ "success": true, "data": { "deletedCount": 42 } }
```

---

## 设置（Settings）

### GET `/settings/security`

获取当前安全设置（需认证）。

**Response** `200 OK`:
```json
{
  "success": true,
  "data": {
    "autoFetchLinkPreview": true,
    "burnAfterReadingMinutes": 0,
    "language": "zh-CN",
    "autoCleanupEnabled": false,
    "autoCleanupMonths": 1
  }
}
```

---

### PUT `/settings/security`

更新安全设置（需认证）。

**Request Body**: 与 `GET` 响应 `data` 字段结构相同。

---

### PUT `/settings/profile`

更新昵称（需认证）。

**Request Body**: `{ "nickname": "新昵称" }`

---

### PUT `/settings/password`

修改密码（需认证）。

**Request Body**:
```json
{
  "currentPassword": "oldPass",
  "newPassword": "newPass",
  "confirmPassword": "newPass"
}
```

**Error Scenarios**:
- `400` 当前密码错误 / 新密码不一致

---

## HTTP 错误处理规范

| Status Code | 客户端处理策略 |
|------------|--------------|
| `400` | 从 `error` 字段提取错误信息，显示表单错误或 Toast |
| `401` | `AuthDelegatingHandler` 清除 Token → `AppEventBus.RaiseAuthExpired()` → 导航至 `/login` |
| `404` | 显示"内容不存在"提示，不崩溃 |
| `429` | 显示"请求过于频繁，稍后重试"提示 |
| `5xx` | 显示"服务端错误，请稍后重试"提示，记录日志 |
| 网络超时 | 显示离线横幅（`ConnectivityService` 事件） |

---

## 认证头格式

```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json        （所有 JSON 请求体）
Content-Type: multipart/form-data     （文件上传）
Accept: application/json
```
