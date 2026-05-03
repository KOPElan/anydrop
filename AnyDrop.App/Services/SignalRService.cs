using AnyDrop.App.Infrastructure;
using AnyDrop.App.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace AnyDrop.App.Services;

/// <summary>SignalR 连接管理，订阅服务端推送事件。</summary>
public sealed class SignalRService : ISignalRService, IAsyncDisposable
{
    private readonly HubConnectionManager _manager;
    private readonly AppEventBus _eventBus;
    private bool _handlersRegistered;

    public SignalRService(HubConnectionManager manager, AppEventBus eventBus)
    {
        _manager = manager;
        _eventBus = eventBus;
    }

    public HubConnectionState State => _manager.Connection.State;

    public event Action<HubConnectionState>? StateChanged;
    public event Action<IReadOnlyList<TopicDto>>? TopicsUpdated;

    public async Task StartAsync(CancellationToken ct = default)
    {
        var conn = _manager.Connection;
        if (!_handlersRegistered)
        {
            RegisterHandlers(conn);
            _handlersRegistered = true;
        }

        if (conn.State == HubConnectionState.Disconnected)
            await conn.StartAsync(ct).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (_manager.Connection.State != HubConnectionState.Disconnected)
            await _manager.Connection.StopAsync().ConfigureAwait(false);
    }

    private void RegisterHandlers(HubConnection conn)
    {
        conn.Reconnecting += _ =>
        {
            StateChanged?.Invoke(HubConnectionState.Reconnecting);
            return Task.CompletedTask;
        };
        conn.Reconnected += _ =>
        {
            StateChanged?.Invoke(HubConnectionState.Connected);
            return Task.CompletedTask;
        };
        conn.Closed += ex =>
        {
            StateChanged?.Invoke(HubConnectionState.Disconnected);
            if (ex?.Message?.Contains("401") == true || ex?.Message?.Contains("Unauthorized") == true)
                _eventBus.RaiseAuthExpired();
            return Task.CompletedTask;
        };

        conn.On<IReadOnlyList<TopicDto>>("TopicsUpdated", topics =>
        {
            TopicsUpdated?.Invoke(topics);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync().ConfigureAwait(false);
    }
}
