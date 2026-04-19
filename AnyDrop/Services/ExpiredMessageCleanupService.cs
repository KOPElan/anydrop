using AnyDrop.Data;
using AnyDrop.Models;
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

        var now = DateTimeOffset.UtcNow;
        var expired = await db.ShareItems
            .Where(x => x.ExpiresAt != null && x.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return;
        }

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
    }
}
