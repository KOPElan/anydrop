namespace AnyDrop.Services;

/// <summary>
/// 后台任务：每天在系统设置配置的 UTC 小时运行一次，批量为所有未处理的图片/视频生成缩略图。
/// </summary>
public sealed class ThumbnailGenerationBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<ThumbnailGenerationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ThumbnailGenerationBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var targetHour = await GetTargetHourAsync(stoppingToken);
                var now = DateTime.UtcNow;
                var nextRun = new DateTime(now.Year, now.Month, now.Day, targetHour, 0, 0, DateTimeKind.Utc);
                if (nextRun <= now)
                {
                    nextRun = nextRun.AddDays(1);
                }

                var delay = nextRun - now;
                logger.LogInformation(
                    "ThumbnailGenerationBackgroundService: next run at {NextRun:yyyy-MM-dd HH:mm} UTC (in {Minutes:F0} minutes).",
                    nextRun,
                    delay.TotalMinutes);

                await Task.Delay(delay, stoppingToken);

                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 正常停止，退出循环
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ThumbnailGenerationBackgroundService: Unhandled error in background loop.");
                // 发生意外错误时等待 5 分钟后重试，避免死循环空转
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        logger.LogInformation("ThumbnailGenerationBackgroundService stopped.");
    }

    /// <summary>立即执行一次缩略图批处理（可由 API 手动触发）。</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var thumbnailService = scope.ServiceProvider.GetRequiredService<IThumbnailService>();

        logger.LogInformation("ThumbnailGenerationBackgroundService: Starting thumbnail batch.");
        var count = await thumbnailService.ProcessPendingThumbnailsAsync(ct);
        logger.LogInformation("ThumbnailGenerationBackgroundService: Generated {Count} thumbnails.", count);
    }

    private async Task<int> GetTargetHourAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
        return await settingsService.GetThumbnailGenerationHourAsync(ct);
    }
}
