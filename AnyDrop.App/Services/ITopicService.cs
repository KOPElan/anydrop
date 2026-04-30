using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

/// <summary>主题全生命周期管理接口。</summary>
public interface ITopicService
{
    Task<IReadOnlyList<TopicDto>> GetTopicsAsync();
    Task<IReadOnlyList<TopicDto>> GetArchivedTopicsAsync();
    Task<TopicDto> CreateTopicAsync(CreateTopicRequest request);
    Task UpdateTopicAsync(Guid id, UpdateTopicRequest request);
    Task UpdateTopicIconAsync(Guid id, UpdateTopicIconRequest request);
    Task PinTopicAsync(Guid id, PinTopicRequest request);
    Task ArchiveTopicAsync(Guid id, ArchiveTopicRequest request);
    Task ReorderTopicsAsync(ReorderTopicsRequest request);
    Task DeleteTopicAsync(Guid id);
}
