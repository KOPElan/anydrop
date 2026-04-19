# API Contract: Topics

**Feature**: `speckit/002-topic-session-sidebar`  
**Base URL**: `/api/v1/topics`  
**Auth**: 与全局认证机制共享（Cookie/Bearer Token）  
**Response Format**: `{ success: bool, data: T | null, error: string | null }`

---

## GET /api/v1/topics

获取所有主题列表，按排序规则返回（SortOrder ASC, LastMessageAt DESC NULLS LAST）。

**Request**: 无参数

**Response 200**:
```json
{
  "success": true,
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "工作文件",
      "sortOrder": 0,
      "createdAt": "2026-04-19T10:00:00Z",
      "lastMessageAt": "2026-04-19T14:30:00Z",
      "messageCount": 12
    },
    {
      "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "name": "个人备忘",
      "sortOrder": 2147483647,
      "createdAt": "2026-04-18T08:00:00Z",
      "lastMessageAt": null,
      "messageCount": 0
    }
  ],
  "error": null
}
```

---

## POST /api/v1/topics

新建主题。

**Request Body**:
```json
{
  "name": "新主题名称"
}
```

**Validation**:
- `name` 必填，不能为空字符串，最大 100 字符

**Response 201**:
```json
{
  "success": true,
  "data": {
    "id": "...",
    "name": "新主题名称",
    "sortOrder": 2147483647,
    "createdAt": "2026-04-19T10:05:00Z",
    "lastMessageAt": null,
    "messageCount": 0
  },
  "error": null
}
```

**Response 400** (名称为空或超长):
```json
{
  "success": false,
  "data": null,
  "error": "主题名称不能为空，且不超过 100 个字符"
}
```

---

## PUT /api/v1/topics/{id}

更新主题名称。

**Path Parameter**: `id` (Guid) — 主题唯一标识

**Request Body**:
```json
{
  "name": "更新后的名称"
}
```

**Response 200**:
```json
{
  "success": true,
  "data": { "id": "...", "name": "更新后的名称", ... },
  "error": null
}
```

**Response 404** (主题不存在):
```json
{
  "success": false,
  "data": null,
  "error": "主题不存在"
}
```

---

## DELETE /api/v1/topics/{id}

删除指定主题（不级联删除消息，消息 `TopicId` 置为 null）。

**Path Parameter**: `id` (Guid)

**Response 204**: 无响应体

**Response 404**:
```json
{
  "success": false,
  "data": null,
  "error": "主题不存在"
}
```

---

## PUT /api/v1/topics/reorder

批量更新主题排序权重（拖拽完成后调用）。

**Request Body**:
```json
{
  "items": [
    { "topicId": "3fa85f64-...", "sortOrder": 0 },
    { "topicId": "7c9e6679-...", "sortOrder": 1 }
  ]
}
```

**Validation**:
- `items` 不能为空列表
- 每个 `sortOrder` 必须 ≥ 0

**Response 200**:
```json
{
  "success": true,
  "data": null,
  "error": null
}
```

---

## GET /api/v1/topics/{id}/messages

获取指定主题的消息历史（游标分页）。

**Path Parameter**: `id` (Guid)

**Query Parameters**:
| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `limit` | int | No | 50 | 每页消息数，最大 100 |
| `before` | string | No | null | ISO 8601 游标：返回此时间之前的消息（用于向上翻页） |

**Response 200**:
```json
{
  "success": true,
  "data": {
    "messages": [
      {
        "id": "...",
        "contentType": 0,
        "content": "文本内容",
        "fileName": null,
        "fileSize": null,
        "mimeType": null,
        "createdAt": "2026-04-19T14:30:00Z"
      }
    ],
    "hasMore": true,
    "nextCursor": "2026-04-19T10:00:00.0000000Z"
  },
  "error": null
}
```

**Response 404** (主题不存在):
```json
{
  "success": false,
  "data": null,
  "error": "主题不存在"
}
```

---

## SignalR Events

Hub: `/hubs/share`  
Event: `TopicsUpdated`

当主题列表发生变化（新建、删除、排序变更、收到新消息导致 LastMessageAt 更新）时，服务端广播此事件。

**Payload**: `IReadOnlyList<TopicDto>`（完整的最新主题列表，按排序规则排列）

**Client subscription** (Blazor 组件中):
```csharp
hubConnection.On<IReadOnlyList<TopicDto>>("TopicsUpdated", topics =>
{
    _topics = topics;
    await InvokeAsync(StateHasChanged);
});
```
