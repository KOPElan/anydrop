using System.Security.Cryptography;

namespace AnyDrop.Models;

/// <summary>
/// Refresh Token 记录（S2-01，CLI-01）。
///
/// 只有哈希落库，原始 token 只在签发与轮换那一刻返回给客户端。
/// 旋转使用：每次轮换生成新 token、旧 token 标记已撤销并记录替换者；
/// 已撤销的 token 再次出现即视为重放（token 被盗），整族吊销。
/// 单用户部署下 FamilyId 只有登录会话族的意义：同一用户多设备各用一族，
/// 一族被盗只吊销该族，不影响其它设备。
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>同一登录会话的 token 族。旋转时新 token 继承 FamilyId。</summary>
    public Guid FamilyId { get; set; } = Guid.NewGuid();

    /// <summary>SHA-256 哈希（小写 hex），绝不存明文。</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>轮换后替换本 token 的新 token 哈希；重放检测用。</summary>
    public string? ReplacedByTokenHash { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>生成一个原始 Refresh Token（64 字节随机，Base64Url 编码）。</summary>
    public static string GenerateRawToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>原始 token 的 SHA-256 哈希（小写 hex）。</summary>
    public static string ComputeHash(string rawToken)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
