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

    /// <summary>S2-05：服务器地址变更后重建底层 HubConnection（重新注册 handler）。</summary>
    Task RebuildAsync();

    /// <summary>当前连接状态。</summary>
    HubConnectionState State { get; }

    /// <summary>
    /// S3-06：应用级连接状态（Hub 状态 + 网络连通性 + 是否曾经连通过），
    /// 界面横幅直接按它显示，不必再自己拼三个输入。
    /// </summary>
    ConnectionState ConnectionState { get; }

    /// <summary>连接状态变化时触发。</summary>
    event Action<HubConnectionState>? StateChanged;

    /// <summary>S3-06：应用级连接状态变化时触发（仅在推导值真正变化时触发）。</summary>
    event Action<ConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// S2-04：重连成功后触发（自动重连与手动重连循环均触发）。
    /// 订阅方在此按当前话题增量补拉断线期消息并刷新话题列表。
    /// </summary>
    event Action? Reconnected;

    /// <summary>收到 TopicsUpdated 推送时触发。</summary>
    event Action<IReadOnlyList<TopicDto>>? TopicsUpdated;

    /// <summary>收到 ReceiveShareItem 直推时触发（实时新消息）。</summary>
    event Action<ShareItemDto>? ShareItemReceived;

    /// <summary>收到 ShareItemsDeleted 推送时触发。</summary>
    event Action<IReadOnlyList<Guid>>? ShareItemsDeleted;
}
