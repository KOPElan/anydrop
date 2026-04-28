using AnyDrop.Models;

namespace AnyDrop.Services;

public interface ISystemSettingsService
{
    Task<SecuritySettingsDto> GetSecuritySettingsAsync(CancellationToken ct = default);
    Task<AuthResult<SecuritySettingsDto>> UpdateSecuritySettingsAsync(UpdateSecuritySettingsRequest request, CancellationToken ct = default);
    Task<bool> IsAutoFetchLinkPreviewEnabledAsync(CancellationToken ct = default);
    Task<int> GetBurnAfterReadingMinutesAsync(CancellationToken ct = default);

    /// <summary>获取自动清理是否启用及清理月数。</summary>
    Task<(bool Enabled, int Months)> GetAutoCleanupSettingsAsync(CancellationToken ct = default);
}
