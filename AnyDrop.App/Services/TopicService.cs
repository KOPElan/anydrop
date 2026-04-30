using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.Shared;

namespace AnyDrop.App.Services;

public sealed class TopicService : ITopicService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TopicService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<TopicDto>> GetTopicsAsync()
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<TopicDto>>>("api/v1/topics").ConfigureAwait(false);
        return response?.Data ?? [];
    }

    public async Task<IReadOnlyList<TopicDto>> GetArchivedTopicsAsync()
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<TopicDto>>>("api/v1/topics/archived").ConfigureAwait(false);
        return response?.Data ?? [];
    }

    public async Task<TopicDto> CreateTopicAsync(CreateTopicRequest request)
    {
        var client = _httpClientFactory.CreateClient("api");
        var httpResponse = await client.PostAsJsonAsync("api/v1/topics", request).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<ApiResponse<TopicDto>>().ConfigureAwait(false);
        return response!.Data!;
    }

    public async Task UpdateTopicAsync(Guid id, UpdateTopicRequest request)
    {
        var client = _httpClientFactory.CreateClient("api");
        (await client.PutAsJsonAsync($"api/v1/topics/{id}", request).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    public async Task UpdateTopicIconAsync(Guid id, UpdateTopicIconRequest request)
    {
        // 服务端使用 PUT /api/v1/topics/{id}/icon
        var client = _httpClientFactory.CreateClient("api");
        (await client.PutAsJsonAsync($"api/v1/topics/{id}/icon", request).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    public async Task PinTopicAsync(Guid id, PinTopicRequest request)
    {
        // 服务端使用 PUT /api/v1/topics/{id}/pin
        var client = _httpClientFactory.CreateClient("api");
        (await client.PutAsJsonAsync($"api/v1/topics/{id}/pin", request).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    public async Task ArchiveTopicAsync(Guid id, ArchiveTopicRequest request)
    {
        // 服务端使用 PUT /api/v1/topics/{id}/archive
        var client = _httpClientFactory.CreateClient("api");
        (await client.PutAsJsonAsync($"api/v1/topics/{id}/archive", request).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    public async Task ReorderTopicsAsync(ReorderTopicsRequest request)
    {
        var client = _httpClientFactory.CreateClient("api");
        (await client.PutAsJsonAsync("api/v1/topics/reorder", request).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    public async Task DeleteTopicAsync(Guid id)
    {
        var client = _httpClientFactory.CreateClient("api");
        (await client.DeleteAsync($"api/v1/topics/{id}").ConfigureAwait(false)).EnsureSuccessStatusCode();
    }
}

