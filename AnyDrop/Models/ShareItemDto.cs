namespace AnyDrop.Models;

public sealed record ShareItemDto(
    Guid Id,
    ShareContentType ContentType,
    string Content,
    string? FileName,
    long? FileSize,
    string? MimeType,
    string? LinkTitle,
    string? LinkDescription,
    DateTimeOffset CreatedAt,
    Guid? TopicId
);
