namespace AnyDrop.Models;

public sealed class ShareItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ShareContentType ContentType { get; set; } = ShareContentType.Text;

    public string Content { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public long? FileSize { get; set; }

    public string? MimeType { get; set; }

    /// <summary>链接消息的 OGP/meta 标题。</summary>
    public string? LinkTitle { get; set; }

    /// <summary>链接消息的 OGP/meta 描述。</summary>
    public string? LinkDescription { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? TopicId { get; set; }

    public ShareItemDto ToDto() => new(Id, ContentType, Content, FileName, FileSize, MimeType, LinkTitle, LinkDescription, CreatedAt, TopicId);
}
