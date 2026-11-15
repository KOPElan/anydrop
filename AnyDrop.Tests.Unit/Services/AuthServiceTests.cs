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
    public async Task LoginAsync_AfterTooManyFailures_Returns429WithRetryAfter()
    {
        // 被限流时必须返回 429 并给出可等待时长，而不是伪装成「密码错误」：
        // 否则前端无法区分「密码不对」与「被限流」，也无法提示何时可重试。
        await using var db = CreateDbContext();
        var sut = CreateSut(db);
        await sut.SetupAsync(new SetupRequest("Admin", "Password1!", "Password1!"), "k");

        // 记录每一次的状态码，断言完整的失败序列
        var statuses = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await sut.LoginAsync(new LoginRequest("wrong", "/"), "k")).StatusCode);
        }

        var blocked = await sut.LoginAsync(new LoginRequest("Password1!", "/"), "k");
        statuses.Add(blocked.StatusCode);

        statuses.Should().Equal([401, 401, 401, 401, 401, 429],
            "达到阈值后应开始返回 429，即使密码正确也不例外");

        blocked.Succeeded.Should().BeFalse();
        blocked.RetryAfter.Should().NotBeNull();
        blocked.RetryAfter!.Value.Should().BeGreaterThan(TimeSpan.Zero);
        blocked.Error.Should().Contain("尝试次数过多");
    }

    [Fact]
    public async Task LoginAsync_WithLegacyStoredHash_UpgradesItOnSuccess()
    {
        // 升级哈希参数后，既有实例里存的是旧格式哈希。首次登录必须成功，
        // 并且要在用户无感的情况下把哈希换成新格式，否则用户会被永久留在旧参数上。
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        const string password = "Password1!";
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var legacyHash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);

        var user = new User
        {
            Nickname = "Admin",
            PasswordHash = Convert.ToBase64String(legacyHash),
            PasswordSalt = Convert.ToBase64String(salt),
            SessionVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await sut.LoginAsync(new LoginRequest(password, "/"), "k");

        result.Succeeded.Should().BeTrue("旧格式哈希必须仍可登录");

        var stored = await db.Users.AsNoTracking().SingleAsync();
        stored.PasswordHash.Should().StartWith("pbkdf2-sha256$", "登录成功后应自动升级哈希格式");
    }

    [Fact]
    public async Task SetupAsync_StoresSelfDescribingHash()
    {
        await using var db = CreateDbContext();
        var sut = CreateSut(db);

        await sut.SetupAsync(new SetupRequest("Admin", "Password1!", "Password1!"), "k");

        var stored = await db.Users.AsNoTracking().SingleAsync();
        // 测试用 1000 次迭代（见 CreateSut），哈希串必须记录该参数
        stored.PasswordHash.Should().StartWith("pbkdf2-sha256$1000$");
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
        // 测试里用很低的迭代次数：哈希参数的正确性由 PasswordHasherServiceTests 覆盖，
        // 这里只关心认证流程，没必要为每次哈希付出 600,000 次 PBKDF2 的代价。
        var hasher = new PasswordHasherService(Options.Create(new AuthOptions
        {
            PasswordHashIterations = 1_000
        }));
        var token = new TokenService(Options.Create(new AuthOptions
        {
            JwtIssuer = "issuer",
            JwtAudience = "aud",
            JwtSecret = "UnitTestJwtSecretValueAtLeast32Chars!",
            TokenExpiryHours = 24,
            LoginMaxFailures = 5,
            LoginCooldownSeconds = 60
        }), TimeProvider.System);
        var limiter = new LoginRateLimiter(new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AuthOptions { LoginMaxFailures = 5, LoginCooldownSeconds = 60 }),
            TimeProvider.System);
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
