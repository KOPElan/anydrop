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
}
