namespace AnyDrop.Shared;

/// <summary>
/// API 错误码（S3-06）。
///
/// 服务端不再只返回中文错误原文：每个可预期的失败都有一个稳定的
/// snake_case 错误码，随 <see cref="ApiEnvelope{T}.ErrorCode"/> 下发；
/// 客户端按码查本地化文案（resx 键 <c>Error_&lt;code&gt;</c>），
/// 英文界面不再直接显示中文原文。
///
/// 约定：
/// - 码一经发布不再改名、不再复用（旧客户端靠它做分支）；
/// - <c>Error</c> 文本仍保留：中文原文，供旧客户端与日志排障；
/// - 客户端查不到码对应的本地化文案时，回退显示 <c>Error</c> 原文；
/// - 未预期的异常（catch 里 <c>ex.Message</c> 直传）按操作给一个
///   operation 级别的码，原文只作回退，不作界面主文案。
/// </summary>
public static class ApiErrorCodes
{
    /// <summary>resx 本地化键：Error_ + 码；码为空时返回 null（调用方直接回退）。</summary>
    public static string? ResourceKey(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : $"Error_{code}";

    // ── 认证 / 授权 ──────────────────────────────────────────────
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string InvalidCredentials = "invalid_credentials";
    public const string WrongPassword = "wrong_password";
    public const string UserNotFound = "user_not_found";
    public const string SetupRequired = "setup_required";
    public const string SetupAlreadyDone = "setup_already_done";
    public const string TooManyRequests = "too_many_requests";
    public const string InvalidNickname = "invalid_nickname";
    public const string PasswordMismatch = "password_mismatch";
    public const string PasswordTooShort = "password_too_short";
    public const string PasswordSameAsOld = "password_same_as_old";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string LogoutFailed = "logout_failed";
    public const string AuthFailed = "auth_failed";
    public const string GetUserFailed = "get_user_failed";
    public const string UpdateNicknameFailed = "update_nickname_failed";
    public const string UpdatePasswordFailed = "update_password_failed";
    public const string UpdateSettingsFailed = "update_settings_failed";

    // ── 校验 ────────────────────────────────────────────────────
    public const string ContentRequired = "content_required";
    public const string TopicRequired = "topic_required";
    public const string FileRequired = "file_required";
    public const string IdsRequired = "ids_required";
    public const string ItemsRequired = "items_required";
    public const string ActionRequired = "action_required";
    public const string SearchQueryRequired = "search_query_required";
    public const string InvalidMonths = "invalid_months";
    public const string InvalidDateRange = "invalid_date_range";
    public const string InvalidTimeZone = "invalid_time_zone";
    public const string UnsupportedLanguage = "unsupported_language";
    public const string InvalidRequest = "invalid_request";

    // ── 不存在 / 已过期 ──────────────────────────────────────────
    public const string TopicNotFound = "topic_not_found";
    public const string MessageNotFound = "message_not_found";
    public const string MessageExpired = "message_expired";
    public const string MessageHasNoFile = "message_has_no_file";
    public const string FileNotFound = "file_not_found";
    public const string ThumbnailNotReady = "thumbnail_not_ready";
    public const string ThumbnailNotFound = "thumbnail_not_found";
    public const string RecordNotFound = "record_not_found";
    public const string UploadSessionNotFound = "upload_session_not_found";

    // ── 上传 / 存储 / 操作 ───────────────────────────────────────
    public const string StorageFull = "storage_full";
    public const string UploadFailed = "upload_failed";
    public const string SendMessageFailed = "send_message_failed";
    public const string CreateTopicFailed = "create_topic_failed";
    public const string UpdateTopicFailed = "update_topic_failed";
    public const string ReorderTopicsFailed = "reorder_topics_failed";
    public const string ResolveFailed = "resolve_failed";
    public const string BatchDeleteTooLarge = "batch_delete_too_large";

    // ── 安全 ────────────────────────────────────────────────────
    public const string InvalidOrigin = "invalid_origin";
}
