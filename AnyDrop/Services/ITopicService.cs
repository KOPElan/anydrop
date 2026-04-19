using AnyDrop.Models;

namespace AnyDrop.Services;

public interface ITopicService
{
    Task<IReadOnlyList<TopicDto>> GetAllTopicsAsync(CancellationToken ct = default);

    Task<TopicDto> CreateTopicAsync(CreateTopicRequest request, CancellationToken ct = default);

    Task<TopicDto?> UpdateTopicAsync(Guid topicId, UpdateTopicRequest request, CancellationToken ct = default);

    Task<bool> DeleteTopicAsync(Guid topicId, CancellationToken ct = default);

    Task ReorderTopicsAsync(ReorderTopicsRequest request, CancellationToken ct = default);

    Task<TopicDto> PinTopicAsync(Guid topicId, bool isPinned, CancellationToken ct = default);

    Task<TopicMessagesResponse?> GetTopicMessagesAsync(Guid topicId, int limit, DateTimeOffset? before, CancellationToken ct = default);
}
