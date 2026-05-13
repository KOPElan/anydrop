namespace AnyDrop.App.Services;

/// <summary>
/// 使用 SecureStorage（Android Keystore / iOS Keychain）存储 JWT Token。
/// 读写操作通过 SemaphoreSlim 保证线程安全。
/// Token 值在首次读取后缓存于内存，避免每次调用都触发慢速 Keystore/Keychain IO。
/// </summary>
public sealed class SecureTokenStorage : ISecureTokenStorage
{
    private const string TokenKey = "anydrop_token";
    private const string ExpiresKey = "anydrop_token_expires";
    private readonly SemaphoreSlim _lock = new(1, 1);

    // 内存缓存：_cacheLoaded 为 true 后不再读取 SecureStorage
    private bool _cacheLoaded;
    private string? _cachedToken;
    private DateTimeOffset _cachedExpires = DateTimeOffset.MinValue;

    // 用于 net10.0 测试目标的内存回退
    private static readonly Dictionary<string, string> _fallback = new();

    public async Task SaveTokenAsync(string token, DateTimeOffset expiresAt)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
#if ANDROID || IOS || MACCATALYST || WINDOWS
            await SecureStorage.SetAsync(TokenKey, token).ConfigureAwait(false);
            await SecureStorage.SetAsync(ExpiresKey, expiresAt.ToString("O")).ConfigureAwait(false);
#else
            _fallback[TokenKey] = token;
            _fallback[ExpiresKey] = expiresAt.ToString("O");
            await Task.CompletedTask.ConfigureAwait(false);
#endif
            // 同步更新内存缓存，避免下次读取重新访问 SecureStorage
            _cachedToken = token;
            _cachedExpires = expiresAt;
            _cacheLoaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetTokenAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_cacheLoaded)
            {
                // 首次调用：从 SecureStorage 加载并写入内存缓存
#if ANDROID || IOS || MACCATALYST || WINDOWS
                try
                {
                    var token = await SecureStorage.GetAsync(TokenKey).ConfigureAwait(false);
                    var expiresStr = await SecureStorage.GetAsync(ExpiresKey).ConfigureAwait(false);
                    _cachedToken = token;
                    _cachedExpires = DateTimeOffset.TryParse(expiresStr, out var e) ? e : DateTimeOffset.MinValue;
                }
                catch
                {
                    _cachedToken = null;
                    _cachedExpires = DateTimeOffset.MinValue;
                }
#else
                _fallback.TryGetValue(TokenKey, out var fallbackToken);
                _fallback.TryGetValue(ExpiresKey, out var fallbackExpires);
                _cachedToken = fallbackToken;
                _cachedExpires = DateTimeOffset.TryParse(fallbackExpires, out var fe) ? fe : DateTimeOffset.MinValue;
#endif
                _cacheLoaded = true;
            }

            if (_cachedToken is null) return null;
            if (_cachedExpires - DateTimeOffset.UtcNow <= TimeSpan.FromSeconds(30)) return null;
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> IsAuthenticatedAsync()
        => await GetTokenAsync().ConfigureAwait(false) is not null;

    public async Task ClearTokenAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
#if ANDROID || IOS || MACCATALYST || WINDOWS
            SecureStorage.Remove(TokenKey);
            SecureStorage.Remove(ExpiresKey);
#else
            _fallback.Remove(TokenKey);
            _fallback.Remove(ExpiresKey);
#endif
            // 清除内存缓存
            _cachedToken = null;
            _cachedExpires = DateTimeOffset.MinValue;
            _cacheLoaded = true; // 标记为已加载（值为 null）
            await Task.CompletedTask.ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
