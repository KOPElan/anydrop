namespace AnyDrop.Shared;

// ── 公共 API 响应 Envelope ──────────────────────────────────────────────────────

/// <summary>统一的 API 响应信封。所有 API 响应均返回此结构。</summary>
public sealed record ApiEnvelope<T>(bool Success, T? Data, string? Error)
{
    /// <summary>返回成功响应。</summary>
    public static ApiEnvelope<T> Ok(T? data = default) => new(true, data, null);

    /// <summary>返回失败响应。</summary>
    public static ApiEnvelope<T> Fail(string error) => new(false, default, error);
}

// ── 认证相关 DTO ──────────────────────────────────────────────────────────────

/// <summary>获取初始化状态的响应。</summary>
public sealed record SetupStatusDto(bool RequiresSetup);

/// <summary>首次初始化请求。</summary>
public sealed record SetupRequest(string Nickname, string Password, string ConfirmPassword);

/// <summary>登录请求。ReturnUrl 仅 Web 端使用，移动端可忽略。</summary>
public sealed record LoginRequest(string Password, string? ReturnUrl = null);

/// <summary>登录/初始化成功后的响应体（data 字段）。</summary>
public sealed record LoginResponse(UserProfileDto User, string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>当前用户信息。</summary>
public sealed record UserProfileDto(string Nickname, DateTimeOffset? LastLoginAt = null);

/// <summary>注销结果。</summary>
public sealed record LogoutResultDto(bool LoggedOut);

/// <summary>更新昵称请求。</summary>
public sealed record UpdateNicknameRequest(string Nickname);

/// <summary>修改密码请求。</summary>
public sealed record UpdatePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);

// ── 系统安全设置 DTO ──────────────────────────────────────────────────────────

/// <summary>安全与偏好设置。</summary>
public sealed record SecuritySettingsDto(
    bool AutoFetchLinkPreview,
    int BurnAfterReadingMinutes,
    string Language,
    bool AutoCleanupEnabled,
    int AutoCleanupMonths);

/// <summary>更新安全与偏好设置请求。</summary>
public sealed record UpdateSecuritySettingsRequest(
    bool AutoFetchLinkPreview,
    int BurnAfterReadingMinutes,
    string Language,
    bool AutoCleanupEnabled,
    int AutoCleanupMonths);

/// <summary>手动清理操作结果。</summary>
public sealed record CleanupResult(int DeletedCount);

/// <summary>批量删除消息请求。</summary>
public sealed record BatchDeleteRequest(List<Guid> Ids);

// ── 主题 DTO ──────────────────────────────────────────────────────────────────

/// <summary>主题数据传输对象。</summary>
public sealed record TopicDto(
    Guid Id,
    string Name,
    string Icon,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMessageAt,
    int MessageCount,
    bool IsBuiltIn,
    string? LastMessagePreview,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    DateTimeOffset? ArchivedAt
);

/// <summary>创建主题请求。</summary>
public sealed record CreateTopicRequest(string Name);

/// <summary>更新主题名称请求。</summary>
public sealed record UpdateTopicRequest(string Name);

/// <summary>更新主题图标请求。</summary>
public sealed record UpdateTopicIconRequest(string Icon);

/// <summary>置顶主题请求。</summary>
public sealed record PinTopicRequest(bool IsPinned);

/// <summary>归档主题请求。</summary>
public sealed record ArchiveTopicRequest(bool IsArchived);

/// <summary>批量重排主题顺序请求。</summary>
public sealed record ReorderTopicsRequest(IReadOnlyList<TopicOrderItem> Items);

/// <summary>单个主题排序项。</summary>
public sealed record TopicOrderItem(Guid TopicId, int SortOrder);

/// <summary>主题消息列表响应（游标分页）。</summary>
public sealed record TopicMessagesResponse(
    IReadOnlyList<ShareItemDto> Messages,
    bool HasMore,
    string? NextCursor
);

// ── 分享条目 DTO ──────────────────────────────────────────────────────────────

/// <summary>分享内容类型枚举。</summary>
public enum ShareContentType
{
    Text = 0,
    File = 1,
    Image = 2,
    Video = 3,
    Link = 4
}

/// <summary>分享条目数据传输对象。Content 字段含义随类型不同：Text/Link 为文本内容，File/Image/Video 为存储路径。</summary>
public sealed record ShareItemDto(
    Guid Id,
    ShareContentType ContentType,
    string Content,
    string? FileName,
    long? FileSize,
    string? MimeType,
    string? LinkTitle,
    string? LinkDescription,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    Guid? TopicId
);

/// <summary>发送文本消息请求。BurnAfterReading=true 时按系统配置时长自动过期。</summary>
public sealed record CreateTextShareItemRequest(string Content, Guid? TopicId = null, bool BurnAfterReading = false);
