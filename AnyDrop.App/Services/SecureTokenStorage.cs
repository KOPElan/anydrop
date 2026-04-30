namespace AnyDrop.App.Services;

/// <summary>
/// 使用 SecureStorage（Android Keystore / iOS Keychain）存储 JWT Token。
/// 读写操作通过 SemaphoreSlim 保证线程安全。
/// </summary>
public sealed class SecureTokenStorage : ISecureTokenStorage
{
    private const string TokenKey = "anydrop_token";
    private const string ExpiresKey = "anydrop_token_expires";
    private readonly SemaphoreSlim _lock = new(1, 1);

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
            string? token;
            string? expiresStr;
#if ANDROID || IOS || MACCATALYST || WINDOWS
            try
            {
                token = await SecureStorage.GetAsync(TokenKey).ConfigureAwait(false);
                expiresStr = await SecureStorage.GetAsync(ExpiresKey).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
#else
            _fallback.TryGetValue(TokenKey, out token);
            _fallback.TryGetValue(ExpiresKey, out expiresStr);
#endif
            if (token is null || expiresStr is null)
                return null;

            if (DateTimeOffset.TryParse(expiresStr, out var expires)
                && expires - DateTimeOffset.UtcNow <= TimeSpan.FromSeconds(30))
                return null;

            return token;
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
            await Task.CompletedTask.ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
