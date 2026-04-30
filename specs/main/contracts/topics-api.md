# API Contract: Topics

**Version**: v1  
**Base Path**: `/api/v1/topics`  
**Date**: 2026-04-29  
**Feature**: main

---

## Overview

Topics API 用于管理主题（频道），包括创建、更新、排序、归档、置顶，以及查询主题下的消息。所有端点均需认证（Bearer Token 或 Cookie）。

**通用响应格式（ApiEnvelope）**:

```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

**错误响应**:

```json
{
  "success": false,
  "data": null,
  "error": "错误描述"
}
```

---

## Endpoints

### GET /api/v1/topics

**Description**: 获取所有未归档主题（按 sortOrder 排序）  
**Authentication**: 必填

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "默认",
      "icon": "💬",
      "sortOrder": 0,
      "createdAt": "2026-04-01T00:00:00Z",
      "lastMessageAt": "2026-04-29T10:00:00Z",
      "messageCount": 42,
      "isBuiltIn": true,
      "lastMessagePreview": "Hello!",
      "isPinned": false,
      "pinnedAt": null,
      "isArchived": false,
      "archivedAt": null
    }
  ],
  "error": null
}
```

---

### GET /api/v1/topics/archived

**Description**: 获取所有已归档主题  
**Authentication**: 必填

**Success Response** (200 OK): 同上，`isArchived` 为 `true`

---

### GET /api/v1/topics/{id}

**Description**: 根据 ID 获取单个主题（含已归档）  
**Authentication**: 必填

**Path Parameters**:

| 参数 | 类型 | 说明 |
|---|---|---|
| `id` | guid | 主题 ID |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "工作",
    "icon": "💼",
    "sortOrder": 1,
    "createdAt": "2026-04-01T00:00:00Z",
    "lastMessageAt": "2026-04-29T10:00:00Z",
    "messageCount": 10,
    "isBuiltIn": false,
    "lastMessagePreview": "记得提交报告",
    "isPinned": true,
    "pinnedAt": "2026-04-10T08:00:00Z",
    "isArchived": false,
    "archivedAt": null
  },
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `404 Not Found` | 主题不存在 |

---

### POST /api/v1/topics

**Description**: 创建新主题  
**Authentication**: 必填  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "name": "工作"
}
```

**Success Response** (201 Created):

```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `400 Bad Request` | 名称为空或已存在 |

---

### PUT /api/v1/topics/reorder

**Description**: 批量重排主题顺序  
**Authentication**: 必填  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "items": [
    { "topicId": "3fa85f64-...", "sortOrder": 0 },
    { "topicId": "4fb96g75-...", "sortOrder": 1 }
  ]
}
```

**Success Response** (200 OK):

```json
{ "success": true, "data": null, "error": null }
```

---

### PUT /api/v1/topics/{id}

**Description**: 更新主题名称  
**Authentication**: 必填

**Request Body**: `{ "name": "新名称" }`

**Success Response** (200 OK): TopicDto

---

### PUT /api/v1/topics/{id}/pin

**Description**: 置顶或取消置顶主题  
**Authentication**: 必填

**Request Body**: `{ "isPinned": true }`

**Success Response** (200 OK): TopicDto

---

### PUT /api/v1/topics/{id}/archive

**Description**: 归档或取消归档主题  
**Authentication**: 必填

**Request Body**: `{ "isArchived": true }`

**Success Response** (200 OK): TopicDto

---

### PUT /api/v1/topics/{id}/icon

**Description**: 更新主题图标（Emoji）  
**Authentication**: 必填

**Request Body**: `{ "icon": "🚀" }`

**Success Response** (200 OK): TopicDto

---

### DELETE /api/v1/topics/{id}

**Description**: 删除主题及其所有消息和文件  
**Authentication**: 必填

**Success Response** (204 No Content)

**Error Responses**:

| Status | Scenario |
|---|---|
| `404 Not Found` | 主题不存在 |

---

### GET /api/v1/topics/{id}/messages

**Description**: 分页获取主题消息（游标分页，按 createdAt 倒序）  
**Authentication**: 必填

**Query Parameters**:

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `limit` | integer | ❌ | `50` | 每页条数 |
| `before` | DateTimeOffset | ❌ | — | 游标：仅返回此时间之前的消息 |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "messages": [ { ... } ],
    "hasMore": true,
    "nextCursor": "2026-04-29T09:00:00.000Z"
  },
  "error": null
}
```

---

### GET /api/v1/topics/{id}/messages/search

**Description**: 在主题内按文本内容搜索消息（大小写不敏感子串匹配）  
**Authentication**: 必填

**Query Parameters**:

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `q` | string | ✅ | — | 搜索关键词 |
| `limit` | integer | ❌ | `50` | 每页条数 |
| `before` | DateTimeOffset | ❌ | — | 游标：仅返回此时间之前的消息 |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "messages": [ { ... } ],
    "hasMore": false,
    "nextCursor": null
  },
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `400 Bad Request` | `q` 为空 |

---

### GET /api/v1/topics/{id}/messages/by-date

**Description**: 获取主题在指定日期内的全部消息（按服务器本地时区确定日期范围）  
**Authentication**: 必填

**Query Parameters**:

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `date` | DateOnly (yyyy-MM-dd) | ✅ | 要查询的日期 |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": [ { ... } ],
  "error": null
}
```

---

### GET /api/v1/topics/{id}/active-dates

**Description**: 获取主题在指定日期范围内有消息记录的日期集合（按服务器本地时区）  
**Authentication**: 必填

**Query Parameters**:

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `start` | DateOnly (yyyy-MM-dd) | ✅ | 范围起始日期（含） |
| `end` | DateOnly (yyyy-MM-dd) | ✅ | 范围结束日期（含） |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": ["2026-04-27", "2026-04-29"],
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `400 Bad Request` | `end` 早于 `start` |

---

### GET /api/v1/topics/{id}/messages/by-type

**Description**: 按内容类型分页获取主题消息（游标分页）  
**Authentication**: 必填

**Query Parameters**:

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `contentType` | ShareContentType | ✅ | — | 内容类型枚举值：`Text(0)` / `File(1)` / `Image(2)` / `Video(3)` / `Link(4)` |
| `limit` | integer | ❌ | `50` | 每页条数 |
| `before` | DateTimeOffset | ❌ | — | 游标：仅返回此时间之前的消息 |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "messages": [ { ... } ],
    "hasMore": true,
    "nextCursor": "2026-04-28T12:00:00.000Z"
  },
  "error": null
}
```

---

## ShareItemDto Schema

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "contentType": 0,
  "content": "消息内容",
  "fileName": null,
  "fileSize": null,
  "mimeType": null,
  "linkTitle": null,
  "linkDescription": null,
  "createdAt": "2026-04-29T10:00:00.000Z",
  "expiresAt": null,
  "topicId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

---

## ShareContentType 枚举

| 值 | 名称 | 说明 |
|---|---|---|
| `0` | `Text` | 纯文本 |
| `1` | `File` | 任意文件 |
| `2` | `Image` | 图片 |
| `3` | `Video` | 视频 |
| `4` | `Link` | 链接（含 OG 预览） |
