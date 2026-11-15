namespace AnyDrop.Models;

/// <summary>
/// 缺失引用修复记录（S1-01，DAT-02）。
///
/// 对账发现「数据库引用、磁盘上不存在」的文件时记一笔，供人工修复：
/// 在设置页的存储健康区定位到对应消息，可删除消息或标记已处理。
/// 文件恢复后下次对账自动标记已解决。绝不自动删数据库行。
/// </summary>
public sealed class MissingFileReport
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>引用该文件的消息 ID（消息删了也不删本记录，供追溯）。</summary>
    public Guid ShareItemId { get; set; }

    /// <summary>缺失的存储相对路径。</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>缺失的是原文件还是缩略图：content | thumbnail。</summary>
    public string Kind { get; set; } = "content";

    public DateTimeOffset FirstDetectedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsResolved { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>解决方式：auto（文件恢复 / 消息已删）、delete-message、dismiss。</summary>
    public string? ResolvedBy { get; set; }
}
