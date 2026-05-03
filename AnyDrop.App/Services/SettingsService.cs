using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.Shared;

namespace AnyDrop.App.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SettingsService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<SecuritySettingsDto> GetSecuritySettingsAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("api");
            var response = await client.GetFromJsonAsync<ApiEnvelope<SecuritySettingsDto>>("api/v1/settings/security").ConfigureAwait(false);
            return response?.Data ?? new SecuritySettingsDto(false, 0, "zh-CN", false, 12);
        }
        catch
        {
            return new SecuritySettingsDto(false, 0, "zh-CN", false, 12);
        }
    }

    public async Task UpdateSecuritySettingsAsync(UpdateSecuritySettingsRequest request)
    {
        var client = _httpClientFactory.CreateClient("api");
        (await client.PutAsJsonAsync("api/v1/settings/security", request).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    public async Task UpdateNicknameAsync(UpdateNicknameRequest request)
    {
        var client = _httpClientFactory.CreateClient("api");
        (await client.PutAsJsonAsync("api/v1/settings/profile", request).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    public async Task UpdatePasswordAsync(UpdatePasswordRequest request)
    {
        var client = _httpClientFactory.CreateClient("api");
        (await client.PutAsJsonAsync("api/v1/settings/password", request).ConfigureAwait(false)).EnsureSuccessStatusCode();
    }

    public async Task<int> CleanupOldMessagesAsync(int months)
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.DeleteAsync($"api/v1/share-items/cleanup?months={months}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        // 服务端返回 { success, data: { deletedCount }, error }
        var result = await response.Content.ReadFromJsonAsync<ApiEnvelope<CleanupResult>>().ConfigureAwait(false);
        return result?.Data?.DeletedCount ?? 0;
    }
}

