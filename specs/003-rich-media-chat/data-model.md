# Data Model: 富媒体聊天增强 (003-rich-media-chat)

**Generated**: 2026-04-19  
**Phase**: 1 — 实体设计与数据库变更

---

## 实体变更总览

### 1. Topic（已有实体，扩展字段）

```csharp
public sealed class Topic
{
    // 已有字段（不变）
    public Guid Id { get; set; }
    public string Name { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public bool IsBuiltIn { get; set; }
    public string? LastMessagePreview { get; set; }

    // 新增字段
    public bool IsPinned { get; set; } = false;
    public DateTimeOffset? PinnedAt { get; set; }
}
```

**侧边栏排序规则**：
```sql
ORDER BY IsPinned DESC,
         PinnedAt ASC NULLS LAST,
         LastMessageAt DESC NULLS LAST
```

**TopicDto 对应扩展**：
```csharp
public sealed record TopicDto(
    Guid Id,
    string Name,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMessageAt,
    int MessageCount,
    bool IsBuiltIn,
    string? LastMessagePreview,
    bool IsPinned,           // 新增
    DateTimeOffset? PinnedAt // 新增
);
```

---

### 2. ShareItem（已有实体，扩展字段）

```csharp
public sealed class ShareItem
{
    // 已有字段（不变）
    public Guid Id { get; set; }
    public ShareContentType ContentType { get; set; }
    public string Content { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? TopicId { get; set; }

    // 新增字段
    public UploadStatus UploadStatus { get; set; } = UploadStatus.Completed;
    public string? ThumbnailPath { get; set; }
    public string? OriginalFileName { get; set; }

    // 导航属性
    public LinkPreview? LinkPreview { get; set; }
    public MediaMetadata? MediaMetadata { get; set; }
}
```

**ShareItemDto 对应扩展**：
```csharp
public sealed record ShareItemDto(
    Guid Id,
    ShareContentType ContentType,
    string Content,
    string? FileName,
    long? FileSize,
    string? MimeType,
    DateTimeOffset CreatedAt,
    Guid? TopicId,
    UploadStatus UploadStatus,       // 新增
    string? ThumbnailPath,           // 新增
    string? OriginalFileName,        // 新增
    LinkPreviewDto? LinkPreview,     // 新增
    MediaMetadataDto? MediaMetadata  // 新增
);
```

---

### 3. ShareContentType（已有枚举，无需变更）

```csharp
public enum ShareContentType
{
    Text = 0,
    File = 1,    // 附件（非图片/视频）
    Image = 2,
    Video = 3,
    Link = 4
}
```

---

### 4. UploadStatus（新增枚举）

```csharp
public enum UploadStatus
{
    Completed = 0,  // 文字消息或旧记录默认值
    Uploading = 1,  // 文件正在上传
    Failed = 2      // 上传失败
}
```

---

### 5. LinkPreview（新增实体）

```csharp
public sealed class LinkPreview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShareItemId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;

    // 导航属性
    public ShareItem ShareItem { get; set; } = null!;
}

public sealed record LinkPreviewDto(
    string Url,
    string? Title,
    string? Description
);
```

**约束**：
- `ShareItemId` 唯一索引（一条消息最多一条 LinkPreview）
- `Title` 限制 500 字符，`Description` 限制 1000 字符

---

### 6. MediaMetadata（新增实体）

```csharp
public sealed class MediaMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShareItemId { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }

    // 导航属性
    public ShareItem ShareItem { get; set; } = null!;
}

public sealed record MediaMetadataDto(
    int? Width,
    int? Height,
    double? DurationSeconds
);
```

**约束**：
- `ShareItemId` 唯一索引（一条消息最多一条 MediaMetadata）

---

## 新增请求/响应模型

```csharp
// 文件上传请求（Service 层）
public sealed record SendFileRequest(
    IBrowserFile File,
    ShareContentType ContentType,  // Image / Video / File
    Guid? TopicId
);

// 置顶操作请求
public sealed record PinTopicRequest(bool IsPinned);

// 历史消息分页（已有，无需变更）
// TopicMessagesResponse(Messages, HasMore, NextCursor) — 已有

// 链接预览查询
public sealed record LinkMetaResult(
    string Url,
    string? Title,
    string? Description,
    bool Success
);
```

---

## EF Core Migration 变更清单

| 操作 | 表 | 字段/索引 |
|------|-----|-----------|
| ALTER | `Topics` | 新增 `IsPinned` (INTEGER NOT NULL DEFAULT 0) |
| ALTER | `Topics` | 新增 `PinnedAt` (TEXT nullable) |
| ALTER | `Topics` | 新增复合索引 `(IsPinned DESC, PinnedAt ASC, LastMessageAt DESC)` |
| ALTER | `ShareItems` | 新增 `UploadStatus` (INTEGER NOT NULL DEFAULT 0) |
| ALTER | `ShareItems` | 新增 `ThumbnailPath` (TEXT nullable) |
| ALTER | `ShareItems` | 新增 `OriginalFileName` (TEXT nullable) |
| CREATE | `LinkPreviews` | `Id`, `ShareItemId` (UNIQUE FK), `Url`, `Title`, `Description`, `FetchedAt` |
| CREATE | `MediaMetadata` | `Id`, `ShareItemId` (UNIQUE FK), `Width`, `Height`, `DurationSeconds` |

---

## 实体关系图

```
Topic (1) ──────── (N) ShareItem
                         │
              ┌──────────┼──────────┐
              │          │          │
         LinkPreview  MediaMetadata  (ThumbnailPath → FileStorage)
         (0..1)       (0..1)
```

---

## 状态转换图：ShareItem.UploadStatus

```
[文字消息]      → Completed (直接)
[文件选择/拖拽] → Uploading → Completed
                           ↘ Failed → Uploading (重试) → Completed / Failed
```
