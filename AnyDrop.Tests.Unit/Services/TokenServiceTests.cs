using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;

namespace AnyDrop.Tests.Unit.Services;

public class TokenServiceTests
{
    [Fact]
    public void GenerateToken_ShouldContainRequiredClaims()
    {
        var options = Options.Create(new AuthOptions
        {
            JwtIssuer = "issuer",
            JwtAudience = "aud",
            JwtSecret = "UnitTestJwtSecretValueAtLeast32Chars!",
            TokenExpiryHours = 2
        });
        var sut = new TokenService(options);
        var user = new User { Id = Guid.NewGuid(), Nickname = "Admin", SessionVersion = 3 };

        var (token, expiresAt) = sut.GenerateToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.Should().Contain(x => x.Type == "sub" && x.Value == user.Id.ToString());
        jwt.Claims.Should().Contain(x => x.Type == "sessionVersion" && x.Value == "3");
        expiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }
}
