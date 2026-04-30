namespace AnyDrop.App.Models;

public sealed record SecuritySettingsDto(
    bool AutoFetchLinkPreview,
    int BurnAfterReadMinutes,
    bool AutoCleanup,
    int AutoCleanupMonths);

public sealed record UpdateSecuritySettingsRequest(
    bool AutoFetchLinkPreview,
    int BurnAfterReadMinutes,
    bool AutoCleanup,
    int AutoCleanupMonths);

public sealed record UpdateNicknameRequest(string Nickname);

public sealed record UpdatePasswordRequest(string CurrentPassword, string NewPassword);
