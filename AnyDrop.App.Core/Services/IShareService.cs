using AnyDrop.App.Models;
using AnyDrop.Shared;

namespace AnyDrop.App.Services;

/// <summary>分享内容的读写 API。</summary>
public interface IShareService
{
    Task<TopicMessagesResponse> GetMessagesAsync(Guid topicId, string? before = null, int limit = 30);

    /// <summary>S2-04：增量补拉——返回 <paramref name="since"/> 之后的新消息（升序）。</summary>
    Task<TopicMessagesResponse> GetMessagesSinceAsync(Guid topicId, DateTimeOffset since, int limit = 100);
    Task<ShareItemDto> SendTextAsync(CreateTextShareItemRequest request);
    Task<Stream> DownloadFileAsync(Guid id);
    Task DeleteMessagesAsync(IReadOnlyList<Guid> ids);
}

