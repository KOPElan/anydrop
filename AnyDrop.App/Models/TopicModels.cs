namespace AnyDrop.App.Models;

public sealed record TopicDto(
    Guid Id,
    string Name,
    string Icon,
    bool IsPinned,
    bool IsArchived,
    int SortOrder,
    int MessageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateTopicRequest(string Name, string Icon = "📁");

public sealed record UpdateTopicRequest(string Name);

public sealed record UpdateTopicIconRequest(string Icon);

public sealed record PinTopicRequest(bool IsPinned);

public sealed record ArchiveTopicRequest(bool IsArchived);

public sealed record ReorderTopicsRequest(IReadOnlyList<TopicOrderItem> Items);

public sealed record TopicOrderItem(Guid Id, int SortOrder);

public sealed record TopicMessagesResponse(
    IReadOnlyList<ShareItemDto> Items,
    bool HasMore,
    string? NextCursor);
