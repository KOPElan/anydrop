using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

public sealed class SystemSettingsService(AnyDropDbContext dbContext) : ISystemSettingsService
{
    private static readonly Guid SettingsId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task<SecuritySettingsDto> GetSecuritySettingsAsync(CancellationToken ct = default)
    {
        var settings = await EnsureSettingsAsync(ct);
        return new SecuritySettingsDto(settings.AutoFetchLinkPreview);
    }

    public async Task<AuthResult<SecuritySettingsDto>> UpdateSecuritySettingsAsync(bool autoFetchLinkPreview, CancellationToken ct = default)
    {
        var settings = await EnsureSettingsAsync(ct);
        settings.AutoFetchLinkPreview = autoFetchLinkPreview;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);
        return AuthResult<SecuritySettingsDto>.Success(new SecuritySettingsDto(settings.AutoFetchLinkPreview));
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
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.SystemSettings.Add(settings);
        await dbContext.SaveChangesAsync(ct);
        return settings;
    }
}
