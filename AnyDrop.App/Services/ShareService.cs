using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.Shared;

namespace AnyDrop.App.Services;

public sealed class ShareService : IShareService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ShareService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<TopicMessagesResponse> GetMessagesAsync(Guid topicId, string? before = null, int limit = 30)
    {
        var client = _httpClientFactory.CreateClient("api");
        var url = $"api/v1/topics/{topicId}/messages?limit={limit}";
        if (before is not null)
            url += $"&before={Uri.EscapeDataString(before)}";

        var response = await client.GetFromJsonAsync<ApiEnvelope<TopicMessagesResponse>>(url).ConfigureAwait(false);
        return response?.Data ?? new TopicMessagesResponse([], false, null);
    }

    public async Task<ShareItemDto> SendTextAsync(CreateTextShareItemRequest request)
    {
        // 服务端接口：POST /api/v1/share-items/text，请求体 { content, topicId }
        var client = _httpClientFactory.CreateClient("api");
        var httpResponse = await client.PostAsJsonAsync("api/v1/share-items/text", request).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<ApiEnvelope<ShareItemDto>>().ConfigureAwait(false);
        return response!.Data!;
    }

    public async Task<Stream> DownloadFileAsync(Guid id)
    {
        var client = _httpClientFactory.CreateClient("api");
        return await client.GetStreamAsync($"api/v1/share-items/{id}/file?download=true").ConfigureAwait(false);
    }

    public async Task DeleteMessagesAsync(List<Guid> ids)
    {
        var client = _httpClientFactory.CreateClient("api");
        var request = new HttpRequestMessage(HttpMethod.Delete, "api/v1/share-items/batch")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new BatchDeleteRequest(ids))
        };
        var response = await client.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

