namespace AnyDrop.Models;

public sealed class AuthOptions
{
    public string JwtIssuer { get; set; } = "AnyDrop";
    public string JwtAudience { get; set; } = "AnyDrop.Client";
    public string JwtSecret { get; set; } = string.Empty;
    public int TokenExpiryHours { get; set; } = 24;
    public int LoginMaxFailures { get; set; } = 5;
    public int LoginCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// PBKDF2-HMAC-SHA256 的迭代次数，默认取 OWASP 对 SHA256 的建议下限 600,000。
    ///
    /// 该值会被写进哈希串，因此提高它不会让既有密码失效：
    /// 旧哈希仍按自身记录的次数校验，并在用户下次登录成功时自动升级。
    /// </summary>
    public int PasswordHashIterations { get; set; } = 600_000;
}
