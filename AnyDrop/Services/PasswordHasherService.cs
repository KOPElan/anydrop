using System.Security.Cryptography;

namespace AnyDrop.Services;

public sealed class PasswordHasherService : IPasswordHasherService
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public (string Hash, string Salt) HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        Span<byte> salt = stackalloc byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public bool VerifyPassword(string password, string passwordHash, string passwordSalt)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(passwordHash) ||
            string.IsNullOrWhiteSpace(passwordSalt))
        {
            return false;
        }

        byte[] saltBytes;
        byte[] hashBytes;
        try
        {
            saltBytes = Convert.FromBase64String(passwordSalt);
            hashBytes = Convert.FromBase64String(passwordHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var derived = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, hashBytes.Length);
        return CryptographicOperations.FixedTimeEquals(derived, hashBytes);
    }
}
