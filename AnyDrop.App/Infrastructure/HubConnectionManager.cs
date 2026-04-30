using Microsoft.AspNetCore.SignalR.Client;
using AnyDrop.App.Services;

namespace AnyDrop.App.Infrastructure;

/// <summary>
/// Singleton SignalR 连接管理器，管理 HubConnection 生命周期与自动重连。
/// </summary>
public sealed class HubConnectionManager : IAsyncDisposable
{
    private readonly IServerConfigService _serverConfig;
    private readonly ISecureTokenStorage _tokenStorage;
    private HubConnection? _connection;

    public HubConnectionManager(IServerConfigService serverConfig, ISecureTokenStorage tokenStorage)
    {
        _serverConfig = serverConfig;
        _tokenStorage = tokenStorage;
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
                options.AccessTokenProvider = () => _tokenStorage.GetTokenAsync()!;
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
