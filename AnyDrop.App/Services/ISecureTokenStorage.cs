namespace AnyDrop.App.Services;

/// <summary>安全存储 JWT Token 的接口，底层使用 Android Keystore / iOS Keychain。</summary>
public interface ISecureTokenStorage
{
    /// <summary>保存 Token 及过期时间。</summary>
    Task SaveTokenAsync(string token, DateTimeOffset expiresAt);

    /// <summary>获取 Token；若过期（提前 30 秒判定）或不存在则返回 null。</summary>
    Task<string?> GetTokenAsync();

    /// <summary>当前 Token 是否有效。</summary>
    Task<bool> IsAuthenticatedAsync();

    /// <summary>清除已保存的 Token。</summary>
    Task ClearTokenAsync();
}
