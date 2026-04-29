# API Contract: Files

**Version**: v1  
**Base Path**: `/api/v1/files`  
**Date**: 2026-04-29  
**Feature**: main

---

## Overview

文件上传 API，用于上传图片、视频、任意文件并在指定主题中创建对应的 ShareItem。  
上传完成后，服务端通过 SignalR 广播 `ReceiveShareItem` 事件通知所有在线客户端。

文件下载通过 **Share Items** API 的 `GET /api/v1/share-items/{id}/file` 端点完成。

---

## Endpoints

### POST /api/v1/files

**Description**: 上传文件并创建分享条目  
**Authentication**: 必填  
**Content-Type**: `multipart/form-data`

**Form Fields**:

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `file` | binary | ✅ | 文件内容 |
| `topicId` | guid | ✅ | 目标主题 ID |
| `burnAfterReading` | boolean | ❌ | 是否阅后即焚（默认 `false`） |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "contentType": 2,
    "content": "files/abc123.jpg",
    "fileName": "photo.jpg",
    "fileSize": 204800,
    "mimeType": "image/jpeg",
    "linkTitle": null,
    "linkDescription": null,
    "createdAt": "2026-04-29T10:00:00Z",
    "expiresAt": null,
    "topicId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  },
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `400 Bad Request` | 未提供文件或文件为空 |
| `400 Bad Request` | 未指定 topicId |
| `400 Bad Request` | 文件大小超出配置限制（默认 1 GB） |
| `400 Bad Request` | 主题不存在 |

---

## Share Item File Download

文件下载由 Share Items API 处理：

### GET /api/v1/share-items/{id}/file

**Description**: 下载或预览指定消息中的文件  
**Authentication**: 必填

**Path Parameters**:

| 参数 | 类型 | 说明 |
|---|---|---|
| `id` | guid | ShareItem ID |

**Query Parameters**:

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `download` | boolean | ❌ | 为 `true` 时强制以附件下载（`Content-Disposition: attachment`） |

**Response**: 文件流，Content-Type 为文件原始 MIME 类型。  
对于 `text/html`、`image/svg+xml`、`application/javascript` 等危险类型，始终强制附件下载。

**Error Responses**:

| Status | Scenario |
|---|---|
| `404 Not Found` | 消息不存在 |
| `400 Bad Request` | 该消息不包含文件 |
| `404 Not Found` | 文件不存在（已被清理） |

---

## Share Items Management

### GET /api/v1/share-items

**Description**: 获取最近 N 条消息（跨所有主题，按 createdAt 倒序）  
**Authentication**: 必填

**Query Parameters**:

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `count` | integer | ❌ | `50` | 返回条数 |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": [ { ShareItemDto } ],
  "error": null
}
```

---

### POST /api/v1/share-items/text

**Description**: 发布文本消息  
**Authentication**: 必填  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "content": "Hello from mobile!",
  "topicId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "burnAfterReading": false
}
```

**Success Response** (200 OK): ShareItemDto

---

### DELETE /api/v1/share-items/cleanup

**Description**: 手动清理指定月数前的消息（同时删除关联文件）  
**Authentication**: 必填

**Query Parameters**:

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `months` | integer | ✅ | 必须为 `1`、`3` 或 `6` |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": { "deletedCount": 15 },
  "error": null
}
```

---

### DELETE /api/v1/share-items/batch

**Description**: 批量删除指定 ID 的消息（同时删除关联文件）  
**Authentication**: 必填  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "ids": ["3fa85f64-...", "4fb96g75-..."]
}
```

单次最多 500 条。

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": { "deleted": 2 },
  "error": null
}
```
