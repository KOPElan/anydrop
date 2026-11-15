using Microsoft.AspNetCore.SignalR.Client;
using AnyDrop.App.Services;

namespace AnyDrop.App.Infrastructure;

/// <summary>
/// Singleton SignalR 连接管理器，管理 HubConnection 生命周期与自动重连。
/// </summary>
public sealed class HubConnectionManager : IAsyncDisposable
{
    private readonly IServerConfigService _serverConfig;
    private readonly ITokenRefreshService _tokenRefresh;
    private HubConnection? _connection;

    public HubConnectionManager(IServerConfigService serverConfig, ITokenRefreshService tokenRefresh)
    {
        _serverConfig = serverConfig;
        _tokenRefresh = tokenRefresh;
    }

    /// <summary>获取当前 HubConnection，按需构建。</summary>
    public HubConnection Connection => _connection ??= BuildConnection();

    /// <summary>销毁现有连接（BaseUrl 变更时调用）。</summary>
    public async Task RebuildAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private HubConnection BuildConnection()
    {
        var hubUrl = _serverConfig.GetHubUrl() ?? "http://localhost:5002/hubs/share";
        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // S2-02：Hub 用数分钟有效的专用短 token（经 TokenRefreshService
                // 缓存与刷新），不再把 24 小时长效 Access Token 交给 SignalR；
                // 拿不到有效 token 时返回 null，让连接直接走过期流程，
                // 不再用非空断言把空值硬传。
                options.AccessTokenProvider = () => _tokenRefresh.GetHubTokenAsync();
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
