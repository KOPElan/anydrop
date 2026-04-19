using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// 后台服务：每分钟检查并删除已到期的"阅后即焚"消息。
/// 删除时同步清理对应的文件（Image/Video/File 类型）。
/// </summary>
public sealed class ExpiredMessageCleanupService(
    IServiceProvider serviceProvider,
    ILogger<ExpiredMessageCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Expired message cleanup cycle failed.");
            }
        }
    }

    private async Task CleanupExpiredAsync(CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnyDropDbContext>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<ShareHub>>();

        var now = DateTimeOffset.UtcNow;
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

        foreach (var item in expired)
        {
            // 文件类消息：同步删除物理文件，失败时只记录警告，不阻断其余条目的清理
            if (item.ContentType is ShareContentType.Image or ShareContentType.Video or ShareContentType.File)
            {
                try
                {
                    await fileStorage.DeleteFileAsync(item.Content, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete expired file {StoragePath} for item {ItemId}.", item.Content, item.Id);
                }
            }

            db.ShareItems.Remove(item);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Deleted {Count} expired burn-after-reading message(s).", expired.Count);

        // 更新受影响主题的 LastMessageAt/LastMessagePreview 并广播
        if (affectedTopicIds.Count > 0)
        {
            var topics = await db.Topics
                .Where(t => affectedTopicIds.Contains(t.Id))
                .ToListAsync(ct);

            foreach (var topic in topics)
            {
                var latest = await db.ShareItems
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

            await db.SaveChangesAsync(ct);

            // 广播 TopicsUpdated 让侧边栏刷新
            var topicService = scope.ServiceProvider.GetRequiredService<ITopicService>();
            var allTopics = await topicService.GetAllTopicsAsync(ct);

            await hubContext.Clients.All.SendAsync("TopicsUpdated", allTopics, ct);
        }
    }
}
