using AnyDrop.Models;

namespace AnyDrop.Services;

public interface ISystemSettingsService
{
    Task<SecuritySettingsDto> GetSecuritySettingsAsync(CancellationToken ct = default);
    Task<AuthResult<SecuritySettingsDto>> UpdateSecuritySettingsAsync(UpdateSecuritySettingsRequest request, CancellationToken ct = default);
    Task<bool> IsAutoFetchLinkPreviewEnabledAsync(CancellationToken ct = default);
    Task<int> GetBurnAfterReadingMinutesAsync(CancellationToken ct = default);
    Task<TimeZoneInfo> GetDisplayTimeZoneAsync(CancellationToken ct = default);
}
