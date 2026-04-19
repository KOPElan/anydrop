namespace AnyDrop.Services;

public interface IPasswordHasherService
{
    (string Hash, string Salt) HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash, string passwordSalt);
}
