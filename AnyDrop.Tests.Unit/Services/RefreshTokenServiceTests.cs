using System.IdentityModel.Tokens.Jwt;
using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S2-01（CLI-01）：Refresh Token 整链——签发、旋转、重放吊销整族、过期、登出吊销。
/// </summary>
public class RefreshTokenServiceTests
{
    private static readonly AuthOptions Options = new()
    {
        JwtIssuer = "issuer",
        JwtAudience = "aud",
        JwtSecret = "UnitTestJwtSecretValueAtLeast32Chars!",
        TokenExpiryHours = 24,
        RefreshTokenExpiryDays = 30,
        HubTokenExpiryMinutes = 5
    };

    private static (RefreshTokenService Sut, TokenService Tokens, TestDbContextFactory Factory, FakeTimeProvider Clock) CreateSut()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-refresh-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var tokens = new TokenService(Microsoft.Extensions.Options.Options.Create(Options), clock);
        var sut = new RefreshTokenService(factory, tokens, Microsoft.Extensions.Options.Options.Create(Options), clock);
        return (sut, tokens, factory, clock);
    }

    private static async Task<User> SeedUserAsync(TestDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        var user = new User { Nickname = "Admin", PasswordHash = "h", PasswordSalt = "s", SessionVersion = 1 };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task IssueAsync_StoresOnlyHash_AndReturnsRawToken()
    {
        var (sut, _, factory, clock) = CreateSut();
        var user = await SeedUserAsync(factory);

        var (raw, expiresAt) = await sut.IssueAsync(user.Id);

        raw.Should().NotBeNullOrEmpty();
        expiresAt.Should().Be(clock.UtcNow.AddDays(30));

        await using var db = factory.CreateDbContext();
        var stored = await db.RefreshTokens.SingleAsync();
        stored.TokenHash.Should().Be(RefreshToken.ComputeHash(raw));
        stored.TokenHash.Should().NotContain(raw);
        stored.UserId.Should().Be(user.Id);
        stored.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task RotateAsync_Success_RotatesAndOldTokenDies()
    {
        var (sut, _, factory, _) = CreateSut();
        var user = await SeedUserAsync(factory);
        var (raw, _) = await sut.IssueAsync(user.Id);

        var result = await sut.RotateAsync(raw);

        result.Succeeded.Should().BeTrue();
        result.Payload.Should().NotBeNull();
        result.Payload!.AccessToken.Should().NotBeNullOrEmpty();
        result.Payload.RefreshToken.Should().NotBeNullOrEmpty();
        result.Payload.RefreshToken.Should().NotBe(raw);

        // 新 Access Token 是合法 JWT，带 sub
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Payload.AccessToken);
        jwt.Claims.Should().Contain(x => x.Type == "sub" && x.Value == user.Id.ToString());

        // 旧 token 已撤销，再用即失败（但未触发重放——它是正常轮换的旧 token，
        // ReplacedByTokenHash 非空时才会触发重放；此处旧 token 就是被替换的那个，
        // 所以第二次提交应触发整族吊销）
        var replay = await sut.RotateAsync(raw);
        replay.Succeeded.Should().BeFalse();
        replay.ReplayDetected.Should().BeTrue();

        // 整族已吊销：轮换得到的新 token 也失效
        var afterReplay = await sut.RotateAsync(result.Payload.RefreshToken!);
        afterReplay.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RotateAsync_UnknownToken_Fails()
    {
        var (sut, _, _, _) = CreateSut();
        var result = await sut.RotateAsync("not-a-real-token");
        result.Succeeded.Should().BeFalse();
        result.ReplayDetected.Should().BeFalse();
    }

    [Fact]
    public async Task RotateAsync_ExpiredToken_Fails()
    {
        var (sut, _, factory, clock) = CreateSut();
        var user = await SeedUserAsync(factory);
        var (raw, _) = await sut.IssueAsync(user.Id);

        clock.Advance(TimeSpan.FromDays(31));

        var result = await sut.RotateAsync(raw);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("过期");
    }

    [Fact]
    public async Task RevokeAllForUserAsync_InvalidatesIssuedTokens()
    {
        var (sut, _, factory, _) = CreateSut();
        var user = await SeedUserAsync(factory);
        var (raw, _) = await sut.IssueAsync(user.Id);

        await sut.RevokeAllForUserAsync(user.Id);

        var result = await sut.RotateAsync(raw);
        result.Succeeded.Should().BeFalse();
        result.ReplayDetected.Should().BeFalse();
    }

    [Fact]
    public void GenerateHubToken_IsShortLived_AndMarkedHub()
    {
        var (_, tokens, _, clock) = CreateSut();
        var user = new User { Id = Guid.NewGuid(), Nickname = "Admin", SessionVersion = 2 };

        var (token, expiresAt) = tokens.GenerateHubToken(user);

        expiresAt.Should().Be(clock.UtcNow.AddMinutes(5));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(x => x.Type == "token_kind" && x.Value == "hub");
        jwt.Claims.Should().Contain(x => x.Type == "sessionVersion" && x.Value == "2");
    }

    [Fact]
    public void GenerateRefreshToken_IsUniquePerCall()
    {
        var (_, tokens, _, _) = CreateSut();
        var a = tokens.GenerateRefreshToken();
        var b = tokens.GenerateRefreshToken();
        a.Should().NotBe(b);
        a.Length.Should().BeGreaterThan(80);
    }
}
