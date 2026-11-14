namespace AnyDrop.App.Infrastructure;

/// <summary>全局 Singleton 事件总线，用于跨组件/服务通信。</summary>
public sealed class AppEventBus
{
    /// <summary>JWT 过期或 401 响应时触发（10 秒内去重，见 <see cref="RaiseAuthExpired"/>）。</summary>
    public event Action? AuthExpired;

    /// <summary>网络连通性变化时触发，参数 true 表示在线。</summary>
    public event Action<bool>? ConnectivityChanged;

    private long _lastAuthExpiredTicks;

    /// <summary>
    /// 触发 AuthExpired。并发 401（如 10 个请求同时撞过期）只应跳一次登录页：
    /// 10 秒内的重复触发直接丢弃。
    /// </summary>
    public void RaiseAuthExpired()
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var last = Interlocked.Read(ref _lastAuthExpiredTicks);
        if (now - last < TimeSpan.FromSeconds(10).Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastAuthExpiredTicks, now, last) != last)
        {
            return;
        }

        AuthExpired?.Invoke();
    }

    public void RaiseConnectivityChanged(bool isOnline) => ConnectivityChanged?.Invoke(isOnline);
}
