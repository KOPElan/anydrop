using AnyDrop.Models;

namespace AnyDrop.Services;

public interface IAuthService
{
    Task<AuthResult<LoginResponse>> SetupAsync(SetupRequest request, string rateLimitKey, CancellationToken ct = default);
    Task<AuthResult<LoginResponse>> LoginAsync(LoginRequest request, string rateLimitKey, CancellationToken ct = default);
    Task<AuthResult<LogoutResultDto>> LogoutAsync(Guid userId, CancellationToken ct = default);
    Task<AuthResult<UserProfileDto>> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<AuthResult<UserProfileDto>> UpdateNicknameAsync(Guid userId, UpdateNicknameRequest request, CancellationToken ct = default);
    Task<AuthResult<bool>> UpdatePasswordAsync(Guid userId, UpdatePasswordRequest request, CancellationToken ct = default);
    Task<bool> ValidateSessionVersionAsync(Guid userId, int sessionVersion, CancellationToken ct = default);
}
