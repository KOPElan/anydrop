namespace AnyDrop.Models;

public sealed record TopicDto(
    Guid Id,
    string Name,
    string Icon,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMessageAt,
    int MessageCount,
    bool IsBuiltIn,
    string? LastMessagePreview,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    DateTimeOffset? ArchivedAt
);

public sealed record CreateTopicRequest(string Name);

public sealed record UpdateTopicRequest(string Name);
public sealed record UpdateTopicIconRequest(string Icon);
public sealed record PinTopicRequest(bool IsPinned);
public sealed record ArchiveTopicRequest(bool IsArchived);

public sealed record ReorderTopicsRequest(IReadOnlyList<TopicOrderItem> Items);

public sealed record TopicOrderItem(Guid TopicId, int SortOrder);

public sealed record TopicMessagesResponse(
    IReadOnlyList<ShareItemDto> Messages,
    bool HasMore,
    string? NextCursor
);
