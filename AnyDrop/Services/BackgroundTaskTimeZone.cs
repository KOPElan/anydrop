namespace AnyDrop.Services;

/// <summary>
/// 后台任务的统一调度时区（D6，2026-11-14 Elan 确认；S1-07）。
///
/// 来源：配置 <c>BackgroundTasks:TimeZoneId</c>（环境变量
/// <c>BackgroundTasks__TimeZoneId</c>，IANA ID，如 <c>Asia/Shanghai</c>）。
/// 未设置时用当前计算机时区（<see cref="TimeZoneInfo.Local"/>）；ID 无效时
/// 记 Error 并回退本机时区，**永不静默用 UTC**。
///
/// 生效时区与来源在构造时记一条 Information 日志（启动时由 Program 显式
/// 解析一次，保证日志一定出现）；到期时间存储仍用 UTC，调度与「今天」的
/// 换算才按本时区算。Web 界面展示用的浏览器时区是另一回事，与此无关。
/// </summary>
public sealed class BackgroundTaskTimeZone
{
    /// <summary>后台任务的每日锚定小时（对账）：凌晨 3 点。</summary>
    public const int DailyAnchorHour = 3;

    public TimeZoneInfo TimeZone { get; }

    /// <summary>生效来源说明，用于启动日志与排障。</summary>
    public string Source { get; }

    public BackgroundTaskTimeZone(IConfiguration configuration, ILogger<BackgroundTaskTimeZone> logger)
    {
        var configured = configuration["BackgroundTasks:TimeZoneId"]?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            TimeZone = TimeZoneInfo.Local;
            Source = "未配置 BackgroundTasks:TimeZoneId，使用当前计算机时区";
        }
        else
        {
            try
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(configured);
                Source = $"BackgroundTasks:TimeZoneId={configured}";
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                logger.LogError(
                    ex,
                    "BackgroundTasks:TimeZoneId='{Configured}' 无效，回退到当前计算机时区（{LocalId}）。请检查配置，IANA ID 如 Asia/Shanghai。",
                    configured,
                    TimeZoneInfo.Local.Id);
                TimeZone = TimeZoneInfo.Local;
                Source = $"BackgroundTasks:TimeZoneId={configured} 无效，已回退本机时区";
            }
        }

        logger.LogInformation(
            "后台任务时区：{TimeZoneId}（{Source}）。容器内本机时区通常是 UTC，要与宿主机一致请显式配置 BackgroundTasks:TimeZoneId 或透传 TZ。",
            TimeZone.Id,
            Source);
    }

    /// <summary>把 UTC 时刻换算成本时区的本地日期（用于「今天是否已执行」这类按日判断）。</summary>
    public DateOnly TodayLocal(DateTimeOffset utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, TimeZone).DateTime);

    /// <summary>把 UTC 时刻换算成本时区时间（仅用于日志展示）。</summary>
    public DateTimeOffset ToLocal(DateTimeOffset utcNow)
        => TimeZoneInfo.ConvertTime(utcNow, TimeZone);

    /// <summary>
    /// 下一次本时区 <paramref name="hour"/> 点整对应的 UTC 时刻。
    /// 夏令时跳变日不存在的那一小时按惯例顺延一小时（与 ShareService 的换算一致）。
    /// </summary>
    public DateTimeOffset GetNextRunUtc(int hour, DateTimeOffset utcNow)
    {
        var localNow = TimeZoneInfo.ConvertTime(utcNow, TimeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var candidate = ToZonedTime(localDate, hour);
        if (candidate <= utcNow)
        {
            candidate = ToZonedTime(localDate.AddDays(1), hour);
        }

        return candidate;
    }

    /// <summary>到下一次本时区 <paramref name="hour"/> 点整的等待时长。</summary>
    public TimeSpan GetDelayUntilNextDailyRun(int hour, DateTimeOffset utcNow)
        => GetNextRunUtc(hour, utcNow) - utcNow;

    private DateTimeOffset ToZonedTime(DateOnly date, int hour)
    {
        var local = date.ToDateTime(new TimeOnly(hour, 0));
        if (TimeZone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local));
    }
}
