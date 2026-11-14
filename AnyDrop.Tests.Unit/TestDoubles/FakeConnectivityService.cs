using AnyDrop.App.Services;

namespace AnyDrop.Tests.Unit.TestDoubles;

/// <summary>测试用的可控网络状态。</summary>
public sealed class FakeConnectivityService(bool isOnline = true) : IConnectivityService
{
    public bool IsOnline { get; set; } = isOnline;
}
