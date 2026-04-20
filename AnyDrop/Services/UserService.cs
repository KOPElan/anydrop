using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

public sealed class UserService(AnyDropDbContext dbContext) : IUserService
{
    public Task<bool> HasUserAsync(CancellationToken ct = default) => dbContext.Users.AnyAsync(ct);

    public Task<User?> GetSingleUserAsync(CancellationToken ct = default)
        => dbContext.Users.FirstOrDefaultAsync(ct);

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default)
        => dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);

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

        var user = await GetByIdAsync(userId, ct);
        if (user is null)
        {
            return AuthResult<UserProfileDto>.Failure("用户不存在。", StatusCodes.Status404NotFound);
        }

        user.Nickname = normalized;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return AuthResult<UserProfileDto>.Success(new UserProfileDto(user.Nickname, user.LastLoginAt));
    }
}
