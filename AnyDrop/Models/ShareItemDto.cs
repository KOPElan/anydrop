namespace AnyDrop.Models;

public sealed record ShareItemDto(
    Guid Id,
    ShareContentType ContentType,
    string Content,
    string? FileName,
    long? FileSize,
    string? MimeType,
    DateTimeOffset CreatedAt,
    Guid? TopicId
);
