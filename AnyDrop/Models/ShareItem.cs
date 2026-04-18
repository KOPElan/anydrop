namespace AnyDrop.Models;

public sealed class ShareItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ShareContentType ContentType { get; set; } = ShareContentType.Text;

    public string Content { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public long? FileSize { get; set; }

    public string? MimeType { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ShareItemDto ToDto() => new(Id, ContentType, Content, FileName, FileSize, MimeType, CreatedAt);
}
