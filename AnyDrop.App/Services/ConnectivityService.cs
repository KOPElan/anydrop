using AnyDrop.App.Infrastructure;

namespace AnyDrop.App.Services;

/// <summary>
/// 订阅 Connectivity.ConnectivityChanged，变更时触发 AppEventBus。
/// </summary>
public sealed class ConnectivityService : IConnectivityService, IDisposable
{
    private readonly AppEventBus _eventBus;
    private bool _isOnline;

    public ConnectivityService(AppEventBus eventBus)
    {
        _eventBus = eventBus;
#if ANDROID || IOS || MACCATALYST || WINDOWS
        _isOnline = Connectivity.NetworkAccess == NetworkAccess.Internet;
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
#else
        _isOnline = true;
#endif
    }

    public bool IsOnline => _isOnline;

#if ANDROID || IOS || MACCATALYST || WINDOWS
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        _isOnline = e.NetworkAccess == NetworkAccess.Internet;
        _eventBus.RaiseConnectivityChanged(_isOnline);
    }
#endif

    public void Dispose()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;
#endif
    }
}
