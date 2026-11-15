namespace AnyDrop.Services;

/// <summary>密码校验结果。</summary>
public enum PasswordVerificationResult
{
    /// <summary>密码不正确，或存储的哈希无法解析。</summary>
    Failed,

    /// <summary>密码正确。</summary>
    Success,

    /// <summary>
    /// 密码正确，但存储的哈希使用了已过时的参数（旧格式，或迭代次数低于当前要求）。
    /// 调用方应在本次登录成功后用当前参数重新计算并保存，从而让哈希随版本自然升级。
    /// </summary>
    SuccessRehashNeeded
}

public interface IPasswordHasherService
{
    /// <summary>
    /// 用当前推荐参数计算密码哈希。
    /// 返回的哈希串自带算法与迭代次数，便于将来提高参数后仍能校验旧密码。
    /// </summary>
    (string Hash, string Salt) HashPassword(string password);

    /// <summary>校验密码，并告知是否需要按当前参数升级存储的哈希。</summary>
    PasswordVerificationResult VerifyPassword(string password, string passwordHash, string passwordSalt);
}
