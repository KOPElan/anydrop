using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

public sealed class AuthService(
    AnyDropDbContext dbContext,
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

        var nickname = request.Nickname.Trim();
        if (string.IsNullOrWhiteSpace(nickname) || nickname.Length > 50)
        {
            return AuthResult<LoginResponse>.Failure("昵称不能为空且不能超过 50 个字符。", StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            return AuthResult<LoginResponse>.Failure("密码长度至少 6 位。", StatusCodes.Status400BadRequest);
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
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

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(ct);
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
        if (loginRateLimiter.IsLocked(rateLimitKey, out _))
        {
            return AuthResult<LoginResponse>.Failure("账号或密码错误。", StatusCodes.Status401Unauthorized);
        }

        var user = await userService.GetSingleUserAsync(ct);
        if (user is null)
        {
            return AuthResult<LoginResponse>.Failure("请先完成初始化配置。", StatusCodes.Status409Conflict);
        }

        var ok = passwordHasherService.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt);
        if (!ok)
        {
            loginRateLimiter.RegisterFailure(rateLimitKey);
            return AuthResult<LoginResponse>.Failure("账号或密码错误。", StatusCodes.Status401Unauthorized);
        }

        loginRateLimiter.Reset(rateLimitKey);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        var token = tokenService.GenerateToken(user);
        var payload = new LoginResponse(new UserProfileDto(user.Nickname, user.LastLoginAt), token.AccessToken, token.ExpiresAt);
        return AuthResult<LoginResponse>.Success(payload);
    }

    public async Task<AuthResult<LogoutResultDto>> LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userService.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return AuthResult<LogoutResultDto>.Failure("用户不存在。", StatusCodes.Status404NotFound);
        }

        user.SessionVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
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

        var user = await userService.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return AuthResult<bool>.Failure("用户不存在。", StatusCodes.Status404NotFound);
        }

        if (!passwordHasherService.VerifyPassword(request.CurrentPassword, user.PasswordHash, user.PasswordSalt))
        {
            return AuthResult<bool>.Failure("当前密码错误。", StatusCodes.Status401Unauthorized);
        }

        var (hash, salt) = passwordHasherService.HashPassword(request.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.SessionVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return AuthResult<bool>.Success(true);
    }

    public async Task<bool> ValidateSessionVersionAsync(Guid userId, int sessionVersion, CancellationToken ct = default)
    {
        var current = await dbContext.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => (int?)x.SessionVersion)
            .FirstOrDefaultAsync(ct);

        return current.HasValue && current.Value == sessionVersion;
    }
}
