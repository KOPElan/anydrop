namespace AnyDrop.Models;

public sealed class SystemSettings
{
    public Guid Id { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public bool AutoFetchLinkPreview { get; set; } = true;

    /// <summary>用于 UI 显示时间的时区 ID（IANA 或 Windows 时区名称），默认 UTC。</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>阅后即焚消息的默认保留时长（分钟），默认 10 分钟。</summary>
    public int BurnAfterReadingMinutes { get; set; } = 10;

    /// <summary>界面语言代码，支持 zh-CN / zh-TW / en，默认简体中文。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>是否启用自动清理旧消息，默认关闭。</summary>
    public bool AutoCleanupEnabled { get; set; } = false;

    /// <summary>自动清理的时间阈值（月数），支持 1 / 3 / 6，默认 1 个月。</summary>
    public int AutoCleanupMonths { get; set; } = 1;

    /// <summary>每天自动生成缩略图的 UTC 小时（0–23），默认凌晨 2 点。</summary>
    public int ThumbnailGenerationHour { get; set; } = 2;

    /// <summary>是否启用计划任务批量生成预览图。默认关闭（关闭时上传后即时生成）。</summary>
    public bool ScheduledThumbnailGenerationEnabled { get; set; } = false;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
