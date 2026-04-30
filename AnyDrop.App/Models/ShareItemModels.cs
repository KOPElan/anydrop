namespace AnyDrop.App.Models;

public enum ShareContentType
{
    Text = 0,
    File = 1,
    Image = 2,
    Video = 3,
    Link = 4
}

public sealed record ShareItemDto(
    Guid Id,
    Guid TopicId,
    ShareContentType ContentType,
    string? TextContent,
    string? FileName,
    long? FileSize,
    string? MimeType,
    string? LinkUrl,
    string? LinkTitle,
    string? LinkDescription,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record CreateTextShareItemRequest(Guid TopicId, string TextContent);

public sealed record ActiveDatesResponse(IReadOnlyList<DateOnly> Dates);

public sealed record SharedContent(string? Text, IReadOnlyList<string> FilePaths, string? MimeType);
