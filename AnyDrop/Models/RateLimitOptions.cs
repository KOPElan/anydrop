namespace AnyDrop.Models;

/// <summary>
/// S3-02：分端点限流的策略名（<c>RequireRateLimiting</c> 用）。
/// 登录 / 初始化沿用 S0-03 的 <c>LoginRateLimiter</c>，不在此列。
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>单次上传 + 分片初始化/传片/完成。</summary>
    public const string Upload = "upload";

    /// <summary>话题内消息搜索。</summary>
    public const string Search = "search";

    /// <summary>批量删除消息。</summary>
    public const string BatchDelete = "batch-delete";

    /// <summary>手动触发缩略图批处理。</summary>
    public const string Thumbnail = "thumbnail";
}

/// <summary>
/// S3-02：分端点限流阈值（每分钟请求数，按来源 IP 分区）。
///
/// 默认值按「正常使用不受 429 干扰、突发刷量仍被拦住」定：
/// 上传 120——分片一片 4MB，顺序传片约 480MB/分钟，弱网续传不受影响；
/// 搜索 30、批量删除 10、手动缩略图 10——都是低频人工操作。
/// 配置（RateLimit:UploadPerMinute 等，环境变量 RateLimit__UploadPerMinute）
/// 超出 1–10000 时启动即失败（见 <see cref="Validate"/>），
/// 防止误配出「等于关闭保护」的值。
/// </summary>
public sealed class RateLimitOptions
{
    public const int MinPerMinute = 1;
    public const int MaxPerMinute = 10000;

    public int UploadPerMinute { get; set; } = 120;
    public int SearchPerMinute { get; set; } = 30;
    public int BatchDeletePerMinute { get; set; } = 10;
    public int ThumbnailPerMinute { get; set; } = 10;

    /// <summary>校验全部阈值；越界时抛 InvalidOperationException，启动即失败。</summary>
    public void Validate()
    {
        ValidateOne(nameof(UploadPerMinute), UploadPerMinute);
        ValidateOne(nameof(SearchPerMinute), SearchPerMinute);
        ValidateOne(nameof(BatchDeletePerMinute), BatchDeletePerMinute);
        ValidateOne(nameof(ThumbnailPerMinute), ThumbnailPerMinute);
    }

    private static void ValidateOne(string name, int value)
    {
        if (value < MinPerMinute || value > MaxPerMinute)
        {
            throw new InvalidOperationException(
                $"RateLimit:{name} must be between {MinPerMinute} and {MaxPerMinute} (got {value}).");
        }
    }
}
