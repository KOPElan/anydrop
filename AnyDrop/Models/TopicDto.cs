namespace AnyDrop.Models;

public sealed record TopicDto(
    Guid Id,
    string Name,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMessageAt,
    int MessageCount,
    bool IsBuiltIn,
    string? LastMessagePreview
);

public sealed record CreateTopicRequest(string Name);

public sealed record UpdateTopicRequest(string Name);

public sealed record ReorderTopicsRequest(IReadOnlyList<TopicOrderItem> Items);

public sealed record TopicOrderItem(Guid TopicId, int SortOrder);

public sealed record TopicMessagesResponse(
    IReadOnlyList<ShareItemDto> Messages,
    bool HasMore,
    string? NextCursor
);
