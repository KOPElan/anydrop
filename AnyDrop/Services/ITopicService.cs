using AnyDrop.Models;

namespace AnyDrop.Services;

public interface ITopicService
{
    Task<IReadOnlyList<TopicDto>> GetAllTopicsAsync(CancellationToken ct = default);

    /// <summary>按 ID 获取单个主题（含已归档），若不存在则返回 null。</summary>
    Task<TopicDto?> GetTopicByIdAsync(Guid topicId, CancellationToken ct = default);

    Task<IReadOnlyList<TopicDto>> GetArchivedTopicsAsync(CancellationToken ct = default);

    Task<TopicDto> CreateTopicAsync(CreateTopicRequest request, CancellationToken ct = default);

    Task<TopicDto?> UpdateTopicAsync(Guid topicId, UpdateTopicRequest request, CancellationToken ct = default);

    Task<bool> DeleteTopicAsync(Guid topicId, CancellationToken ct = default);

    Task ReorderTopicsAsync(ReorderTopicsRequest request, CancellationToken ct = default);

    Task<TopicDto> PinTopicAsync(Guid topicId, bool isPinned, CancellationToken ct = default);

    Task<TopicDto> ArchiveTopicAsync(Guid topicId, bool isArchived, CancellationToken ct = default);

    Task<TopicMessagesResponse?> GetTopicMessagesAsync(Guid topicId, int limit, DateTimeOffset? before, CancellationToken ct = default);
}
