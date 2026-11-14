namespace AnyDrop.Services;

/// <summary>
/// 每日执行一次存储目录与数据库的对账。
///
/// **默认只报告、不删除**（<c>Storage:OrphanCleanupEnabled=false</c>）。
/// 这是一个主打「数据在自己手里」的自托管应用，静默删除文件的代价高于回收一点磁盘空间；
/// 运维者可以先看日志里的报告，确认无误后再开启实际删除。
/// </summary>
public sealed class OrphanedFileCleanupService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    TimeProvider timeProvider,
    BackgroundTaskTimeZone taskTimeZone,
    ILogger<OrphanedFileCleanupService> logger) : BackgroundService
{
    /// <summary>
    /// 启动后延迟首次扫描，避免与迁移、缩略图批处理抢 IO。
    /// 取 1 分钟而非更久：报告模式下的扫描成本很低，而每次启动都能在日志里
    /// 看到最新对账结果，便于运维者及时发现问题。
    /// </summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // S1-07：首轮失败不再终止本服务。旧代码里 InitialDelay + 首次 RunOnceAsync
        // 在 try/catch 之外（只有 PeriodicTimer 循环内有 catch），首轮抛错会让
        // ExecuteAsync 直接退出、之后再也不对账。改为捕获并继续走每日锚定循环。
        try
        {
            await Task.Delay(InitialDelay, timeProvider, stoppingToken);
            await RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "孤儿文件对账首轮失败，宿主继续运行，将在下一锚定时刻重试。");
        }

        // 此后锚定到后台任务时区（D6）的每日凌晨 3 点执行，而不是「启动后每 24 小时」——
        // 这样三个后台任务的触发时间都按同一时区对齐，日志里的时刻也与运维者的本地时间一致。
        // 每次都重新算下一次锚点，夏令时跳变日也不会漂移。
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = taskTimeZone.GetDelayUntilNextDailyRun(
                    BackgroundTaskTimeZone.DailyAnchorHour, timeProvider.GetUtcNow());
                logger.LogDebug(
                    "孤儿文件对账：下次在后台任务时区 {TimeZoneId} 凌晨 {Hour} 点执行（{Minutes:F0} 分钟后）。",
                    taskTimeZone.TimeZone.Id,
                    BackgroundTaskTimeZone.DailyAnchorHour,
                    delay.TotalMinutes);
                await Task.Delay(delay, timeProvider, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "孤儿文件对账任务失败，将在下一锚定时刻重试。");
            }
        }
    }

    internal async Task<OrphanScanResult> RunOnceAsync(CancellationToken ct)
    {
        var deleteOrphans = configuration.GetValue("Storage:OrphanCleanupEnabled", false);

        await using var scope = serviceProvider.CreateAsyncScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<OrphanFileReconciler>();

        var result = await reconciler.ReconcileAsync(deleteOrphans, ct);

        if (result.OrphansFound == 0 && result.MissingReferencedFiles == 0
            && result.StalePlaceholdersRemoved == 0 && result.SweepRowsRemoved == 0
            && result.QueueEntriesDrained == 0 && result.QueueEntriesFailed == 0)
        {
            logger.LogDebug("孤儿文件对账完成：扫描 {Scanned} 个文件，未发现异常。", result.FilesScanned);
            return result;
        }

        logger.LogInformation(
            "孤儿文件对账完成：扫描 {Scanned} 个文件；孤儿 {Orphans} 个（{Bytes} 字节），已删除 {Deleted} 个；"
            + "数据库引用了 {Missing} 个不存在的文件（未解决修复记录 {OpenReports} 个）；"
            + "超期预占位清理 {Placeholders} 行；延迟删除清扫 {SweepRows} 行 / {SweepFiles} 个文件；"
            + "待删队列消费 {QueueDrained} 条、失败 {QueueFailed} 条。模式：{Mode}。样本：{Samples}",
            result.FilesScanned,
            result.OrphansFound,
            result.OrphanBytes,
            result.OrphansDeleted,
            result.MissingReferencedFiles,
            result.MissingReportsOpen,
            result.StalePlaceholdersRemoved,
            result.SweepRowsRemoved,
            result.SweepFilesDeleted,
            result.QueueEntriesDrained,
            result.QueueEntriesFailed,
            deleteOrphans ? "删除" : "仅报告（Storage:OrphanCleanupEnabled=false）",
            result.SampleOrphans.Count == 0 ? "-" : string.Join(", ", result.SampleOrphans));

        return result;
    }
}
