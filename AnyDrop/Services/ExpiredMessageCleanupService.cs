using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Logging;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// 后台服务：每分钟检查并标记已到期的"定时销毁"消息；
/// 同时每天（后台任务时区日期变更后）检查是否需要执行自动清理旧消息任务。
/// 标记（<c>DeleteAfter</c>）一提交消息即不可见，文件由 Stage 1 的清扫任务
/// 在到期后物理删除（D5）；本服务不再直接删文件。
/// </summary>
public sealed class ExpiredMessageCleanupService(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    DbWriteGate dbWriteGate,
    BackgroundTaskTimeZone taskTimeZone,
    ILogger<ExpiredMessageCleanupService> logger) : BackgroundService
{
    // 记录上次执行自动清理的日期（后台任务时区），避免一天内重复执行
    private DateOnly _lastAutoCleanupDate = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // S3-04：用 LogMarkers 常量记日志，摘要按同一字符串计数。
                logger.LogWarning(ex, LogMarkers.ExpiredCleanupFailed);
            }

            // 每天后台任务时区日期变更后执行一次自动清理检查
            // （D6：按后台任务时区算「今天」，而不是 UTC 日——否则在
            // Asia/Shanghai 下，UTC 日已翻但本地还是昨天，清理会被多触发一次）
            var today = taskTimeZone.TodayLocal(timeProvider.GetUtcNow());
            if (today > _lastAutoCleanupDate)
            {
                try
                {
                    await RunAutoCleanupIfEnabledAsync(stoppingToken);
                    // 仅在成功后更新日期，确保失败时下个周期可以重试
                    _lastAutoCleanupDate = today;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // S3-04：用 LogMarkers 常量记日志，摘要按同一字符串计数。
                    logger.LogWarning(ex, LogMarkers.AutoCleanupFailed);
                }
            }
        }
    }

    /// <summary>
    /// 执行一轮过期清理。internal 以便测试直接验证标记 + 广播行为
    /// （后台循环本身用 PeriodicTimer 驱动，不适合在单元测试里跑）。
    /// </summary>
    internal async Task CleanupExpiredAsync(CancellationToken ct)
    {
        // S0-04：后台写操作串行化——与缩略图批处理互斥写库，避免两个定时任务争写锁。
        using var _ = await dbWriteGate.AcquireAsync(ct);

        // S1-03：上下文由工厂现建现用，本轮清理结束即释放。
        // Hub 与 TopicService 仍从独立 Scope 取（TopicService 是 scoped 服务）。
        await using var scope = serviceProvider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AnyDropDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ShareHub>>();

        var now = timeProvider.GetUtcNow();
        var expired = await db.ShareItems
            .Where(x => x.ExpiresAt != null && x.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return;
        }

        // 收集受影响的 Topic IDs，以便事后更新主题元数据
        var affectedTopicIds = expired
            .Where(x => x.TopicId.HasValue)
            .Select(x => x.TopicId!.Value)
            .Distinct()
            .ToHashSet();

        // S0-05（D5）：过期清理同样只写延迟删除标记，不立即删行、不立即删文件。
        // 标记一提交，消息就对所有读路径不可见；文件由 Stage 1 的清扫任务
        // 在 DeleteAfter 到期后物理删除。
        var deleteAfter = now.AddDays(DeletionPolicy.DelayedDeleteDays);
        foreach (var item in expired)
        {
            item.DeleteAfter = deleteAfter;
        }

        await db.SaveChangesWithRetryAsync(ct);
        logger.LogInformation("Marked {Count} expired timed-deletion message(s) for delayed deletion.", expired.Count);

        // S0-06：广播被标记的消息 ID，让已打开的页面立即移除对应气泡，
        // 不必等下一轮清理或手动刷新。读过滤（S0-06）已让新请求看不到它们，
        // 但已打开的页面只有收到广播才会同步消失。
        var markedIds = expired.Select(x => x.Id).ToList();
        await hubContext.Clients.All.SendAsync("ShareItemsDeleted", markedIds, CancellationToken.None);

        // 更新受影响主题的 LastMessageAt/LastMessagePreview 并广播
        if (affectedTopicIds.Count > 0)
        {
            var topics = await db.Topics
                .Where(t => affectedTopicIds.Contains(t.Id))
                .ToListAsync(ct);

            foreach (var topic in topics)
            {
                // S0-06：主题预览不取已到期的消息（本批刚标记的已被全局过滤器排除，
                // 这里再叠加过期过滤，覆盖标记前一刻刚好到期的消息）。
                var latest = await db.ShareItems
                    .NotExpired(timeProvider.GetUtcNow())
                    .Where(x => x.TopicId == topic.Id)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                topic.LastMessageAt = latest?.CreatedAt;
                topic.LastMessagePreview = latest is null
                    ? null
                    : (latest.FileName ?? latest.Content) is { } preview
                        ? preview.Length > 80 ? preview[..80] + "…" : preview
                        : null;
            }

            await db.SaveChangesWithRetryAsync(ct);

            // 广播 TopicsUpdated 让侧边栏刷新
            var topicService = scope.ServiceProvider.GetRequiredService<ITopicService>();
            var allTopics = await topicService.GetAllTopicsAsync(ct);

            await hubContext.Clients.All.SendAsync("TopicsUpdated", allTopics, ct);
        }
    }

    /// <summary>
    /// 读取系统设置，若自动清理已启用，则调用 ShareService 清理超龄消息。
    /// </summary>
    private async Task RunAutoCleanupIfEnabledAsync(CancellationToken ct)
    {
        // S0-04：同上，自动清理的写操作也走同一把锁。
        using var _ = await dbWriteGate.AcquireAsync(ct);

        await using var scope = serviceProvider.CreateAsyncScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
        var (enabled, months) = await settingsService.GetAutoCleanupSettingsAsync(ct);

        if (!enabled)
        {
            return;
        }

        var shareService = scope.ServiceProvider.GetRequiredService<IShareService>();
        var deleted = await shareService.CleanupOldMessagesAsync(months, null, ct);

        if (deleted > 0)
        {
            logger.LogInformation("Auto cleanup marked {Count} message(s) older than {Months} month(s) for delayed deletion.", deleted, months);
        }
    }
}
