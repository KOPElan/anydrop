using AnyDrop.Data;
using AnyDrop.Models;

namespace AnyDrop.Services;

/// <summary>
/// 待删除文件队列的入队入口（S0-05，DAT-01）。
/// 队列的消费（重试删除 / 到期清理）落在 Stage 1，本阶段只建表 + 入队。
/// </summary>
internal static class PendingFileDeletionQueue
{
    /// <summary>把一次文件删除失败记入队列，单独提交（调用时原行已删，不可再随原事务回滚）。</summary>
    public static async Task EnqueueAsync(
        AnyDropDbContext dbContext,
        Guid? shareItemId,
        string storagePath,
        string reason,
        CancellationToken ct)
    {
        dbContext.PendingFileDeletions.Add(new PendingFileDeletion
        {
            ShareItemId = shareItemId,
            StoragePath = storagePath,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesWithRetryAsync(ct);
    }
}
