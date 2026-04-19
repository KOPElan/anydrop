namespace AnyDrop.Api;

public sealed record CreateTextShareItemRequest(string Content, Guid? TopicId);
