using System.Globalization;
using System.Security.Cryptography;
using AnyDrop.Models;
using Microsoft.Extensions.Options;

namespace AnyDrop.Services;

/// <summary>
/// 基于 PBKDF2-HMAC-SHA256 的密码哈希。
///
/// 哈希串是自描述的：<c>pbkdf2-sha256$&lt;迭代次数&gt;$&lt;base64 哈希&gt;</c>，盐单独存放。
///
/// 这样做的原因：此前迭代次数是硬编码常量，与哈希串一起存储的只有算法隐含值，
/// 因此**一旦想提高迭代次数就会让所有既有密码失效**，实际上无法演进。
/// 现在校验时以哈希串里记录的参数为准，并在登录成功后按当前参数自动重算（见
/// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>）。
/// </summary>
public sealed class PasswordHasherService : IPasswordHasherService
{
    private const string Algorithm = "pbkdf2-sha256";
    private const char Separator = '$';
    private const int SaltSize = 16;
    private const int KeySize = 32;

    /// <summary>旧格式所使用的迭代次数，仅用于校验历史哈希。</summary>
    private const int LegacyIterations = 100_000;

    /// <summary>允许的哈希字节数范围，避免解析出的异常长度引发巨量内存分配。</summary>
    private const int MinHashBytes = 16;
    private const int MaxHashBytes = 64;

    private readonly int _currentIterations;

    public PasswordHasherService(IOptions<AuthOptions> authOptions)
    {
        // OWASP 对 PBKDF2-HMAC-SHA256 的建议下限为 600,000 次，
        // 默认值见 AuthOptions.PasswordHashIterations；测试会调低以免拖慢用例。
        _currentIterations = Math.Max(1, authOptions.Value.PasswordHashIterations);
    }

    public (string Hash, string Salt) HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        Span<byte> salt = stackalloc byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, _currentIterations, HashAlgorithmName.SHA256, KeySize);

        var encoded = string.Join(
            Separator,
            Algorithm,
            _currentIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(hash));

        return (encoded, Convert.ToBase64String(salt));
    }

    public PasswordVerificationResult VerifyPassword(string password, string passwordHash, string passwordSalt)
    {
        if (string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(passwordHash)
            || string.IsNullOrWhiteSpace(passwordSalt))
        {
            return PasswordVerificationResult.Failed;
        }

        byte[] saltBytes;
        try
        {
            saltBytes = Convert.FromBase64String(passwordSalt);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }

        if (!TryParseHash(passwordHash, out var iterations, out var expectedHash, out var isLegacy))
        {
            return PasswordVerificationResult.Failed;
        }

        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password, saltBytes, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        if (!CryptographicOperations.FixedTimeEquals(derived, expectedHash))
        {
            return PasswordVerificationResult.Failed;
        }

        return isLegacy || iterations < _currentIterations
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private static bool TryParseHash(string passwordHash, out int iterations, out byte[] hashBytes, out bool isLegacy)
    {
        iterations = 0;
        hashBytes = [];
        isLegacy = false;

        var parts = passwordHash.Split(Separator);

        if (parts.Length == 3)
        {
            if (!string.Equals(parts[0], Algorithm, StringComparison.Ordinal)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
                || iterations <= 0)
            {
                return false;
            }

            return TryDecodeHash(parts[2], out hashBytes);
        }

        // 旧格式：整串就是 base64 哈希。
        // base64 字母表不含 '$'，因此历史哈希永远不会被误判为新格式。
        isLegacy = true;
        iterations = LegacyIterations;
        return TryDecodeHash(passwordHash, out hashBytes);
    }

    private static bool TryDecodeHash(string encoded, out byte[] hashBytes)
    {
        hashBytes = [];

        try
        {
            var decoded = Convert.FromBase64String(encoded);
            if (decoded.Length is < MinHashBytes or > MaxHashBytes)
            {
                return false;
            }

            hashBytes = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
