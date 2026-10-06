using AnyDrop.App.Platform;

namespace AnyDrop.App.Services;

/// <summary>
/// 服务端 BaseUrl 配置服务。
///
/// 持久化通过 <see cref="IPreferenceStore"/> 完成，因此本类不再包含任何平台
/// 条件编译，可以在普通 net10.0 测试项目中直接构造与断言。
/// </summary>
public sealed class ServerConfigService : IServerConfigService
{
    private const string BaseUrlKey = "anydrop_base_url";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPreferenceStore _preferences;

    public ServerConfigService(IHttpClientFactory httpClientFactory, IPreferenceStore preferences)
    {
        _httpClientFactory = httpClientFactory;
        _preferences = preferences;
    }

    public string? GetBaseUrl() => _preferences.Get(BaseUrlKey);

    public Task SetBaseUrlAsync(string url)
    {
        _preferences.Set(BaseUrlKey, NormalizeUrl(url));
        return Task.CompletedTask;
    }

    public bool HasBaseUrl() => GetBaseUrl() is { Length: > 0 };

    public string? GetHubUrl()
    {
        var baseUrl = GetBaseUrl();
        return baseUrl is null ? null : $"{baseUrl}/hubs/share";
    }

    public async Task<bool> ValidateUrlAsync(string url)
    {
        try
        {
            var normalized = NormalizeUrl(url);
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            // 服务端 /api/v1/auth/setup-status 只支持 GET，不支持 HEAD
            var response = await client.GetAsync($"{normalized}/api/v1/auth/setup-status")
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        return url;
    }
}
