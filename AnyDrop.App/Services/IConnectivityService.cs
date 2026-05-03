namespace AnyDrop.App.Services;

/// <summary>监听网络连通性状态。</summary>
public interface IConnectivityService
{
    /// <summary>当前是否有网络连接。</summary>
    bool IsOnline { get; }
}
