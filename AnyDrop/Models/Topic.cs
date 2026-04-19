namespace AnyDrop.Models;

public sealed class Topic
{
    /// <summary>内置"默认"主题的固定 ID，用于启动时幂等写入。</summary>
    public static readonly Guid BuiltInDefaultId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; } = int.MaxValue;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastMessageAt { get; set; }

    /// <summary>当前主题是否为内置主题（不可删除）。</summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>最新一条消息的预览文本（数据库限制 100 字符，写入时截断至 80 字符），由 ShareService 在写入消息时同步更新。</summary>
    public string? LastMessagePreview { get; set; }

    /// <summary>是否置顶。</summary>
    public bool IsPinned { get; set; }

    /// <summary>置顶时间（用于置顶主题内排序）。</summary>
    public DateTimeOffset? PinnedAt { get; set; }

    /// <summary>是否已归档（归档后不参与主题排序，不在普通列表中显示）。</summary>
    public bool IsArchived { get; set; }

    /// <summary>归档时间。</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
}
