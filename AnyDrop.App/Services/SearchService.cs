using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.Shared;

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
        var response = await client.GetFromJsonAsync<ApiResponse<TopicMessagesResponse>>(url).ConfigureAwait(false);
        return response?.Data?.Messages ?? [];
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
        // 服务端接口：GET /api/v1/topics/{id}/active-dates?start=yyyy-MM-dd&end=yyyy-MM-dd
        var client = _httpClientFactory.CreateClient("api");
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var url = $"api/v1/topics/{topicId}/active-dates?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}";
        var response = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<DateOnly>>>(url).ConfigureAwait(false);
        return response?.Data ?? [];
    }

    public async Task<IReadOnlyList<ShareItemDto>> GetByTypeAsync(Guid topicId, ShareContentType type, int limit = 20, string? before = null)
    {
        // 服务端接口：GET /api/v1/topics/{id}/messages/by-type?contentType=<int>&limit=<int>[&before=<DateTimeOffset>]
        var client = _httpClientFactory.CreateClient("api");
        var url = $"api/v1/topics/{topicId}/messages/by-type?contentType={(int)type}&limit={limit}";
        if (before is not null) url += $"&before={Uri.EscapeDataString(before)}";
        var response = await client.GetFromJsonAsync<ApiResponse<TopicMessagesResponse>>(url).ConfigureAwait(false);
        return response?.Data?.Messages ?? [];
    }
}

