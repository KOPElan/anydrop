using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// S1-03（DAT-09）：用 <see cref="IDbContextFactory{TContext}"/>、
/// 每次调用建短寿命上下文。同一方法内「读后改」共用一个上下文（跟踪有效）；
/// 跨方法返回的实体是脱离跟踪的，调用方不得直接在另一个上下文里保存它。
/// </summary>
public sealed class UserService(IDbContextFactory<AnyDropDbContext> dbContextFactory) : IUserService
{
    public async Task<bool> HasUserAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        return await dbContext.Users.AnyAsync(ct);
    }

    public async Task<User?> GetSingleUserAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        return await dbContext.Users.FirstOrDefaultAsync(ct);
    }

    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        return await FindUserAsync(dbContext, userId, ct);
    }

    public async Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await GetByIdAsync(userId, ct);
        return user is null ? null : new UserProfileDto(user.Nickname, user.LastLoginAt);
    }

    public async Task<AuthResult<UserProfileDto>> UpdateNicknameAsync(Guid userId, string nickname, CancellationToken ct = default)
    {
        var normalized = nickname.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 50)
        {
            return AuthResult<UserProfileDto>.Failure("昵称不能为空且不能超过 50 个字符。", StatusCodes.Status400BadRequest);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var user = await FindUserAsync(dbContext, userId, ct);
        if (user is null)
        {
            return AuthResult<UserProfileDto>.Failure("用户不存在。", StatusCodes.Status404NotFound);
        }

        user.Nickname = normalized;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesWithRetryAsync(ct);
        return AuthResult<UserProfileDto>.Success(new UserProfileDto(user.Nickname, user.LastLoginAt));
    }

    private static Task<User?> FindUserAsync(AnyDropDbContext dbContext, Guid userId, CancellationToken ct)
        => dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
}
