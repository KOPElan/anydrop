using AnyDrop.Shared;
using Microsoft.AspNetCore.Http;

namespace AnyDrop.Models;

// 以下 DTO 已迁移到 AnyDrop.Shared，此处提供全局别名以保持向后兼容：
// SetupStatusDto, SetupRequest, LoginRequest, LoginResponse, UserProfileDto,
// LogoutResultDto, UpdateNicknameRequest, UpdatePasswordRequest,
// SecuritySettingsDto, UpdateSecuritySettingsRequest

/// <summary>内部认证服务结果，仅服务端 Service 层使用，不通过 HTTP 传输。</summary>
public sealed record AuthResult<T>(bool Succeeded, int StatusCode, T? Data, string? Error)
{
    public static AuthResult<T> Success(T data, int statusCode = StatusCodes.Status200OK) => new(true, statusCode, data, null);
    public static AuthResult<T> Failure(string error, int statusCode) => new(false, statusCode, default, error);
}

