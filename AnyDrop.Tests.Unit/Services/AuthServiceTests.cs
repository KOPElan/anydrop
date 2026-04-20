using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AnyDrop.Tests.Unit.Services;

public class AuthServiceTests
{
    [Fact]
    public async Task SetupAsync_WhenNoUser_CreatesSingleUserAndReturnsToken()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        var result = await sut.SetupAsync(new SetupRequest("Admin", "Password1!", "Password1!"), "k");

        result.Succeeded.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ShouldReturnUnauthorized()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        await sut.SetupAsync(new SetupRequest("Admin", "Password1!", "Password1!"), "k");

        var result = await sut.LoginAsync(new LoginRequest("wrong", "/"), "k");

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task UpdatePasswordAsync_ShouldIncreaseSessionVersion()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        await sut.SetupAsync(new SetupRequest("Admin", "Password1!", "Password1!"), "k");
        var user = await db.Users.SingleAsync();
        var oldVersion = user.SessionVersion;

        var result = await sut.UpdatePasswordAsync(user.Id, new UpdatePasswordRequest("Password1!", "Password2!", "Password2!"));

        result.Succeeded.Should().BeTrue();
        (await db.Users.SingleAsync()).SessionVersion.Should().Be(oldVersion + 1);
    }

    private static AuthService CreateSut(AnyDropDbContext db)
    {
        var userService = new UserService(db);
        var hasher = new PasswordHasherService();
        var token = new TokenService(Options.Create(new AuthOptions
        {
            JwtIssuer = "issuer",
            JwtAudience = "aud",
            JwtSecret = "UnitTestJwtSecretValueAtLeast32Chars!",
            TokenExpiryHours = 24,
            LoginMaxFailures = 5,
            LoginCooldownSeconds = 60
        }));
        var limiter = new LoginRateLimiter(new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AuthOptions { LoginMaxFailures = 5, LoginCooldownSeconds = 60 }));
        return new AuthService(db, userService, hasher, token, limiter);
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-auth-{Guid.NewGuid():N}")
            .Options;
        return new AnyDropDbContext(options);
    }
}
