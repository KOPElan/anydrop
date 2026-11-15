using Microsoft.AspNetCore.SignalR.Client;

namespace AnyDrop.App.Models;

/// <summary>
/// 应用级连接状态（S3-06）：把 Hub 原始状态、网络连通性、“是否曾经连通过”
/// 三个输入收敛成一个界面可直接用的状态。优先级：离线 &gt; 重连中 &gt; 已连接 &gt; 未连接；
/// 界面一次只显示一条横幅——离线时只显示离线横幅（Hub 断开是其必然结果，不再叠加显示）。
///
/// 原始 <see cref="HubConnectionState"/> 仍保留在
/// <see cref="Services.ISignalRService.State"/>，供重连循环与 MessageList 的
/// 轮询降级等内部逻辑使用。
/// </summary>
public enum ConnectionState
{
    /// <summary>尚未建立过连接（启动中）：不显示任何横幅，避免初始化时误报。</summary>
    NeverConnected,

    /// <summary>实时通道已连接。</summary>
    Connected,

    /// <summary>曾经连通过，现已断开或正在重连：显示重连横幅。</summary>
    Reconnecting,

    /// <summary>设备无网络：显示离线横幅。</summary>
    Offline
}

/// <summary>应用级连接状态的推导规则（纯函数，供 <see cref="Services.ISignalRService"/> 与测试共用）。</summary>
public static class ConnectionStateRules
{
    public static ConnectionState Derive(bool isOnline, HubConnectionState hubState, bool hasEverConnected)
    {
        if (!isOnline)
            return ConnectionState.Offline;
        if (hubState == HubConnectionState.Connected)
            return ConnectionState.Connected;
        if (hasEverConnected)
            return ConnectionState.Reconnecting;
        return ConnectionState.NeverConnected;
    }
}
