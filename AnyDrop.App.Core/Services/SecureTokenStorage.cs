using AnyDrop.App.Platform;

namespace AnyDrop.App.Services;

/// <summary>
/// 使用 <see cref="ISecretStore"/>（Android Keystore / iOS Keychain）存储 JWT Token。
/// 读写操作通过 SemaphoreSlim 保证线程安全。
/// Token 值在首次读取后缓存于内存，避免每次调用都触发慢速 Keystore/Keychain IO。
///
/// 注意：此前的实现把回退存储放在 <c>static</c> 字典里，并在类内用
/// <c>#if ANDROID || IOS || ...</c> 选择分支。现在改为注入
/// <see cref="ISecretStore"/>，既消除了平台条件编译，也消除了测试之间
/// 共享静态可变状态的问题。
/// </summary>
public sealed class SecureTokenStorage : ISecureTokenStorage
{
    private const string TokenKey = "anydrop_token";
    private const string ExpiresKey = "anydrop_token_expires";
    private const string RefreshTokenKey = "anydrop_refresh_token";
    private const string RefreshExpiresKey = "anydrop_refresh_token_expires";

    private readonly ISecretStore _secretStore;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // 内存缓存：_cacheLoaded 为 true 后不再读取底层密钥存储
    private bool _cacheLoaded;
    private string? _cachedToken;
    private DateTimeOffset _cachedExpires = DateTimeOffset.MinValue;
    private string? _cachedRefreshToken;
    private DateTimeOffset _cachedRefreshExpires = DateTimeOffset.MinValue;

    public SecureTokenStorage(ISecretStore secretStore) => _secretStore = secretStore;

    /// <summary>
    /// S2-08：密钥库读写抛错不再打断登录与 401 流程——降级为记录加提示，
    /// 内存缓存仍更新，本次会话继续可用；下次读取时再重试底层存储。
    /// </summary>
    public async Task SaveTokenAsync(string token, DateTimeOffset expiresAt)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await _secretStore.SetAsync(TokenKey, token).ConfigureAwait(false);
                await _secretStore.SetAsync(ExpiresKey, expiresAt.ToString("O")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecureTokenStorage] 保存 Access Token 到密钥库失败（仅内存缓存）: {ex.Message}");
            }

            // 同步更新内存缓存，避免下次读取重新访问密钥存储
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
                await LoadFromStoreAsync().ConfigureAwait(false);
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

    public async Task<DateTimeOffset?> GetTokenExpiresAtAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_cacheLoaded)
            {
                await LoadFromStoreAsync().ConfigureAwait(false);
            }

            return _cachedToken is null ? null : _cachedExpires;
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
            try
            {
                await _secretStore.RemoveAsync(TokenKey).ConfigureAwait(false);
                await _secretStore.RemoveAsync(ExpiresKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // S2-08：清除失败不打断登出流程——内存缓存已清，本次会话已失效。
                System.Diagnostics.Debug.WriteLine($"[SecureTokenStorage] 清除 Access Token 失败: {ex.Message}");
            }

            // 清除内存缓存
            _cachedToken = null;
            _cachedExpires = DateTimeOffset.MinValue;
            _cacheLoaded = true; // 标记为已加载（值为 null）
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveRefreshTokenAsync(string refreshToken, DateTimeOffset expiresAt)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await _secretStore.SetAsync(RefreshTokenKey, refreshToken).ConfigureAwait(false);
                await _secretStore.SetAsync(RefreshExpiresKey, expiresAt.ToString("O")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecureTokenStorage] 保存 Refresh Token 到密钥库失败（仅内存缓存）: {ex.Message}");
            }

            _cachedRefreshToken = refreshToken;
            _cachedRefreshExpires = expiresAt;
            _cacheLoaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_cacheLoaded)
            {
                await LoadFromStoreAsync().ConfigureAwait(false);
            }

            if (_cachedRefreshToken is null) return null;
            if (_cachedRefreshExpires <= DateTimeOffset.UtcNow) return null;
            return _cachedRefreshToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearRefreshTokenAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                await _secretStore.RemoveAsync(RefreshTokenKey).ConfigureAwait(false);
                await _secretStore.RemoveAsync(RefreshExpiresKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecureTokenStorage] 清除 Refresh Token 失败: {ex.Message}");
            }

            _cachedRefreshToken = null;
            _cachedRefreshExpires = DateTimeOffset.MinValue;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>首次读取时从密钥库加载 Access Token 与 Refresh Token 到内存缓存。</summary>
    private async Task LoadFromStoreAsync()
    {
        try
        {
            var token = await _secretStore.GetAsync(TokenKey).ConfigureAwait(false);
            var expiresStr = await _secretStore.GetAsync(ExpiresKey).ConfigureAwait(false);
            _cachedToken = token;
            _cachedExpires = DateTimeOffset.TryParse(expiresStr, out var e) ? e : DateTimeOffset.MinValue;

            var refreshToken = await _secretStore.GetAsync(RefreshTokenKey).ConfigureAwait(false);
            var refreshExpiresStr = await _secretStore.GetAsync(RefreshExpiresKey).ConfigureAwait(false);
            _cachedRefreshToken = refreshToken;
            _cachedRefreshExpires = DateTimeOffset.TryParse(refreshExpiresStr, out var re) ? re : DateTimeOffset.MinValue;

            // 仅在成功读取时标记缓存已加载；异常时保持 false，允许下次重试
            _cacheLoaded = true;
        }
        catch
        {
            // Keychain/Keystore 瞬时故障：不设 _cacheLoaded，下次调用时仍会重试
            System.Diagnostics.Debug.WriteLine("[SecureTokenStorage] 密钥存储读取失败，将在下次调用时重试。");
            _cachedToken = null;
            _cachedExpires = DateTimeOffset.MinValue;
            _cachedRefreshToken = null;
            _cachedRefreshExpires = DateTimeOffset.MinValue;
        }
    }
}
