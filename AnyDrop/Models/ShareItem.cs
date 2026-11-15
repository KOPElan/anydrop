namespace AnyDrop.Models;

/// <summary>
/// 文件上传状态（S1-01，DAT-02）。
/// 上传先建 <see cref="Uploading"/> 预占位行再落盘，落盘后转正为 <see cref="Ready"/>。
/// 崩溃留下的 Uploading 行由对账任务识别并清理（24 小时宽限期后删行删文件），
/// 而不是在磁盘上留一个没有任何数据库行引用的孤儿文件。
/// </summary>
public enum UploadState
{
    /// <summary>预占位：数据库行已建，文件尚未落盘或尚未确认。</summary>
    Uploading = 0,

    /// <summary>正常：文件已落盘确认。</summary>
    Ready = 1
}

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

    /// <summary>定时销毁：消息自动删除的时间，为 null 表示永久保留。注意是按时间到期销毁，不是阅读后销毁。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// 延迟物理删除标记（S0-05，D5）：批量 / 自动清理先写本字段（当前时间 + 7 天），
    /// 消息立即对所有读路径不可见，文件由 Stage 1 的清扫任务在到期后物理删除。
    /// 为 null 表示未标记。单条手动删除不走本字段，直接删行。
    /// </summary>
    public DateTimeOffset? DeleteAfter { get; set; }

    /// <summary>
    /// 上传状态（S1-01，DAT-02）。默认 <see cref="UploadState.Ready"/>：
    /// 文本消息与历史数据都没有上传过程。文件上传先建 Uploading 预占位行，
    /// 落盘后转正；全局查询过滤使 Uploading 行对所有读路径不可见。
    /// </summary>
    public UploadState UploadState { get; set; } = UploadState.Ready;

    public Guid? TopicId { get; set; }

    /// <summary>缩略图/预览图的存储相对路径，图片或视频上传后由后台任务生成，为 null 表示尚未生成。</summary>
    public string? ThumbnailPath { get; set; }

    public ShareItemDto ToDto() => new(Id, ContentType, Content, FileName, FileSize, MimeType, LinkTitle, LinkDescription, CreatedAt, ExpiresAt, TopicId, ThumbnailPath);
}
