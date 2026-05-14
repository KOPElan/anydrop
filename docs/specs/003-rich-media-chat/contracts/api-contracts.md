# API & SignalR 契约 (003-rich-media-chat)

**Generated**: 2026-04-19  
**Phase**: 1 — 接口契约定义

---

## REST API 端点

统一响应格式（已有约定）：
```json
{ "success": true, "data": { ... }, "error": null }
{ "success": false, "data": null, "error": "错误描述" }
```

---

### 媒体/附件上传

**POST** `/api/v1/share-items/media`

上传图片或视频。请求为 `multipart/form-data`。

**Request**:
```
Content-Type: multipart/form-data

file        : 文件二进制（必填）
contentType : "Image" | "Video"（必填）
topicId     : GUID（可选，为空时归属默认 Topic）
```

**Response** `201 Created`:
```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "contentType": 2,
    "content": "image.jpg",
    "fileName": "image.jpg",
    "fileSize": 102400,
    "mimeType": "image/jpeg",
    "createdAt": "2026-04-19T03:00:00Z",
    "topicId": "uuid",
    "uploadStatus": 1,
    "thumbnailPath": null,
    "originalFileName": "my-photo.jpg",
    "linkPreview": null,
    "mediaMetadata": null
  }
}
```

**错误**：
- `400` — 文件类型不允许
- `413` — 文件超出大小限制
- `415` — MIME 类型与 contentType 不符

---

**POST** `/api/v1/share-items/attachment`

上传普通附件（非图片/视频）。

**Request**: 同上，`contentType` 固定为 `"File"`

**Response**: 同上结构，`contentType: 1`

---

### 上传重试

**POST** `/api/v1/share-items/{id}/retry`

重试失败的上传。

**Request**: Body 为空

**Response** `200 OK`:
```json
{
  "success": true,
  "data": { "id": "uuid", "uploadStatus": 1 }
}
```

**错误**：
- `404` — 消息不存在
- `409` — 状态不为 `Failed`，无法重试

---

### 文件下载

**GET** `/api/v1/share-items/{id}/file`

返回文件内容，浏览器触发下载。

**Response** `200 OK`:
```
Content-Type: {mimeType}
Content-Disposition: attachment; filename="{originalFileName}"
Content-Length: {fileSize}
```

**错误**：
- `404` — 消息或文件不存在
- `400` — 消息类型非文件

---

**GET** `/api/v1/share-items/{id}/thumbnail`

返回缩略图（图片/视频首帧）。

**Response** `200 OK`:
```
Content-Type: image/webp (图片缩略图) | image/jpeg (视频帧)
Cache-Control: public, max-age=86400
```

**降级**：缩略图未生成时返回 `204 No Content`，前端显示占位图标。

---

### 主题置顶

**PUT** `/api/v1/topics/{topicId}/pin`

置顶或取消置顶主题。

**Request**:
```json
{ "isPinned": true }
```

**Response** `200 OK`:
```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "name": "工作",
    "isPinned": true,
    "pinnedAt": "2026-04-19T03:00:00Z"
  }
}
```

**错误**：
- `404` — 主题不存在
- `400` — 内置主题不可置顶（如业务规则需要）

---

### 历史消息分页

**GET** `/api/v1/topics/{topicId}/messages?limit=20&before={cursor}`

（已有端点，无需变更签名，仅确认行为）

**Response** `200 OK`:
```json
{
  "success": true,
  "data": {
    "messages": [ { ...ShareItemDto... } ],
    "hasMore": true,
    "nextCursor": "2026-04-18T10:00:00Z"
  }
}
```

---

## SignalR 消息（客户端接收）

Hub 类：`ShareHub`  
连接端点：`/share-hub`（已有）

---

### 已有消息（不变）

| 事件名 | 数据 | 触发时机 |
|--------|------|----------|
| `TopicsUpdated` | `TopicDto[]` | 主题列表变更时 |
| `ItemReceived` | `ShareItemDto` | 新消息发送成功时 |

---

### 新增消息

**`ShareItemUpdated`**

消息内容更新推送（文件上传完成、链接预览就绪）。

```json
{
  "id": "uuid",
  "uploadStatus": 0,
  "thumbnailPath": "/thumbs/abc.webp",
  "mediaMetadata": { "width": 1920, "height": 1080, "durationSeconds": null },
  "linkPreview": null
}
```

触发时机：
1. 文件上传从 `Uploading` → `Completed` 或 `Failed`
2. 缩略图生成完成（`thumbnailPath` 非 null）
3. 链接预览抓取完成（`linkPreview` 非 null）

---

## 接口实现注意事项

1. **文件上传端点** 必须在 `Program.cs` 中配置 `RequestSizeLimitAttribute` 或等价的 `MaxRequestBodySize`
2. **下载端点** 必须使用参数化路径（`id` 为 GUID），服务层验证文件路径归属，防止路径遍历
3. **缩略图** 通过 `ThumbnailPath` 存相对路径，端点读取文件时拼接 `Storage:BasePath`，不暴露绝对路径
4. **重试端点** 需幂等：连续重试同一失败消息时，多次请求结果一致（均返回 `Uploading` 状态）
5. **置顶端点** 返回完整 `TopicDto` 并触发 `TopicsUpdated` SignalR 广播，确保所有客户端同步更新
