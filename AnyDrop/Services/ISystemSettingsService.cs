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

    /// <summary>获取每日缩略图生成的小时（0–23，按后台任务时区 D6）。</summary>
    Task<int> GetThumbnailGenerationHourAsync(CancellationToken ct = default);

    /// <summary>是否启用计划任务批量生成预览图。</summary>
    Task<bool> IsScheduledThumbnailGenerationEnabledAsync(CancellationToken ct = default);
}
