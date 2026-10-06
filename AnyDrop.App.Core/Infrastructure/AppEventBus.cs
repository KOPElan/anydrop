namespace AnyDrop.App.Infrastructure;

/// <summary>全局 Singleton 事件总线，用于跨组件/服务通信。</summary>
public sealed class AppEventBus
{
    /// <summary>JWT 过期或 401 响应时触发。</summary>
    public event Action? AuthExpired;

    /// <summary>网络连通性变化时触发，参数 true 表示在线。</summary>
    public event Action<bool>? ConnectivityChanged;

    public void RaiseAuthExpired() => AuthExpired?.Invoke();

    public void RaiseConnectivityChanged(bool isOnline) => ConnectivityChanged?.Invoke(isOnline);
}
