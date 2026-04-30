namespace AnyDrop.App.Services;

/// <summary>
/// 使用 Preferences 持久化服务端 BaseUrl 配置。
/// </summary>
public sealed class ServerConfigService : IServerConfigService
{
    private const string BaseUrlKey = "anydrop_base_url";
    private readonly IHttpClientFactory _httpClientFactory;

    // net10.0 回退存储（实例字段，保证测试隔离）
    private readonly Dictionary<string, string> _prefs = new();

    public ServerConfigService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string? GetBaseUrl()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        return Preferences.Get(BaseUrlKey, null);
#else
        _prefs.TryGetValue(BaseUrlKey, out var val);
        return val;
#endif
    }

    public async Task SetBaseUrlAsync(string url)
    {
        var normalized = NormalizeUrl(url);
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Preferences.Set(BaseUrlKey, normalized);
#else
        _prefs[BaseUrlKey] = normalized;
#endif
        await Task.CompletedTask.ConfigureAwait(false);
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
            var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, $"{normalized}/api/v1/auth/setup-status"))
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
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
