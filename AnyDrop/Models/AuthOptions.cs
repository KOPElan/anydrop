namespace AnyDrop.Models;

public sealed class AuthOptions
{
    public string JwtIssuer { get; set; } = "AnyDrop";
    public string JwtAudience { get; set; } = "AnyDrop.Client";
    public string JwtSecret { get; set; } = string.Empty;
    public int TokenExpiryHours { get; set; } = 24;
    public int LoginMaxFailures { get; set; } = 5;
    public int LoginCooldownSeconds { get; set; } = 60;
}
