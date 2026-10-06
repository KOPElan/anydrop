using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
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
        // 固定时钟，使过期时间的断言是确定的（而不是依赖真实时间流逝）
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var sut = new TokenService(options, clock);
        var user = new User { Id = Guid.NewGuid(), Nickname = "Admin", SessionVersion = 3 };

        var (token, expiresAt) = sut.GenerateToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.Should().Contain(x => x.Type == "sub" && x.Value == user.Id.ToString());
        jwt.Claims.Should().Contain(x => x.Type == "sessionVersion" && x.Value == "3");
        expiresAt.Should().Be(clock.UtcNow.AddHours(2));
    }
}
