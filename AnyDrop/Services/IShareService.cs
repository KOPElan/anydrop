using AnyDrop.Models;

namespace AnyDrop.Services;

public interface IShareService
{
    Task<ShareItemDto> SendTextAsync(string content, Guid? topicId = null, CancellationToken ct = default);

    Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default);
}
