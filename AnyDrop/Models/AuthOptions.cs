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

    /// <summary>
    /// 渐进冷却的上限（秒）。同一来源反复触发锁定时，冷却时间按
    /// <c>LoginCooldownSeconds × 2^(n-1)</c> 递增，但不超过本值。
    /// </summary>
    public int LoginMaxCooldownSeconds { get; set; } = 900;

    /// <summary>
    /// Cookie 的 Secure 策略（<c>Always</c> / <c>SameAsRequest</c> / <c>None</c>）。
    /// 默认 <c>Always</c>：TLS 反代部署下 Cookie 只走 HTTPS。
    /// 纯 HTTP 局域网例外需显式改为 <c>None</c>，否则浏览器不会回传 Cookie、登录会失效。
    /// </summary>
    public string CookieSecurePolicy { get; set; } = "Always";
}
