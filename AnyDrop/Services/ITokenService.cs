using AnyDrop.Models;

namespace AnyDrop.Services;

public interface ITokenService
{
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateToken(User user);

    /// <summary>生成一个原始 Refresh Token（仅返回一次，调用方负责落库哈希）。</summary>
    string GenerateRefreshToken();

    /// <summary>
    /// 生成 Hub 专用短 token（S2-02）：与普通 Access Token 同样的 sub/sessionVersion
    /// 校验，但有效期只有数分钟（<see cref="AuthOptions.HubTokenExpiryMinutes"/>），
    /// 并带 token_kind=hub 标记。SignalR 协商时用，泄露窗口远小于长效 token。
    /// </summary>
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateHubToken(User user);
}
