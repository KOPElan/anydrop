using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

/// <summary>内容搜索接口。</summary>
public interface ISearchService
{
    Task<IReadOnlyList<ShareItemDto>> SearchAsync(Guid topicId, string q, int limit = 20, string? before = null);
    Task<IReadOnlyList<ShareItemDto>> GetByDateAsync(Guid topicId, DateOnly date);
    Task<IReadOnlyList<DateOnly>> GetActiveDatesAsync(Guid topicId, int year, int month);
    Task<IReadOnlyList<ShareItemDto>> GetByTypeAsync(Guid topicId, ShareContentType type, int limit = 20, string? before = null);
}
