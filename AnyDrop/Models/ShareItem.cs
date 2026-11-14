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

    /// <summary>阅后即焚：消息自动删除的时间，为 null 表示永久保留。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// 延迟物理删除标记（S0-05，D5）：批量 / 自动清理先写本字段（当前时间 + 7 天），
    /// 消息立即对所有读路径不可见，文件由 Stage 1 的清扫任务在到期后物理删除。
    /// 为 null 表示未标记。单条手动删除不走本字段，直接删行。
    /// </summary>
    public DateTimeOffset? DeleteAfter { get; set; }

    public Guid? TopicId { get; set; }

    /// <summary>缩略图/预览图的存储相对路径，图片或视频上传后由后台任务生成，为 null 表示尚未生成。</summary>
    public string? ThumbnailPath { get; set; }

    public ShareItemDto ToDto() => new(Id, ContentType, Content, FileName, FileSize, MimeType, LinkTitle, LinkDescription, CreatedAt, ExpiresAt, TopicId, ThumbnailPath);
}
