using AnyDrop.App.Models;

namespace AnyDrop.App.Infrastructure;

/// <summary>
/// 跨平台静态存储，用于保存待处理的外部分享内容和通知深链接。
/// </summary>
public static class PendingShareStore
{
    private static SharedContent? _content;

    public static SharedContent? Content
    {
        get => _content;
        set => _content = value;
    }

    public static bool HasContent => _content is not null;

    /// <summary>通知点击携带的 topicId，用于深链接跳转。</summary>
    public static Guid? NotificationTopicId { get; set; }

    public static void Clear() => _content = null;
}
