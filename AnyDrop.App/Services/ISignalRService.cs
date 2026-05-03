using AnyDrop.App.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace AnyDrop.App.Services;

/// <summary>管理 SignalR 连接状态与事件订阅。</summary>
public interface ISignalRService
{
    /// <summary>启动 SignalR 连接。</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止 SignalR 连接。</summary>
    Task StopAsync();

    /// <summary>当前连接状态。</summary>
    HubConnectionState State { get; }

    /// <summary>连接状态变化时触发。</summary>
    event Action<HubConnectionState>? StateChanged;

    /// <summary>收到 TopicsUpdated 推送时触发。</summary>
    event Action<IReadOnlyList<TopicDto>>? TopicsUpdated;
}
