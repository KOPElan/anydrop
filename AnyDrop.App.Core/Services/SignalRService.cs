using AnyDrop.App.Infrastructure;
using AnyDrop.App.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace AnyDrop.App.Services;

/// <summary>
/// SignalR 连接管理，订阅服务端推送事件（S2-04，CLI-02 / WEB-03）。
///
/// 自动重连（WithAutomaticReconnect，最长 ~42 秒）耗尽进入 Closed 后，
/// 不再永久断线：接带抖动的指数退避手动重连循环（1s → 30s 封顶），直到
/// 连接恢复或显式停止。每次重连成功后触发 <see cref="Reconnected"/>，
/// 由页面按当前话题增量补拉断线期消息；断线期间页面降级为轮询。
/// </summary>
public sealed class SignalRService : ISignalRService, IAsyncDisposable
{
    /// <summary>手动重连退避序列（秒）：1、2、4、8、16，之后封顶 30 秒。</summary>
    internal static readonly int[] ReconnectBackoffSeconds = [1, 2, 4, 8, 16, 30];

    /// <summary>按尝试次数取退避延迟（封顶 30 秒），供测试断言。</summary>
    internal static TimeSpan BackoffForAttempt(int attempt)
    {
        var seconds = ReconnectBackoffSeconds[Math.Min(attempt, ReconnectBackoffSeconds.Length - 1)];
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>给退避加 ±25% 抖动，避免多设备同时重连形成惊群。</summary>
    internal static TimeSpan WithJitter(TimeSpan delay)
    {
        var jitter = delay.TotalMilliseconds * (Random.Shared.NextDouble() * 0.5 - 0.25);
        return TimeSpan.FromMilliseconds(Math.Max(100, delay.TotalMilliseconds + jitter));
    }

    private readonly HubConnectionManager _manager;
    private readonly AppEventBus _eventBus;
    private HubConnection? _registeredConnection;
    private bool _stopping;
    private bool _hasEverConnected;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectLoopTask;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly object _reconnectLock = new();

    public SignalRService(HubConnectionManager manager, AppEventBus eventBus)
    {
        _manager = manager;
        _eventBus = eventBus;
        _eventBus.ConnectivityChanged += OnConnectivityChanged;
    }

    public HubConnectionState State => _manager.Connection.State;

    public event Action<HubConnectionState>? StateChanged;
    public event Action<IReadOnlyList<TopicDto>>? TopicsUpdated;
    public event Action<ShareItemDto>? ShareItemReceived;
    public event Action<IReadOnlyList<Guid>>? ShareItemsDeleted;

    /// <summary>
    /// 重连成功后触发（自动重连与手动重连循环均触发）。
    /// 订阅方在此按当前话题增量补拉断线期消息并刷新话题列表。
    /// </summary>
    public event Action? Reconnected;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _stopping = false;
        await _startLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conn = _manager.Connection;
            if (_registeredConnection != conn)
            {
                RegisterHandlers(conn);
                _registeredConnection = conn;
            }

            if (conn.State == HubConnectionState.Disconnected)
            {
                await conn.StartAsync(ct).ConfigureAwait(false);
                _hasEverConnected = true;
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task StopAsync()
    {
        _stopping = true;
        _reconnectCts?.Cancel();
        if (_reconnectLoopTask is not null)
        {
            try { await _reconnectLoopTask.ConfigureAwait(false); } catch { /* 循环取消是预期的 */ }
            _reconnectLoopTask = null;
        }

        if (_manager.Connection.State != HubConnectionState.Disconnected)
        {
            await _manager.Connection.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// S2-05：服务器地址变更后重建连接。旧连接释放后下次 <see cref="StartAsync"/>
    /// 会在新连接上重新注册事件 handler。
    /// </summary>
    public async Task RebuildAsync()
    {
        _stopping = true;
        _reconnectCts?.Cancel();
        await _manager.RebuildAsync().ConfigureAwait(false);
        _registeredConnection = null;
        _hasEverConnected = false;
        _stopping = false;
    }

    private void OnConnectivityChanged(bool isOnline)
    {
        if (!isOnline || _stopping)
        {
            return;
        }

        // 网络恢复：若处于断线状态，立刻触发一次重连尝试（退避循环也会兜底）。
        if (_manager.Connection.State == HubConnectionState.Disconnected)
        {
            _ = ReconnectLoopAsync();
        }
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
            _hasEverConnected = true;
            StateChanged?.Invoke(HubConnectionState.Connected);
            Reconnected?.Invoke();
            return Task.CompletedTask;
        };
        conn.Closed += ex =>
        {
            StateChanged?.Invoke(HubConnectionState.Disconnected);
            // S2-02：过期判断改用状态码，不再靠异常消息里是否含「401」字符串猜。
            if (ex is HttpRequestException httpEx
                && httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _eventBus.RaiseAuthExpired();
                return Task.CompletedTask;
            }

            // 自动重连耗尽才会走到 Closed：接手动重连循环，不永久断线。
            if (!_stopping)
            {
                _ = ReconnectLoopAsync();
            }

            return Task.CompletedTask;
        };

        conn.On<IReadOnlyList<TopicDto>>("TopicsUpdated", topics =>
        {
            TopicsUpdated?.Invoke(topics);
        });

        conn.On<ShareItemDto>("ReceiveShareItem", item =>
        {
            ShareItemReceived?.Invoke(item);
        });

        conn.On<IReadOnlyList<Guid>>("ShareItemsDeleted", ids =>
        {
            ShareItemsDeleted?.Invoke(ids);
        });
    }

    /// <summary>
    /// 手动重连循环：单例运行（重复触发只保留一个循环），指数退避 + 抖动、
    /// 30 秒封顶，直到连接恢复或显式停止。成功后触发 <see cref="Reconnected"/>。
    /// </summary>
    private Task ReconnectLoopAsync()
    {
        lock (_reconnectLock)
        {
            if (_reconnectLoopTask is { IsCompleted: false })
            {
                return _reconnectLoopTask;
            }

            _reconnectCts?.Dispose();
            _reconnectCts = new CancellationTokenSource();
            var ct = _reconnectCts.Token;
            _reconnectLoopTask = RunReconnectLoopAsync(ct);
            return _reconnectLoopTask;
        }
    }

    private async Task RunReconnectLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested && !_stopping)
        {
            var delay = WithJitter(BackoffForAttempt(attempt));
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested || _stopping)
            {
                return;
            }

            try
            {
                await StartAsync(ct).ConfigureAwait(false);
                if (_manager.Connection.State == HubConnectionState.Connected)
                {
                    // StartAsync 成功即视为本次重连完成：若之前断过线，补拉由 Reconnected 触发。
                    if (_hasEverConnected)
                    {
                        Reconnected?.Invoke();
                    }
                    else
                    {
                        _hasEverConnected = true;
                    }

                    StateChanged?.Invoke(HubConnectionState.Connected);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SignalRService] 手动重连第 {attempt + 1} 次失败: {ex.Message}");
            }

            attempt++;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true;
        _eventBus.ConnectivityChanged -= OnConnectivityChanged;
        _reconnectCts?.Cancel();
        await _manager.DisposeAsync().ConfigureAwait(false);
        _startLock.Dispose();
        _reconnectCts?.Dispose();
    }
}
