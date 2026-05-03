using AnyDrop.App.Models;
using AnyDrop.Shared;

namespace AnyDrop.App.Services;

/// <summary>认证服务接口。</summary>
public interface IAuthService
{
    Task<SetupStatusDto> GetSetupStatusAsync();
    Task<AppAuthResult> SetupAsync(SetupRequest request);
    Task<AppAuthResult> LoginAsync(LoginRequest request);
    Task LogoutAsync();
    Task<UserProfileDto> GetCurrentUserAsync();
}

