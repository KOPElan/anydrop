using AnyDrop.Shared;

namespace AnyDrop.App.Models;

// SetupStatusDto, SetupRequest, LoginRequest, LoginResponse, UserProfileDto,
// LogoutResultDto, UpdateNicknameRequest, UpdatePasswordRequest
// 均已迁移到 AnyDrop.Shared，可直接使用。

/// <summary>App 端认证服务操作结果。封装服务端 LoginResponse 的成功/失败状态。</summary>
public sealed record AppAuthResult(
    bool Success,
    string? Error = null,
    string? AccessToken = null,
    DateTimeOffset ExpiresAt = default,
    UserProfileDto? User = null,
    /// <summary>服务端错误码（见 <see cref="ApiErrorCodes"/>）；界面按码显示本地化文案。</summary>
    string? ErrorCode = null);

