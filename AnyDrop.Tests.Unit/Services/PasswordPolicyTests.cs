using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S3-02（SEC-03）：密码策略只约束新密码——最低 10 位，改密新旧不同；
/// 已有短密码仍能登录；登录路径不做长度校验。
/// </summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short9chr")]
    public void ValidateNewPassword_TooShort_ReturnsError(string? password)
        => PasswordPolicy.ValidateNewPassword(password).Should().Be("密码长度至少 10 位。");

    [Fact]
    public void ValidateNewPassword_Exactly10Chars_Passes()
        => PasswordPolicy.ValidateNewPassword("0123456789").Should().BeNull();

    [Fact]
    public async Task SetupAsync_PasswordShorterThan10_Returns400WithNewMinimum()
    {
        var factory = CreateDbContextFactory();
        var sut = CreateSut(factory);

        var result = await sut.SetupAsync(new SetupRequest("Admin", "Short9chr", "Short9chr"), "k");

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be("密码长度至少 10 位。");
    }

    [Fact]
    public async Task SetupAsync_PasswordExactly10Chars_Succeeds()
    {
        var factory = CreateDbContextFactory();
        var sut = CreateSut(factory);

        var result = await sut.SetupAsync(new SetupRequest("Admin", "0123456789", "0123456789"), "k");

        result.Succeeded.Should().BeTrue();
        result.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task UpdatePasswordAsync_NewPasswordSameAsCurrent_Returns400()
    {
        var factory = CreateDbContextFactory();
        var sut = CreateSut(factory);
        await sut.SetupAsync(new SetupRequest("Admin", "Password1!", "Password1!"), "k");
        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync();

        var result = await sut.UpdatePasswordAsync(
            user.Id, new UpdatePasswordRequest("Password1!", "Password1!", "Password1!"));

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be("新密码不能与当前密码相同。");
    }

    [Fact]
    public async Task UpdatePasswordAsync_NewPasswordTooShort_Returns400()
    {
        var factory = CreateDbContextFactory();
        var sut = CreateSut(factory);
        await sut.SetupAsync(new SetupRequest("Admin", "Password1!", "Password1!"), "k");
        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync();

        var result = await sut.UpdatePasswordAsync(
            user.Id, new UpdatePasswordRequest("Password1!", "Short9chr", "Short9chr"));

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be("密码长度至少 10 位。");
    }

    [Fact]
    public async Task LoginAsync_PreExistingShortPassword_StillLogsIn()
    {
        // Stage 0 之前按 6 位建的用户：登录路径不做长度校验，必须仍能登录。
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var hasher = new PasswordHasherService();
        var (hash, salt) = hasher.HashPassword("Abc123");
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User
        {
            Nickname = "Admin",
            PasswordHash = hash,
            PasswordSalt = salt,
            SessionVersion = 1,
            CreatedAt = now,
            LastLoginAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(factory);

        var result = await sut.LoginAsync(new LoginRequest("Abc123", "/"), "k");

        result.Succeeded.Should().BeTrue();
    }

    private static AuthService CreateSut(TestDbContextFactory factory)
    {
        var userService = new UserService(factory);
        var hasher = new PasswordHasherService();
        var options = Options.Create(new AuthOptions
        {
            JwtIssuer = "issuer",
            JwtAudience = "aud",
            JwtSecret = "UnitTestJwtSecretValueAtLeast32Chars!",
            TokenExpiryHours = 24,
            LoginMaxFailures = 5,
            LoginCooldownSeconds = 60,
            RefreshTokenExpiryDays = 30
        });
        var token = new TokenService(options, TimeProvider.System);
        var limiter = new LoginRateLimiter(new MemoryCache(new MemoryCacheOptions()), options, TimeProvider.System);
        var refreshTokens = new RefreshTokenService(factory, token, options, TimeProvider.System);
        return new AuthService(factory, userService, hasher, token, refreshTokens, limiter);
    }

    private static TestDbContextFactory CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-pwdpolicy-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }
}
