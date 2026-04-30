using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

/// <summary>分享内容的读写 API。</summary>
public interface IShareService
{
    Task<TopicMessagesResponse> GetMessagesAsync(Guid topicId, string? before = null, int limit = 30);
    Task<ShareItemDto> SendTextAsync(CreateTextShareItemRequest request);
    Task<Stream> DownloadFileAsync(Guid id);
}
