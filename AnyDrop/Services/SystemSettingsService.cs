using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// S1-03（DAT-09）：用 <see cref="IDbContextFactory{TContext}"/>、
/// 每次调用建短寿命上下文；单例行的「先查后插」遇主键冲突时重读返回，
/// 避免冷启动并发下偶发 500。
/// </summary>
public sealed class SystemSettingsService(IDbContextFactory<AnyDropDbContext> dbContextFactory) : ISystemSettingsService
{
    private static readonly Guid SettingsId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task<SecuritySettingsDto> GetSecuritySettingsAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(dbContext, ct);
        return MapToDto(settings);
    }

    public async Task<AuthResult<SecuritySettingsDto>> UpdateSecuritySettingsAsync(UpdateSecuritySettingsRequest request, CancellationToken ct = default)
    {
        // 验证定时销毁时长范围（1–43200 分钟，即 1 分钟到 30 天）
        if (request.BurnAfterReadingMinutes is < 1 or > 43200)
        {
            return AuthResult<SecuritySettingsDto>.Failure("定时销毁时长必须在 1–43200 分钟之间。", StatusCodes.Status400BadRequest);
        }

        // 验证语言代码
        if (!SupportedLanguages.All.Contains(request.Language))
        {
            return AuthResult<SecuritySettingsDto>.Failure("不支持的语言代码。", StatusCodes.Status400BadRequest);
        }

        // 仅在启用自动清理时才校验月数（未启用时月数无实际影响，避免破坏旧客户端请求）
        if (request.AutoCleanupEnabled && request.AutoCleanupMonths is not (1 or 3 or 6))
        {
            return AuthResult<SecuritySettingsDto>.Failure("自动清理月数必须为 1、3 或 6。", StatusCodes.Status400BadRequest);
        }

        // 验证缩略图生成小时范围（0–23）
        if (request.ThumbnailGenerationHour is < 0 or > 23)
        {
            return AuthResult<SecuritySettingsDto>.Failure("缩略图生成小时必须在 0–23 之间。", StatusCodes.Status400BadRequest);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(dbContext, ct);
        settings.AutoFetchLinkPreview = request.AutoFetchLinkPreview;
        settings.BurnAfterReadingMinutes = request.BurnAfterReadingMinutes;
        settings.Language = request.Language;
        settings.AutoCleanupEnabled = request.AutoCleanupEnabled;
        settings.AutoCleanupMonths = request.AutoCleanupMonths;
        settings.ThumbnailGenerationHour = request.ThumbnailGenerationHour;
        settings.ScheduledThumbnailGenerationEnabled = request.ScheduledThumbnailGenerationEnabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesWithRetryAsync(ct);
        return AuthResult<SecuritySettingsDto>.Success(MapToDto(settings));
    }

    public async Task<bool> IsAutoFetchLinkPreviewEnabledAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var settingsProjection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.AutoFetchLinkPreview })
            .FirstOrDefaultAsync(ct);
        if (settingsProjection is not null)
        {
            return settingsProjection.AutoFetchLinkPreview;
        }

        var settings = await EnsureSettingsAsync(dbContext, ct);
        return settings.AutoFetchLinkPreview;
    }

    public async Task<int> GetBurnAfterReadingMinutesAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var projection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.BurnAfterReadingMinutes })
            .FirstOrDefaultAsync(ct);
        if (projection is not null)
        {
            return projection.BurnAfterReadingMinutes;
        }

        var settings = await EnsureSettingsAsync(dbContext, ct);
        return settings.BurnAfterReadingMinutes;
    }

    public async Task<(bool Enabled, int Months)> GetAutoCleanupSettingsAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var projection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.AutoCleanupEnabled, x.AutoCleanupMonths })
            .FirstOrDefaultAsync(ct);
        if (projection is not null)
        {
            return (projection.AutoCleanupEnabled, projection.AutoCleanupMonths);
        }

        var settings = await EnsureSettingsAsync(dbContext, ct);
        return (settings.AutoCleanupEnabled, settings.AutoCleanupMonths);
    }

    public async Task<int> GetThumbnailGenerationHourAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var projection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.ThumbnailGenerationHour })
            .FirstOrDefaultAsync(ct);
        if (projection is not null)
        {
            return projection.ThumbnailGenerationHour;
        }

        var settings = await EnsureSettingsAsync(dbContext, ct);
        return settings.ThumbnailGenerationHour;
    }

    public async Task<bool> IsScheduledThumbnailGenerationEnabledAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var projection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.ScheduledThumbnailGenerationEnabled })
            .FirstOrDefaultAsync(ct);
        if (projection is not null)
        {
            return projection.ScheduledThumbnailGenerationEnabled;
        }

        var settings = await EnsureSettingsAsync(dbContext, ct);
        return settings.ScheduledThumbnailGenerationEnabled;
    }

    private static SecuritySettingsDto MapToDto(SystemSettings s)
        => new(
            s.AutoFetchLinkPreview,
            s.BurnAfterReadingMinutes,
            s.Language,
            s.AutoCleanupEnabled,
            s.AutoCleanupMonths,
            s.ThumbnailGenerationHour,
            s.ScheduledThumbnailGenerationEnabled);

    /// <summary>
    /// 取不到单例行时插入一行。冷启动并发下两个调用可能同时走到插入：
    /// 后到的会撞主键冲突——此时不抛 500，改用全新上下文重读一次，
    /// 读到先到的那行就直接返回；重读不到才原样抛出。
    /// </summary>
    private async Task<SystemSettings> EnsureSettingsAsync(AnyDropDbContext dbContext, CancellationToken ct)
    {
        var settings = await dbContext.SystemSettings.FirstOrDefaultAsync(ct);
        if (settings is not null)
        {
            return settings;
        }

        settings = new SystemSettings
        {
            Id = SettingsId,
            AutoFetchLinkPreview = true,
            TimeZoneId = "UTC",
            BurnAfterReadingMinutes = 10,
            Language = "zh-CN",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.SystemSettings.Add(settings);
        try
        {
            await dbContext.SaveChangesWithRetryAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 并发插入冲突：用全新上下文重读，先到的那一行一定已提交。
            await using var freshDb = await dbContextFactory.CreateDbContextAsync(ct);
            var existing = await freshDb.SystemSettings.FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }

        return settings;
    }
}
