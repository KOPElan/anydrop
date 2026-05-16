namespace AnyDrop.Models;

public sealed record UploadPendingSignal(
    string TempId,
    Guid TopicId,
    string FileName,
    string MimeType,
    long FileSize,
    DateTimeOffset CreatedAt);
