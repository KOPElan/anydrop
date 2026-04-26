using Microsoft.AspNetCore.Http;

namespace AnyDrop.Models;

public sealed record SetupRequest(string Nickname, string Password, string ConfirmPassword);

public sealed record LoginRequest(string Password, string? ReturnUrl);

public sealed record LoginResponse(UserProfileDto User, string AccessToken, DateTimeOffset ExpiresAt);

public sealed record UserProfileDto(string Nickname, DateTimeOffset? LastLoginAt);

public sealed record UpdateNicknameRequest(string Nickname);

public sealed record UpdatePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);

public sealed record SecuritySettingsDto(
    bool AutoFetchLinkPreview,
    string TimeZoneId,
    int BurnAfterReadingMinutes,
    string Language);

public sealed record UpdateSecuritySettingsRequest(
    bool AutoFetchLinkPreview,
    string TimeZoneId,
    int BurnAfterReadingMinutes,
    string Language);

public sealed record SetupStatusDto(bool RequiresSetup);

public sealed record LogoutResultDto(bool LoggedOut);

public sealed record AuthResult<T>(bool Succeeded, int StatusCode, T? Data, string? Error)
{
    public static AuthResult<T> Success(T data, int statusCode = StatusCodes.Status200OK) => new(true, statusCode, data, null);
    public static AuthResult<T> Failure(string error, int statusCode) => new(false, statusCode, default, error);
}
