using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

/// <summary>认证服务接口。</summary>
public interface IAuthService
{
    Task<SetupStatusDto> GetSetupStatusAsync();
    Task<LoginResponse> SetupAsync(SetupRequest request);
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task LogoutAsync();
    Task<UserProfileDto> GetCurrentUserAsync();
}
