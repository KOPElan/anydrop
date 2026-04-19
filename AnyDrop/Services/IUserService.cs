using AnyDrop.Models;

namespace AnyDrop.Services;

public interface IUserService
{
    Task<bool> HasUserAsync(CancellationToken ct = default);
    Task<User?> GetSingleUserAsync(CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<AuthResult<UserProfileDto>> UpdateNicknameAsync(Guid userId, string nickname, CancellationToken ct = default);
}
