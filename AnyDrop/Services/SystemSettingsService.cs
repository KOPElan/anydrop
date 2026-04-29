using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

public sealed class SystemSettingsService(AnyDropDbContext dbContext) : ISystemSettingsService
{
    private static readonly Guid SettingsId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task<SecuritySettingsDto> GetSecuritySettingsAsync(CancellationToken ct = default)
    {
        var settings = await EnsureSettingsAsync(ct);
        return MapToDto(settings);
    }

    public async Task<AuthResult<SecuritySettingsDto>> UpdateSecuritySettingsAsync(UpdateSecuritySettingsRequest request, CancellationToken ct = default)
    {
        // 验证阅后即焚时长范围（1–43200 分钟，即 1 分钟到 30 天）
        if (request.BurnAfterReadingMinutes is < 1 or > 43200)
        {
            return AuthResult<SecuritySettingsDto>.Failure("阅后即焚时长必须在 1–43200 分钟之间。", StatusCodes.Status400BadRequest);
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

        var settings = await EnsureSettingsAsync(ct);
        settings.AutoFetchLinkPreview = request.AutoFetchLinkPreview;
        settings.BurnAfterReadingMinutes = request.BurnAfterReadingMinutes;
        settings.Language = request.Language;
        settings.AutoCleanupEnabled = request.AutoCleanupEnabled;
        settings.AutoCleanupMonths = request.AutoCleanupMonths;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return AuthResult<SecuritySettingsDto>.Success(MapToDto(settings));
    }

    public async Task<bool> IsAutoFetchLinkPreviewEnabledAsync(CancellationToken ct = default)
    {
        var settingsProjection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.AutoFetchLinkPreview })
            .FirstOrDefaultAsync(ct);
        if (settingsProjection is not null)
        {
            return settingsProjection.AutoFetchLinkPreview;
        }

        var settings = await EnsureSettingsAsync(ct);
        return settings.AutoFetchLinkPreview;
    }

    public async Task<int> GetBurnAfterReadingMinutesAsync(CancellationToken ct = default)
    {
        var projection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.BurnAfterReadingMinutes })
            .FirstOrDefaultAsync(ct);
        if (projection is not null)
        {
            return projection.BurnAfterReadingMinutes;
        }

        var settings = await EnsureSettingsAsync(ct);
        return settings.BurnAfterReadingMinutes;
    }

    public async Task<(bool Enabled, int Months)> GetAutoCleanupSettingsAsync(CancellationToken ct = default)
    {
        var projection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.AutoCleanupEnabled, x.AutoCleanupMonths })
            .FirstOrDefaultAsync(ct);
        if (projection is not null)
        {
            return (projection.AutoCleanupEnabled, projection.AutoCleanupMonths);
        }

        var settings = await EnsureSettingsAsync(ct);
        return (settings.AutoCleanupEnabled, settings.AutoCleanupMonths);
    }

    private static SecuritySettingsDto MapToDto(SystemSettings s)
        => new(s.AutoFetchLinkPreview, s.BurnAfterReadingMinutes, s.Language, s.AutoCleanupEnabled, s.AutoCleanupMonths);

    private async Task<SystemSettings> EnsureSettingsAsync(CancellationToken ct)
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
        await dbContext.SaveChangesAsync(ct);
        return settings;
    }
}
