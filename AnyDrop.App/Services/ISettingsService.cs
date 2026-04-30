using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

/// <summary>用户设置管理接口。</summary>
public interface ISettingsService
{
    Task<SecuritySettingsDto> GetSecuritySettingsAsync();
    Task UpdateSecuritySettingsAsync(UpdateSecuritySettingsRequest request);
    Task UpdateNicknameAsync(UpdateNicknameRequest request);
    Task UpdatePasswordAsync(UpdatePasswordRequest request);
    Task<int> CleanupOldMessagesAsync(int months);
}
