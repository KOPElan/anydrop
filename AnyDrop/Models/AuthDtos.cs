using AnyDrop.Shared;
using Microsoft.AspNetCore.Http;

namespace AnyDrop.Models;

// 以下 DTO 已迁移到 AnyDrop.Shared，此处提供全局别名以保持向后兼容：
// SetupStatusDto, SetupRequest, LoginRequest, LoginResponse, UserProfileDto,
// LogoutResultDto, UpdateNicknameRequest, UpdatePasswordRequest,
// SecuritySettingsDto, UpdateSecuritySettingsRequest

/// <summary>内部认证服务结果，仅服务端 Service 层使用，不通过 HTTP 传输。</summary>
/// <param name="ErrorCode">失败时的稳定错误码（见 <see cref="ApiErrorCodes"/>）；端点层透传进信封。</param>
public sealed record AuthResult<T>(
    bool Succeeded,
    int StatusCode,
    T? Data,
    string? Error,
    string? ErrorCode = null,
    TimeSpan? RetryAfter = null)
{
    public static AuthResult<T> Success(T data, int statusCode = StatusCodes.Status200OK) => new(true, statusCode, data, null);

    /// <summary><paramref name="retryAfter"/> 非空时端点会带上 Retry-After 响应头（用于限流）。</summary>
    public static AuthResult<T> Failure(string error, int statusCode, string? errorCode = null, TimeSpan? retryAfter = null)
        => new(false, statusCode, default, error, errorCode, retryAfter);
}

