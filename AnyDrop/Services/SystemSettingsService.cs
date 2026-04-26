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
        // 验证时区 ID 合法性
        TimeZoneInfo? tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return AuthResult<SecuritySettingsDto>.Failure("无效的时区 ID。", StatusCodes.Status400BadRequest);
        }

        // 验证阅后即焚时长范围（1–43200 分钟，即 1 分钟到 30 天）
        if (request.BurnAfterReadingMinutes is < 1 or > 43200)
        {
            return AuthResult<SecuritySettingsDto>.Failure("阅后即焚时长必须在 1–43200 分钟之间。", StatusCodes.Status400BadRequest);
        }

        // 验证语言代码
        var validLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zh-CN", "zh-TW", "en" };
        if (!validLanguages.Contains(request.Language))
        {
            return AuthResult<SecuritySettingsDto>.Failure("不支持的语言代码。", StatusCodes.Status400BadRequest);
        }

        var settings = await EnsureSettingsAsync(ct);
        settings.AutoFetchLinkPreview = request.AutoFetchLinkPreview;
        settings.TimeZoneId = tz.Id;
        settings.BurnAfterReadingMinutes = request.BurnAfterReadingMinutes;
        settings.Language = request.Language;
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

    public async Task<TimeZoneInfo> GetDisplayTimeZoneAsync(CancellationToken ct = default)
    {
        var projection = await dbContext.SystemSettings
            .AsNoTracking()
            .Select(x => new { x.TimeZoneId })
            .FirstOrDefaultAsync(ct);

        var tzId = projection?.TimeZoneId ?? "UTC";
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static SecuritySettingsDto MapToDto(SystemSettings s)
        => new(s.AutoFetchLinkPreview, s.TimeZoneId, s.BurnAfterReadingMinutes, s.Language);

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
