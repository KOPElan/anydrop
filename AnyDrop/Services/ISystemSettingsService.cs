using AnyDrop.Models;

namespace AnyDrop.Services;

public interface ISystemSettingsService
{
    Task<SecuritySettingsDto> GetSecuritySettingsAsync(CancellationToken ct = default);
    Task<AuthResult<SecuritySettingsDto>> UpdateSecuritySettingsAsync(bool autoFetchLinkPreview, CancellationToken ct = default);
    Task<bool> IsAutoFetchLinkPreviewEnabledAsync(CancellationToken ct = default);
}
