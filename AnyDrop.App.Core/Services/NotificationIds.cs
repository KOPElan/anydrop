namespace AnyDrop.App.Services;

/// <summary>
/// 本地通知 ID 导出：由话题 Guid 稳定导出非负 ID，同一话题始终同一 ID
/// （新通知覆盖旧通知），不同话题互不覆盖。旧实现 Math.Abs(Guid.GetHashCode())
/// 在 GetHashCode() == int.MinValue 时抛 OverflowException，且跨进程不保证稳定。
/// </summary>
public static class NotificationIds
{
    public static int ForTopic(Guid topicId) =>
        BitConverter.ToInt32(topicId.ToByteArray(), 0) & 0x7fffffff;
}
