namespace AnyDrop.App.Services;

/// <summary>安全存储 JWT Token 的接口，底层使用 Android Keystore / iOS Keychain。</summary>
public interface ISecureTokenStorage
{
    /// <summary>保存 Token 及过期时间。</summary>
    Task SaveTokenAsync(string token, DateTimeOffset expiresAt);

    /// <summary>获取 Token；若过期（提前 30 秒判定）或不存在则返回 null。</summary>
    Task<string?> GetTokenAsync();

    /// <summary>保存的 Access Token 过期时间（null 表示未保存）。用于刷新前判断是否临近过期。</summary>
    Task<DateTimeOffset?> GetTokenExpiresAtAsync();

    /// <summary>当前 Token 是否有效。</summary>
    Task<bool> IsAuthenticatedAsync();

    /// <summary>清除已保存的 Token。</summary>
    Task ClearTokenAsync();

    /// <summary>保存 Refresh Token 及过期时间（S2-01）。</summary>
    Task SaveRefreshTokenAsync(string refreshToken, DateTimeOffset expiresAt);

    /// <summary>获取 Refresh Token；若过期或不存在则返回 null。</summary>
    Task<string?> GetRefreshTokenAsync();

    /// <summary>清除已保存的 Refresh Token。</summary>
    Task ClearRefreshTokenAsync();
}
