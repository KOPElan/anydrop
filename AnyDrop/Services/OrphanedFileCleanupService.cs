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
    ILogger<OrphanedFileCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// 启动后延迟首次扫描，避免与迁移、缩略图批处理抢 IO。
    /// 取 1 分钟而非更久：报告模式下的扫描成本很低，而每次启动都能在日志里
    /// 看到最新对账结果，便于运维者及时发现问题。
    /// </summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, timeProvider, stoppingToken);
            await RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(ScanInterval, timeProvider);

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "孤儿文件对账任务失败。");
            }
        }
    }

    internal async Task<OrphanScanResult> RunOnceAsync(CancellationToken ct)
    {
        var deleteOrphans = configuration.GetValue("Storage:OrphanCleanupEnabled", false);

        await using var scope = serviceProvider.CreateAsyncScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<OrphanFileReconciler>();

        var result = await reconciler.ReconcileAsync(deleteOrphans, ct);

        if (result.OrphansFound == 0 && result.MissingReferencedFiles == 0)
        {
            logger.LogDebug("孤儿文件对账完成：扫描 {Scanned} 个文件，未发现异常。", result.FilesScanned);
            return result;
        }

        logger.LogInformation(
            "孤儿文件对账完成：扫描 {Scanned} 个文件；孤儿 {Orphans} 个（{Bytes} 字节），已删除 {Deleted} 个；"
            + "数据库引用了 {Missing} 个不存在的文件。模式：{Mode}。样本：{Samples}",
            result.FilesScanned,
            result.OrphansFound,
            result.OrphanBytes,
            result.OrphansDeleted,
            result.MissingReferencedFiles,
            deleteOrphans ? "删除" : "仅报告（Storage:OrphanCleanupEnabled=false）",
            result.SampleOrphans.Count == 0 ? "-" : string.Join(", ", result.SampleOrphans));

        return result;
    }
}
