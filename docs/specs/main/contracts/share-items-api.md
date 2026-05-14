# API Contract: Share Items

**Version**: v1  
**Base Path**: `/api/v1/share-items`  
**Date**: 2026-04-18  
**Feature**: feat/001-core-infra-mvp

---

## Overview

Share Items API 供手机 App 及第三方客户端调用，与 Blazor Web UI 共享相同的业务逻辑（`IShareService`）。

**General Response Envelope**:

```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

**Error Response**:

```json
{
  "success": false,
  "data": null,
  "error": "Error message here"
}
```

---

## Endpoints

### GET /api/v1/share-items

**Description**: 获取最近 N 条共享内容（按 `createdAt` 倒序）  
**Authentication**: 暂无（MVP 阶段，内网私有部署）  
**Content-Type**: `application/json`

**Query Parameters**:

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `count` | integer | ❌ | `50` | 返回条数，范围 1–200 |

**Success Response** (200 OK):

```json
{
  "success": true,
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "contentType": 0,
      "content": "Hello from my desktop!",
      "fileName": null,
      "fileSize": null,
      "mimeType": null,
      "createdAt": "2026-04-18T10:30:00.000Z"
    }
  ],
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `400 Bad Request` | `count` 超出范围（< 1 或 > 200） |
| `500 Internal Server Error` | 数据库查询失败 |

---

### POST /api/v1/share-items

**Description**: 发布新的共享内容（MVP 阶段仅支持 `text` 类型）  
**Authentication**: 暂无（MVP 阶段）  
**Content-Type**: `application/json`

**Request Body**:

```json
{
  "contentType": "text",
  "content": "Hello from mobile app!"
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `contentType` | string | ✅ | 枚举值（小写字符串）：`text`、`file`、`image`、`video`、`link` |
| `content` | string | ✅ | 文本内容，1–10,000 字符（Text 类型） |

**Success Response** (201 Created):

```json
{
  "success": true,
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "contentType": 0,
    "content": "Hello from mobile app!",
    "fileName": null,
    "fileSize": null,
    "mimeType": null,
    "createdAt": "2026-04-18T10:31:00.000Z"
  },
  "error": null
}
```

**Error Responses**:

| Status | Scenario |
|---|---|
| `400 Bad Request` | `content` 为空或超过 10,000 字符 |
| `400 Bad Request` | `contentType` 为非 `text` 类型（MVP 未实现） |
| `422 Unprocessable Entity` | 请求体格式错误 |
| `500 Internal Server Error` | 数据库写入失败 |

**副作用**: 成功创建后，服务端通过 SignalR 向所有已连接客户端广播 `ReceiveShareItem` 事件。

---

## OpenAPI / Scalar UI

- **开发环境 OpenAPI 规范**: `GET /openapi/v1.json`
- **开发环境 Scalar UI**: `GET /scalar/v1`

---

## Minimal API Implementation Reference

```csharp
// Api/ShareItemEndpoints.cs
public static class ShareItemEndpoints
{
    public static IEndpointRouteBuilder MapShareItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/share-items")
            .WithTags("ShareItems")
            .WithOpenApi();

        group.MapGet("/", async (
            IShareService svc,
            [FromQuery] int count = 50,
            CancellationToken ct = default) =>
        {
            if (count < 1 || count > 200)
                return Results.BadRequest(new { success = false, data = (object?)null, error = "count must be between 1 and 200" });
            var items = await svc.GetRecentAsync(count, ct);
            return Results.Ok(new { success = true, data = items, error = (string?)null });
        });

        group.MapPost("/", async (
            SendTextRequest request,
            IShareService svc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 10_000)
                return Results.BadRequest(new { success = false, data = (object?)null, error = "content is required and must be ≤ 10000 characters" });
            var dto = await svc.SendTextAsync(request.Content, ct);
            return Results.Created($"/api/v1/share-items/{dto.Id}", new { success = true, data = dto, error = (string?)null });
        });

        return app;
    }

    private sealed record SendTextRequest(string ContentType, string Content);
}
```