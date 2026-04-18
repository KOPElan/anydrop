namespace AnyDrop.Models;

/// <summary>
/// Represents a persisted share item in the system.
/// </summary>
public sealed class ShareItem
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    public ShareContentType ContentType { get; set; } = ShareContentType.Text;

    /// <summary>
    /// Gets or sets the main content payload.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional file name for file-based content.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets or sets the optional file size for file-based content.
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// Gets or sets the optional MIME type for file-based content.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the server creation time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Converts this entity to a transport DTO.
    /// </summary>
    /// <returns>A DTO representation of the item.</returns>
    public ShareItemDto ToDto()
    {
        return new ShareItemDto(Id, ContentType, Content, FileName, FileSize, MimeType, CreatedAt);
    }
}
