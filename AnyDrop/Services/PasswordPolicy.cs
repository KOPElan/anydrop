namespace AnyDrop.Services;

/// <summary>
/// S3-02（SEC-03）：密码策略——仅约束**新**密码。
///
/// 最低 10 位（计划给的 10–12 位区间取下限：单用户自托管，10 位已把
/// 在线猜解成本抬到限流之上，又不至于逼用户写纸上；熵检查本轮不做，
/// 见下方说明）。已有短密码（Stage 0 之前按 6 位建的）仍可正常登录，
/// 登录路径不做长度校验——长度只在创建/修改密码时检查。
/// 熵检查未采用：单用户场景下，长度 + 登录限流已覆盖在线猜解；
/// 离线猜解取决于哈希强度（PBKDF2-SHA256）而非口令熵，熵检查对
/// 用户体验的代价大于收益，留待以后评估。
/// </summary>
public static class PasswordPolicy
{
    /// <summary>新密码最低长度（位）。</summary>
    public const int MinLength = 10;

    /// <summary>
    /// 校验新密码。返回 null 表示通过，否则返回应直接展示给用户的错误文案。
    /// </summary>
    public static string? ValidateNewPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinLength)
        {
            return $"密码长度至少 {MinLength} 位。";
        }

        return null;
    }
}
