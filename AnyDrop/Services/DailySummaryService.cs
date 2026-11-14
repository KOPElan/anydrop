using System.Globalization;
using AnyDrop.Data;
using AnyDrop.Logging;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// 每日运维摘要（S3-04）。
///
/// 每天后台任务时区凌晨 3 点（与对账任务同一锚点）生成一份上一日的摘要，
/// 写到日志目录的 <c>summaries/daily-summary-&lt;yyyy-MM-dd&gt;.md</c>，
/// 同时在日志里记一行计数。摘要先落文件，不做推送告警。
///
/// 计数来源（复用 Stage 1 已有的对账与存储健康逻辑，不另起一套计数器）：
/// - 孤儿文件 / 缺失引用 / 清扫结果：报告模式对账
///   （<c>ReconcileAsync(deleteOrphans: false)</c>，不删孤儿文件；
///   但与每日对账任务一样执行已授权的生命周期清扫——超期预占位、
///   到期延迟删除、待删除队列——清扫结果本身就是摘要的一部分）；
/// - 待删除队列深度：<c>PendingFileDeletions</c> 行数；
/// - 缩略图失败 / 清理失败：统计当日日志文件里对应标记的行数
///   （标记见 <see cref="LogMarkers"/>，与日志调用点共用常量）；
/// - FTS 索引一致性：S3-03 的 <c>GetConsistencyAsync</c>。
///
/// 失败统计的窗口是摘要日（后台任务时区）的 00:00–24:00；
/// 日志文件按 UTC 日期切分，统计时按行首时间戳换算进窗口，
/// 跨 UTC 日界的窗口会读两个日志文件。日志文件缺失（日志被禁用或
/// 刚装机）时失败计数记为 n/a，不影响其他计数。
/// </summary>
public sealed class DailySummaryService(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    BackgroundTaskTimeZone taskTimeZone,
    FileLoggerOptions fileLoggerOptions,
    IConfiguration configuration,
    ILogger<DailySummaryService> logger) : BackgroundService
{
    /// <summary>摘要文件保留天数（日志本身 14 天，摘要留 90 天）。</summary>
    public const int SummaryRetainedDays = 90;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("DailySummary:Enabled", true))
        {
            logger.LogInformation("每日摘要已禁用（DailySummary:Enabled=false）。");
            return;
        }

        // 与对账任务同一锚点：后台任务时区每日凌晨 3 点。
        // 每次重新算下一次锚点，夏令时跳变日也不会漂移。
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = taskTimeZone.GetDelayUntilNextDailyRun(
                    BackgroundTaskTimeZone.DailyAnchorHour, timeProvider.GetUtcNow());
                await Task.Delay(delay, timeProvider, stoppingToken);

                var date = taskTimeZone.TodayLocal(timeProvider.GetUtcNow()).AddDays(-1);
                var summary = await BuildSummaryAsync(date, stoppingToken);
                await WriteSummaryAsync(summary, stoppingToken);
                logger.LogInformation(
                    "每日摘要已生成（{Date}）：孤儿 {Orphans} 个（{OrphanBytes} 字节），缺失引用 {Missing} 个，"
                    + "待删除队列 {Queue} 条，缩略图失败 {Thumb} 次，清理失败 {Cleanup} 次。",
                    summary.Date,
                    summary.OrphansFound,
                    summary.OrphanBytes,
                    summary.MissingReferencedFiles,
                    summary.PendingDeleteQueue,
                    summary.ThumbnailFailures?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
                    summary.CleanupFailures?.ToString(CultureInfo.InvariantCulture) ?? "n/a");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // 摘要失败不影响主服务；下一锚点再试。
                logger.LogWarning(ex, "每日摘要生成失败，将在下一锚定时刻重试。");
            }
        }
    }

    /// <summary>
    /// 生成指定日（后台任务时区日期）的摘要。internal 以便测试直接验证计数
    /// （后台循环本身用延时驱动，不适合在单元测试里跑）。
    /// </summary>
    internal async Task<DailySummary> BuildSummaryAsync(DateOnly date, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<OrphanFileReconciler>();
        var result = await reconciler.ReconcileAsync(deleteOrphans: false, ct);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AnyDropDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var pendingQueue = await db.PendingFileDeletions.CountAsync(ct);

        long? ftsTotal = null;
        long? ftsIndexed = null;
        try
        {
            (ftsTotal, ftsIndexed) = await MessageSearchFts.GetConsistencyAsync(db, ct);
        }
        catch (Exception ex)
        {
            // FTS 不可用时一致性计数缺失，不让摘要整体失败。
            logger.LogWarning(ex, "每日摘要：FTS 一致性计数失败。");
        }

        var (thumbnailFailures, cleanupFailures) = CountFailuresFromLogs(date);

        return new DailySummary(
            date,
            result.FilesScanned,
            result.OrphansFound,
            result.OrphanBytes,
            result.MissingReferencedFiles,
            result.MissingReportsOpen,
            pendingQueue,
            result.SweepRowsRemoved,
            result.SweepFilesDeleted,
            result.QueueEntriesDrained,
            result.QueueEntriesFailed,
            result.StalePlaceholdersRemoved,
            thumbnailFailures,
            cleanupFailures,
            ftsTotal,
            ftsIndexed);
    }

    /// <summary>统计摘要日窗口内日志里的失败次数（null 表示日志文件不可用）。</summary>
    internal (int? ThumbnailFailures, int? CleanupFailures) CountFailuresFromLogs(DateOnly date)
    {
        if (!fileLoggerOptions.Enabled)
        {
            return (null, null);
        }

        // 窗口：摘要日 00:00–24:00（后台任务时区），换算成 UTC 再读日志。
        var zone = taskTimeZone.TimeZone;
        var midnightInZone = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var windowStartUtc = TimeZoneInfo.ConvertTimeToUtc(midnightInZone, zone);
        var windowEndUtc = TimeZoneInfo.ConvertTimeToUtc(midnightInZone.AddDays(1), zone);
        var windowStart = new DateTimeOffset(windowStartUtc, TimeSpan.Zero);
        var windowEnd = new DateTimeOffset(windowEndUtc, TimeSpan.Zero);

        var files = new List<string>();
        try
        {
            if (!Directory.Exists(fileLoggerOptions.Directory))
            {
                return (null, null);
            }

            // 窗口最多跨两个 UTC 日期（含滚动文件）。
            for (var d = DateOnly.FromDateTime(windowStartUtc); d <= DateOnly.FromDateTime(windowEndUtc); d = d.AddDays(1))
            {
                files.AddRange(Directory.EnumerateFiles(
                    fileLoggerOptions.Directory,
                    $"anydrop-{d:yyyyMMdd}*.log"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "每日摘要：读取日志文件失败，失败计数不可用。");
            return (null, null);
        }

        if (files.Count == 0)
        {
            return (null, null);
        }

        var thumbnails = 0;
        var cleanups = 0;
        foreach (var file in files)
        {
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    // 行首是 ISO 8601 UTC 时间戳；解析失败的行不计入。
                    var space = line.IndexOf(' ');
                    if (space < 0
                        || !DateTimeOffset.TryParse(
                            line[..space], CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var timestamp)
                        || timestamp < windowStart || timestamp >= windowEnd)
                    {
                        continue;
                    }

                    if (line.Contains(LogMarkers.ThumbnailFailedPrefix, StringComparison.Ordinal))
                    {
                        thumbnails++;
                    }

                    foreach (var marker in LogMarkers.CleanupFailedMarkers)
                    {
                        if (line.Contains(marker, StringComparison.Ordinal))
                        {
                            cleanups++;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "每日摘要：读取日志文件 {File} 失败，跳过。", file);
            }
        }

        return (thumbnails, cleanups);
    }

    /// <summary>把摘要写成 Markdown 文件，并清理 90 天前的旧摘要。</summary>
    internal async Task WriteSummaryAsync(DailySummary summary, CancellationToken ct)
    {
        var summariesDir = Path.Combine(fileLoggerOptions.Directory, "summaries");
        Directory.CreateDirectory(summariesDir);
        var path = Path.Combine(summariesDir, $"daily-summary-{summary.Date:yyyy-MM-dd}.md");
        await File.WriteAllTextAsync(path, RenderMarkdown(summary), ct);

        try
        {
            var cutoff = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime).AddDays(-SummaryRetainedDays);
            foreach (var file in Directory.EnumerateFiles(summariesDir, "daily-summary-*.md"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (DateOnly.TryParseExact(
                        name["daily-summary-".Length..], "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate)
                    && fileDate < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "每日摘要：清理旧摘要文件失败。");
        }
    }

    /// <summary>渲染摘要 Markdown（纯函数，便于测试）。</summary>
    public static string RenderMarkdown(DailySummary summary) =>
        $"""
        # 每日运维摘要 {summary.Date:yyyy-MM-dd}

        生成时区：后台任务时区；失败统计窗口为该日 00:00–24:00（按日志行首 UTC 时间戳换算）。
        孤儿/缺失/清扫计数来自当日报告模式对账；待删除队列为摘要生成时刻的行数。

        | 指标 | 数量 |
        |---|---:|
        | 扫描文件 | {summary.FilesScanned} |
        | 孤儿文件 | {summary.OrphansFound}（{summary.OrphanBytes} 字节） |
        | 缺失引用（活行引用但磁盘不存在） | {summary.MissingReferencedFiles} |
        | 未解决缺失修复记录 | {summary.MissingReportsOpen} |
        | 待删除队列 | {summary.PendingDeleteQueue} |
        | 延迟删除清扫 | {summary.SweepRowsRemoved} 行 / {summary.SweepFilesDeleted} 个文件 |
        | 待删除队列已处理 | {summary.QueueEntriesDrained} 条（失败 {summary.QueueEntriesFailed} 条） |
        | 超期预占位清理 | {summary.StalePlaceholdersRemoved} 行 |
        | 缩略图失败 | {FormatNullable(summary.ThumbnailFailures)} |
        | 清理失败（过期清理 / 孤儿对账 / 分片清扫） | {FormatNullable(summary.CleanupFailures)} |
        | FTS 索引一致性（消息行 / 已索引） | {FormatNullable(summary.FtsTotal)} / {FormatNullable(summary.FtsIndexed)} |

        失败计数为 n/a 表示当日日志文件不可用（日志被禁用或刚装机）。
        FTS 计数为 n/a 表示 FTS5 不可用或一致性查询失败。
        """;

    private static string FormatNullable(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    private static string FormatNullable(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
}

/// <summary>一日运维摘要的计数快照。</summary>
public sealed record DailySummary(
    DateOnly Date,
    int FilesScanned,
    int OrphansFound,
    long OrphanBytes,
    int MissingReferencedFiles,
    int MissingReportsOpen,
    int PendingDeleteQueue,
    int SweepRowsRemoved,
    int SweepFilesDeleted,
    int QueueEntriesDrained,
    int QueueEntriesFailed,
    int StalePlaceholdersRemoved,
    int? ThumbnailFailures,
    int? CleanupFailures,
    long? FtsTotal,
    long? FtsIndexed);
