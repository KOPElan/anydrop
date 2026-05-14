# Data Model: 主题会话侧边栏

**Feature**: `speckit/002-topic-session-sidebar`  
**Date**: 2026-04-19

---

## Entities

### Topic（主题）

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `Guid` | PK, required | 唯一标识符，由应用层生成 |
| `Name` | `string` | MaxLength(100), required | 主题名称，不允许空字符串 |
| `SortOrder` | `int` | required, default = `int.MaxValue` | 手动排序权重，越小越靠前；未拖拽时默认 `int.MaxValue`，退化为按时间排序 |
| `CreatedAt` | `DateTimeOffset` | required, UTC | 主题创建时间 |
| `LastMessageAt` | `DateTimeOffset?` | nullable | 最后一条消息发送时间；无消息时为 null，排序时排在末尾 |

**Relationships**:
- 一个 `Topic` 拥有多个 `ShareItem`（1:N）
- EF Core 级联策略：`DeleteBehavior.Restrict`（防止删除主题时意外级联删除消息）

**Validation Rules**:
- `Name` 不能为空字符串
- `Name.Length` ≤ 100
- `SortOrder` 必须为非负整数

**Indexes**:
- `(SortOrder, LastMessageAt DESC)` — 支持侧边栏排序查询
- `CreatedAt` — 支持时间范围过滤

---

### ShareItem（消息/共享项，现有，需变更）

新增字段：

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `TopicId` | `Guid?` | FK → Topic.Id, nullable | 所属主题；null 表示未分类（向后兼容） |

**Migration Notes**:
- 现有记录迁移后 `TopicId = null`，不破坏现有功能
- 添加外键索引 `(TopicId, CreatedAt)` 支持"按主题分页查询消息"场景

---

## DTOs

### `TopicDto`

```csharp
public sealed record TopicDto(
    Guid Id,
    string Name,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMessageAt,
    int MessageCount   // 该主题下消息总数，用于 UI 显示徽标
);
```

### `CreateTopicRequest`

```csharp
public sealed record CreateTopicRequest(
    string Name    // 验证：非空，MaxLength(100)
);
```

### `UpdateTopicRequest`

```csharp
public sealed record UpdateTopicRequest(
    string Name
);
```

### `ReorderTopicsRequest`

```csharp
public sealed record ReorderTopicsRequest(
    IReadOnlyList<TopicOrderItem> Items
);

public sealed record TopicOrderItem(
    Guid TopicId,
    int SortOrder
);
```

### `TopicMessagesResponse`

```csharp
public sealed record TopicMessagesResponse(
    IReadOnlyList<ShareItemDto> Messages,
    bool HasMore,
    string? NextCursor   // ISO 8601 格式的 DateTimeOffset，作为下一页的游标
);
```

---

## State Transitions

```
[创建主题] ──→ Topic(Name, SortOrder=int.MaxValue, LastMessageAt=null)
                │
                ├──→ [发送消息] ──→ ShareItem.TopicId = topic.Id
                │                    Topic.LastMessageAt = message.CreatedAt
                │
                └──→ [拖拽排序] ──→ Topic.SortOrder = newOrder (批量更新)
```

---

## Sorting Logic

侧边栏主题列表排序公式（EF Core LINQ）：

```
ORDER BY Topic.SortOrder ASC,
         Topic.LastMessageAt DESC NULLS LAST,
         Topic.CreatedAt DESC
```

解释：
1. 手动排序权重优先（SortOrder ASC）
2. 相同权重时，最近有消息的主题排前（LastMessageAt DESC）
3. 完全相同时，新建主题排前（CreatedAt DESC）

---

## EF Core Configuration

```csharp
// Topic entity
modelBuilder.Entity<Topic>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
    entity.Property(e => e.SortOrder).IsRequired();
    entity.Property(e => e.CreatedAt).HasConversion(/* DateTimeOffset → string */);
    entity.Property(e => e.LastMessageAt).HasConversion(/* nullable */);
    entity.HasIndex(e => new { e.SortOrder, e.LastMessageAt });
    entity.HasIndex(e => e.CreatedAt);
});

// ShareItem — 新增 FK
modelBuilder.Entity<ShareItem>(entity =>
{
    entity.HasOne<Topic>()
          .WithMany()
          .HasForeignKey(e => e.TopicId)
          .OnDelete(DeleteBehavior.Restrict)
          .IsRequired(false);
    entity.HasIndex(e => new { e.TopicId, e.CreatedAt });
});
```
