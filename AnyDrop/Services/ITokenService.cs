using AnyDrop.Models;

namespace AnyDrop.Services;

public interface ITokenService
{
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateToken(User user);
}
