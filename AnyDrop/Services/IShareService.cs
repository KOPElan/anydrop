using AnyDrop.Models;

namespace AnyDrop.Services;

public interface IShareService
{
    Task<ShareItemDto> SendTextAsync(string content, Guid? topicId = null, bool burnAfterReading = false, CancellationToken ct = default);

    /// <param name="knownFileSize">
    /// 已知的文件大小（字节），用于流不支持 Seek 时（如 JS 拖放流）仍能正确记录文件大小。
    /// </param>
    Task<ShareItemDto> SendFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid? topicId = null,
        long? knownFileSize = null,
        bool burnAfterReading = false,
        CancellationToken ct = default);

    Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default);

    /// <summary>
    /// 在指定主题内按文本内容进行搜索（大小写不敏感的子串匹配）。
    /// </summary>
    Task<TopicMessagesResponse> SearchTopicMessagesAsync(
        Guid topicId,
        string query,
        int limit = 50,
        DateTimeOffset? before = null,
        CancellationToken ct = default);

    /// <summary>
    /// 获取指定主题在某一天内的全部消息（按服务器本地时区确定"一天"的范围）。
    /// </summary>
    Task<IReadOnlyList<ShareItemDto>> GetTopicMessagesByDateAsync(
        Guid topicId,
        DateOnly date,
        CancellationToken ct = default);

    /// <summary>
    /// 获取指定主题在给定日期范围内有消息记录的日期集合（按服务器本地时区）。
    /// </summary>
    Task<IReadOnlyCollection<DateOnly>> GetTopicActiveDatesAsync(
        Guid topicId,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default);

    /// <summary>
    /// 按内容类型获取指定主题的消息（支持游标分页）。
    /// </summary>
    Task<TopicMessagesResponse> GetTopicMessagesByTypeAsync(
        Guid topicId,
        ShareContentType contentType,
        int limit = 50,
        DateTimeOffset? before = null,
        CancellationToken ct = default);

    /// <summary>
    /// 清理指定月数前的消息（同时删除相关文件资源）。
    /// </summary>
    /// <param name="months">清理多少月前的消息（支持 1 / 3 / 6）。</param>
    /// <param name="topicId">可选的主题 ID，为 null 时清理所有主题的消息。</param>
    /// <returns>实际删除的消息数量。</returns>
    Task<int> CleanupOldMessagesAsync(int months, Guid? topicId = null, CancellationToken ct = default);

    /// <summary>
    /// 批量删除指定 ID 的消息（同时删除相关文件资源）。
    /// </summary>
    /// <param name="ids">要删除的消息 ID 列表。</param>
    /// <returns>实际删除的消息数量。</returns>
    Task<int> DeleteShareItemsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
