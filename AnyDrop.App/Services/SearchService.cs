using System.Net.Http.Json;
using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

public sealed class SearchService : ISearchService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SearchService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<ShareItemDto>> SearchAsync(Guid topicId, string q, int limit = 20, string? before = null)
    {
        var client = _httpClientFactory.CreateClient("api");
        var url = $"api/v1/topics/{topicId}/messages/search?q={Uri.EscapeDataString(q)}&limit={limit}";
        if (before is not null) url += $"&before={Uri.EscapeDataString(before)}";
        var response = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<ShareItemDto>>>(url).ConfigureAwait(false);
        return response?.Data ?? [];
    }

    public async Task<IReadOnlyList<ShareItemDto>> GetByDateAsync(Guid topicId, DateOnly date)
    {
        var client = _httpClientFactory.CreateClient("api");
        var url = $"api/v1/topics/{topicId}/messages/by-date?date={date:yyyy-MM-dd}";
        var response = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<ShareItemDto>>>(url).ConfigureAwait(false);
        return response?.Data ?? [];
    }

    public async Task<IReadOnlyList<DateOnly>> GetActiveDatesAsync(Guid topicId, int year, int month)
    {
        var client = _httpClientFactory.CreateClient("api");
        var url = $"api/v1/topics/{topicId}/messages/active-dates?year={year}&month={month}";
        var response = await client.GetFromJsonAsync<ApiResponse<ActiveDatesResponse>>(url).ConfigureAwait(false);
        return response?.Data?.Dates ?? [];
    }

    public async Task<IReadOnlyList<ShareItemDto>> GetByTypeAsync(Guid topicId, ShareContentType type, int limit = 20, string? before = null)
    {
        var client = _httpClientFactory.CreateClient("api");
        var url = $"api/v1/topics/{topicId}/messages?type={(int)type}&limit={limit}";
        if (before is not null) url += $"&before={Uri.EscapeDataString(before)}";
        var response = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<ShareItemDto>>>(url).ConfigureAwait(false);
        return response?.Data ?? [];
    }
}
