namespace AnyDrop.App.Services;

/// <summary>管理服务端 BaseUrl 配置。</summary>
public interface IServerConfigService
{
    /// <summary>获取已配置的 BaseUrl（无尾斜杠），未配置则返回 null。</summary>
    string? GetBaseUrl();

    /// <summary>保存 BaseUrl（自动标准化）。</summary>
    Task SetBaseUrlAsync(string url);

    /// <summary>是否已配置 BaseUrl。</summary>
    bool HasBaseUrl();

    /// <summary>获取 SignalR Hub 完整 URL。</summary>
    string? GetHubUrl();

    /// <summary>发送 HEAD 请求验证服务端可达性。</summary>
    Task<bool> ValidateUrlAsync(string url);
}
