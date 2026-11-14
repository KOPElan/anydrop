namespace AnyDrop.Shared;

// ── 公共 API 响应 Envelope ──────────────────────────────────────────────────────

/// <summary>统一的 API 响应信封。所有 API 响应均返回此结构。</summary>
/// <param name="ErrorCode">
/// 失败时的稳定错误码（见 <see cref="ApiErrorCodes"/>），成功时为 null。
/// 客户端按码查本地化文案；<paramref name="Error"/> 文本保留作旧客户端回退与日志排障。
/// </param>
public sealed record ApiEnvelope<T>(bool Success, T? Data, string? Error, string? ErrorCode = null)
{
    /// <summary>返回成功响应。</summary>
    public static ApiEnvelope<T> Ok(T? data = default) => new(true, data, null);

    /// <summary>返回失败响应。</summary>
    public static ApiEnvelope<T> Fail(string error, string? errorCode = null) => new(false, default, error, errorCode);
}

// ── 认证相关 DTO ──────────────────────────────────────────────────────────────

/// <summary>获取初始化状态的响应。</summary>
public sealed record SetupStatusDto(bool RequiresSetup);

/// <summary>首次初始化请求。</summary>
public sealed record SetupRequest(string Nickname, string Password, string ConfirmPassword);

/// <summary>登录请求。ReturnUrl 仅 Web 端使用，移动端可忽略。</summary>
public sealed record LoginRequest(string Password, string? ReturnUrl = null);

/// <summary>登录/初始化成功后的响应体（data 字段）。RefreshToken 为空表示服务端未签发（旧客户端兼容：仍可凭 AccessToken 使用）。</summary>
public sealed record LoginResponse(UserProfileDto User, string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null, DateTimeOffset? RefreshTokenExpiresAt = null);

/// <summary>刷新 Access Token 的请求。</summary>
public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>Hub 专用短 token 响应。</summary>
public sealed record HubTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

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
    int AutoCleanupMonths,
    int ThumbnailGenerationHour = 2,
    bool ScheduledThumbnailGenerationEnabled = false);

/// <summary>更新安全与偏好设置请求。</summary>
public sealed record UpdateSecuritySettingsRequest(
    bool AutoFetchLinkPreview,
    int BurnAfterReadingMinutes,
    string Language,
    bool AutoCleanupEnabled,
    int AutoCleanupMonths,
    int ThumbnailGenerationHour = 2,
    bool ScheduledThumbnailGenerationEnabled = false);

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
    Guid? TopicId,
    string? ThumbnailPath = null
);

/// <summary>发送文本消息请求。BurnAfterReading=true 时按系统配置时长自动过期。</summary>
public sealed record CreateTextShareItemRequest(string Content, Guid? TopicId = null, bool BurnAfterReading = false);

// ── 存储健康 DTO（S1-01，DAT-02）──────────────────────────────────────────────

/// <summary>缺失引用修复记录（未解决）。FileName 为对应消息的文件名（文本消息为 null）。</summary>
public sealed record MissingFileReportDto(
    Guid Id,
    Guid ShareItemId,
    string StoragePath,
    string Kind,
    string? FileName,
    string? TopicName,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastSeenAt
);

/// <summary>处理缺失引用记录的请求。Action：delete-message（删除对应消息）或 dismiss（标记已处理）。</summary>
public sealed record ResolveMissingFileRequest(string Action);

// ── 分片上传 DTO（S2-07，CLI-05）───────────────────────────────────────────────

/// <summary>分片上传初始化请求。TotalSha256 可空：客户端可在上传各片时增量计算，完成后提交。</summary>
public sealed record InitUploadRequest(
    string FileName,
    long TotalSize,
    string? TotalSha256,
    string IdempotencyKey,
    Guid? TopicId = null,
    bool BurnAfterReading = false,
    string? MimeType = null);

/// <summary>分片上传初始化响应。同一幂等键重复初始化返回同一 UploadId 与已确认分片清单。</summary>
public sealed record InitUploadResponse(
    Guid UploadId,
    int ChunkSize,
    IReadOnlyList<int> ConfirmedChunks,
    DateTimeOffset ExpiresAt);

/// <summary>分片上传状态（断点续传查询）。</summary>
public sealed record UploadStatusResponse(
    Guid UploadId,
    string FileName,
    long TotalSize,
    int ChunkSize,
    IReadOnlyList<int> ConfirmedChunks,
    string Status,
    DateTimeOffset ExpiresAt);

/// <summary>完成分片上传请求。TotalSha256 有值时服务端合并后校验，不一致则拒绝完成（分片需重传）。</summary>
public sealed record CompleteUploadRequest(string? TotalSha256 = null);

/// <summary>单片上传确认响应。</summary>
public sealed record ChunkUploadResponse(int ChunkIndex, long Size, IReadOnlyList<int> ConfirmedChunks);
