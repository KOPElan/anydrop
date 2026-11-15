using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// S1-03（DAT-09）：用 <see cref="IDbContextFactory{TContext}"/>、
/// 每次调用建短寿命上下文。注意「读后改」必须在同一个上下文里完成：
/// <see cref="IUserService"/> 返回的实体属于另一个方法的上下文（已释放），
/// 在本服务的上下文里直接保存它不会生效，因此改密码 / 登出 / 登录更新
/// 一律用本方法的上下文重新加载用户再改。
/// </summary>
public sealed class AuthService(
    IDbContextFactory<AnyDropDbContext> dbContextFactory,
    IUserService userService,
    IPasswordHasherService passwordHasherService,
    ITokenService tokenService,
    ILoginRateLimiter loginRateLimiter) : IAuthService
{
    public async Task<AuthResult<LoginResponse>> SetupAsync(SetupRequest request, string rateLimitKey, CancellationToken ct = default)
    {
        if (await userService.HasUserAsync(ct))
        {
            return AuthResult<LoginResponse>.Failure("初始化已完成。", StatusCodes.Status409Conflict);
        }

        // S0-03：初始化接口同样按来源限流。初始化只能成功一次，但不限流时
        // 攻击者可用同一出口 IP 反复提交初始化请求刷数据库写操作。
        // 注意：429 与 409 的顺序——已初始化时直接返回 409，不消耗限流额度。
        if (loginRateLimiter.IsLocked(rateLimitKey, out _))
        {
            return AuthResult<LoginResponse>.Failure("操作过于频繁，请稍后再试。", StatusCodes.Status429TooManyRequests);
        }

        var nickname = request.Nickname.Trim();
        if (string.IsNullOrWhiteSpace(nickname) || nickname.Length > 50)
        {
            loginRateLimiter.RegisterFailure(rateLimitKey);
            return AuthResult<LoginResponse>.Failure("昵称不能为空且不能超过 50 个字符。", StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            loginRateLimiter.RegisterFailure(rateLimitKey);
            return AuthResult<LoginResponse>.Failure("密码长度至少 6 位。", StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            loginRateLimiter.RegisterFailure(rateLimitKey);
            return AuthResult<LoginResponse>.Failure("两次输入的密码不一致。", StatusCodes.Status400BadRequest);
        }

        var (passwordHash, passwordSalt) = passwordHasherService.HashPassword(request.Password);
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Nickname = nickname,
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
            SessionVersion = 1,
            CreatedAt = now,
            LastLoginAt = now,
            UpdatedAt = now
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesWithRetryAsync(ct);
        }
        catch (DbUpdateException)
        {
            return AuthResult<LoginResponse>.Failure("初始化已完成。", StatusCodes.Status409Conflict);
        }

        loginRateLimiter.Reset(rateLimitKey);
        var token = tokenService.GenerateToken(user);
        var payload = new LoginResponse(new UserProfileDto(user.Nickname, user.LastLoginAt), token.AccessToken, token.ExpiresAt);
        return AuthResult<LoginResponse>.Success(payload, StatusCodes.Status201Created);
    }

    public async Task<AuthResult<LoginResponse>> LoginAsync(LoginRequest request, string rateLimitKey, CancellationToken ct = default)
    {
        if (loginRateLimiter.IsLocked(rateLimitKey, out var retryAfter))
        {
            // 明确返回 429 与需要等待的时长，而不是伪装成「账号或密码错误」：
            // 前端可以据此给出可操作的提示，也便于用户区分「密码不对」和「被限流」。
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            return AuthResult<LoginResponse>.Failure(
                $"尝试次数过多，请 {seconds} 秒后再试。",
                StatusCodes.Status429TooManyRequests,
                retryAfter);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var user = await dbContext.Users.FirstOrDefaultAsync(ct);
        if (user is null)
        {
            return AuthResult<LoginResponse>.Failure("请先完成初始化配置。", StatusCodes.Status409Conflict);
        }

        var verification = passwordHasherService.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt);
        if (verification == PasswordVerificationResult.Failed)
        {
            loginRateLimiter.RegisterFailure(rateLimitKey);
            return AuthResult<LoginResponse>.Failure("账号或密码错误。", StatusCodes.Status401Unauthorized);
        }

        loginRateLimiter.Reset(rateLimitKey);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        // 存储的哈希参数已过时（旧格式，或迭代次数低于当前要求）：
        // 借这次已知明文的机会就地升级，用户无感。
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var (upgradedHash, upgradedSalt) = passwordHasherService.HashPassword(request.Password);
            user.PasswordHash = upgradedHash;
            user.PasswordSalt = upgradedSalt;
        }

        await dbContext.SaveChangesWithRetryAsync(ct);

        var token = tokenService.GenerateToken(user);
        var payload = new LoginResponse(new UserProfileDto(user.Nickname, user.LastLoginAt), token.AccessToken, token.ExpiresAt);
        return AuthResult<LoginResponse>.Success(payload);
    }

    public async Task<AuthResult<LogoutResultDto>> LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null)
        {
            return AuthResult<LogoutResultDto>.Failure("用户不存在。", StatusCodes.Status404NotFound);
        }

        user.SessionVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesWithRetryAsync(ct);
        return AuthResult<LogoutResultDto>.Success(new LogoutResultDto(true));
    }

    public async Task<AuthResult<UserProfileDto>> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await userService.GetProfileAsync(userId, ct);
        return profile is null
            ? AuthResult<UserProfileDto>.Failure("用户不存在。", StatusCodes.Status404NotFound)
            : AuthResult<UserProfileDto>.Success(profile);
    }

    public Task<AuthResult<UserProfileDto>> UpdateNicknameAsync(Guid userId, UpdateNicknameRequest request, CancellationToken ct = default)
        => userService.UpdateNicknameAsync(userId, request.Nickname, ct);

    public async Task<AuthResult<bool>> UpdatePasswordAsync(Guid userId, UpdatePasswordRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return AuthResult<bool>.Failure("新密码长度至少 6 位。", StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return AuthResult<bool>.Failure("两次输入的新密码不一致。", StatusCodes.Status400BadRequest);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null)
        {
            return AuthResult<bool>.Failure("用户不存在。", StatusCodes.Status404NotFound);
        }

        if (passwordHasherService.VerifyPassword(request.CurrentPassword, user.PasswordHash, user.PasswordSalt)
            == PasswordVerificationResult.Failed)
        {
            return AuthResult<bool>.Failure("当前密码错误。", StatusCodes.Status401Unauthorized);
        }

        var (hash, salt) = passwordHasherService.HashPassword(request.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.SessionVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesWithRetryAsync(ct);
        return AuthResult<bool>.Success(true);
    }

    public async Task<bool> ValidateSessionVersionAsync(Guid userId, int sessionVersion, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var current = await dbContext.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => (int?)x.SessionVersion)
            .FirstOrDefaultAsync(ct);

        return current.HasValue && current.Value == sessionVersion;
    }
}
