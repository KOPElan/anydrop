namespace AnyDrop.Models;

/// <summary>
/// Represents the share item payload used by UI and real-time messages.
/// </summary>
/// <param name="Id">Share item identifier.</param>
/// <param name="ContentType">Shared content type.</param>
/// <param name="Content">Main content payload.</param>
/// <param name="FileName">Optional file name for file content.</param>
/// <param name="FileSize">Optional file size in bytes.</param>
/// <param name="MimeType">Optional MIME type for file content.</param>
/// <param name="CreatedAt">Server-side creation timestamp.</param>
public sealed record ShareItemDto(
    Guid Id,
    ShareContentType ContentType,
    string Content,
    string? FileName,
    long? FileSize,
    string? MimeType,
    DateTimeOffset CreatedAt);
