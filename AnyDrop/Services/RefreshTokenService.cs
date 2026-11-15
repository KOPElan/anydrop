using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AnyDrop.Services;

/// <summary>Refresh Token 轮换结果。</summary>
public sealed record RefreshRotationResult(
    bool Succeeded,
    string? Error,
    LoginResponse? Payload = null,
    bool ReplayDetected = false);

public interface IRefreshTokenService
{
    /// <summary>为用户签发一个新的 Refresh Token（新族）。返回原始 token（只出现一次）与过期时间。</summary>
    Task<(string RawToken, DateTimeOffset ExpiresAt)> IssueAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// 用旧 Refresh Token 换新 Access Token + 新 Refresh Token（旋转）。
    /// 已撤销的 token 再次出现即视为重放：整族吊销，返回 <see cref="RefreshRotationResult.ReplayDetected"/>。
    /// </summary>
    Task<RefreshRotationResult> RotateAsync(string rawToken, CancellationToken ct = default);

    /// <summary>吊销整个 token 族（该族的全部 token 立即失效）。</summary>
    Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default);

    /// <summary>吊销该用户的全部 Refresh Token（登出 / 改密码时调用）。</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Refresh Token 服务（S2-01，CLI-01）。
///
/// 存储只保留 SHA-256 哈希；旋转时旧 token 标记撤销并记录替换者。
/// 重放检测：已撤销且已被替换的 token 再次提交，说明旧 token 已泄露，
/// 此时整族吊销——攻击者手里的新 token 也随之失效。
/// </summary>
public sealed class RefreshTokenService(
    IDbContextFactory<AnyDropDbContext> dbContextFactory,
    ITokenService tokenService,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider) : IRefreshTokenService
{
    private readonly AuthOptions _authOptions = authOptions.Value;

    public async Task<(string RawToken, DateTimeOffset ExpiresAt)> IssueAsync(Guid userId, CancellationToken ct = default)
    {
        var rawToken = tokenService.GenerateRefreshToken();
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddDays(Math.Clamp(_authOptions.RefreshTokenExpiryDays, 1, 365));

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            FamilyId = Guid.NewGuid(),
            TokenHash = RefreshToken.ComputeHash(rawToken),
            ExpiresAt = expiresAt,
            CreatedAt = now
        });
        await db.SaveChangesWithRetryAsync(ct);
        return (rawToken, expiresAt);
    }

    public async Task<RefreshRotationResult> RotateAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return new RefreshRotationResult(false, "刷新令牌无效。");
        }

        var hash = RefreshToken.ComputeHash(rawToken);
        var now = timeProvider.GetUtcNow();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null)
        {
            return new RefreshRotationResult(false, "刷新令牌无效。");
        }

        if (stored.RevokedAt.HasValue)
        {
            // 已撤销的 token 再次出现：若它曾被轮换替换，说明旧 token 已泄露并被重放。
            if (stored.ReplacedByTokenHash is not null)
            {
                await RevokeFamilyInternalAsync(db, stored.FamilyId, now, ct);
                await db.SaveChangesWithRetryAsync(ct);
                return new RefreshRotationResult(false, "刷新令牌无效。", ReplayDetected: true);
            }

            return new RefreshRotationResult(false, "刷新令牌无效。");
        }

        if (stored.ExpiresAt <= now)
        {
            return new RefreshRotationResult(false, "刷新令牌已过期。");
        }

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == stored.UserId, ct);
        if (user is null)
        {
            return new RefreshRotationResult(false, "用户不存在。");
        }

        // 旋转：旧 token 撤销并记录替换者，新 token 继承同一族。
        var newRawToken = tokenService.GenerateRefreshToken();
        var newHash = RefreshToken.ComputeHash(newRawToken);
        var newExpiresAt = now.AddDays(Math.Clamp(_authOptions.RefreshTokenExpiryDays, 1, 365));

        stored.RevokedAt = now;
        stored.ReplacedByTokenHash = newHash;
        stored.LastUsedAt = now;
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = stored.UserId,
            FamilyId = stored.FamilyId,
            TokenHash = newHash,
            ExpiresAt = newExpiresAt,
            CreatedAt = now
        });

        await db.SaveChangesWithRetryAsync(ct);

        // 顺手清掉过期超过 7 天的旧记录，避免表无限增长；失败不影响本次轮换。
        try
        {
            var cutoff = now.AddDays(-7);
            await db.RefreshTokens.Where(x => x.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
        }
        catch
        {
            // 清理失败不影响轮换结果
        }

        var access = tokenService.GenerateToken(user);
        var payload = new LoginResponse(
            new UserProfileDto(user.Nickname, user.LastLoginAt),
            access.AccessToken,
            access.ExpiresAt,
            newRawToken,
            newExpiresAt);
        return new RefreshRotationResult(true, null, payload);
    }

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await RevokeFamilyInternalAsync(db, familyId, timeProvider.GetUtcNow(), ct);
        await db.SaveChangesWithRetryAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow();
        // 逐行更新而不用 ExecuteUpdate：单用户部署下行数极少，且 InMemory 测试库
        // 不支持 ExecuteUpdate/ExecuteDelete。
        var tokens = await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }

        await db.SaveChangesWithRetryAsync(ct);
    }

    private async Task RevokeFamilyInternalAsync(AnyDropDbContext db, Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens
            .Where(x => x.FamilyId == familyId && x.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
    }
}
