using AnyDrop.App.Models;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;

namespace AnyDrop.Tests.Unit.App;

/// <summary>S3-06：应用级连接状态的推导规则（离线 &gt; 重连中 &gt; 已连接 &gt; 未连接）。</summary>
public class ConnectionStateTests
{
    [Theory]
    [InlineData(true, HubConnectionState.Connected, false, ConnectionState.Connected)]
    [InlineData(true, HubConnectionState.Connected, true, ConnectionState.Connected)]
    [InlineData(true, HubConnectionState.Disconnected, true, ConnectionState.Reconnecting)]
    [InlineData(true, HubConnectionState.Reconnecting, true, ConnectionState.Reconnecting)]
    [InlineData(true, HubConnectionState.Connecting, true, ConnectionState.Reconnecting)]
    [InlineData(true, HubConnectionState.Disconnected, false, ConnectionState.NeverConnected)]
    [InlineData(true, HubConnectionState.Reconnecting, false, ConnectionState.NeverConnected)]
    [InlineData(false, HubConnectionState.Connected, true, ConnectionState.Offline)]
    [InlineData(false, HubConnectionState.Disconnected, false, ConnectionState.Offline)]
    [InlineData(false, HubConnectionState.Disconnected, true, ConnectionState.Offline)]
    public void Derive_Matrix(bool isOnline, HubConnectionState hubState, bool hasEverConnected, ConnectionState expected)
    {
        ConnectionStateRules.Derive(isOnline, hubState, hasEverConnected).Should().Be(expected);
    }

    [Fact]
    public void Derive_OfflineWinsOverConnected()
    {
        // 离线时只显示离线横幅，Hub 断开是其必然结果，不再叠加显示重连横幅。
        ConnectionStateRules.Derive(false, HubConnectionState.Connected, true)
            .Should().Be(ConnectionState.Offline);
    }

    [Fact]
    public void Derive_NeverConnectedShowsNoBanner()
    {
        // 启动时尚未连通过：不显示任何横幅，避免初始化时误报（旧 MainLayout 的 _hasEverConnected 语义）。
        ConnectionStateRules.Derive(true, HubConnectionState.Disconnected, false)
            .Should().Be(ConnectionState.NeverConnected);
    }
}
