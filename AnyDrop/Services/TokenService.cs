using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AnyDrop.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AnyDrop.Services;

public sealed class TokenService(IOptions<AuthOptions> authOptions) : ITokenService
{
    private readonly AuthOptions _authOptions = authOptions.Value;

    public (string AccessToken, DateTimeOffset ExpiresAt) GenerateToken(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(Math.Max(1, _authOptions.TokenExpiryHours));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_authOptions.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Nickname),
            new Claim("nickname", user.Nickname),
            new Claim("sessionVersion", user.SessionVersion.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _authOptions.JwtIssuer,
            audience: _authOptions.JwtAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return (tokenString, expiresAt);
    }
}
