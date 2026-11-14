using AnyDrop.Data;
using AnyDrop.Shared;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// 存储健康：缺失引用修复入口（S1-01，DAT-02）。
///
/// 对账发现的缺失引用记在 <see cref="Models.MissingFileReport"/> 里，
/// 这里提供查看与人工处理：删除对应消息，或标记已处理。
/// 文件恢复后对账会自动标记已解决；这里不做任何自动删除。
/// </summary>
public sealed class StorageHealthService(
    IDbContextFactory<AnyDropDbContext> dbContextFactory,
    IShareService shareService,
    TimeProvider timeProvider)
{
    /// <summary>列出全部未解决的缺失引用记录（按最近发现倒序）。</summary>
    public async Task<IReadOnlyList<MissingFileReportDto>> ListMissingFileReportsAsync(
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var reports = await db.MissingFileReports
            .Where(r => !r.IsResolved)
            .OrderByDescending(r => r.LastSeenAt)
            .ToListAsync(ct);
        if (reports.Count == 0)
        {
            return [];
        }

        var itemIds = reports.Select(r => r.ShareItemId).Distinct().ToList();
        var items = await db.ShareItems
            .IgnoreQueryFilters()
            .Where(x => itemIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FileName, x.TopicId })
            .ToListAsync(ct);
        var itemById = items.ToDictionary(x => x.Id);

        var topicIds = items
            .Where(x => x.TopicId.HasValue)
            .Select(x => x.TopicId!.Value)
            .Distinct()
            .ToList();
        var topicById = await db.Topics
            .Where(t => topicIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return reports.Select(r =>
        {
            itemById.TryGetValue(r.ShareItemId, out var item);
            string? topicName = null;
            if (item?.TopicId.HasValue == true)
            {
                topicById.TryGetValue(item.TopicId.Value, out topicName);
            }

            return new MissingFileReportDto(
                r.Id,
                r.ShareItemId,
                r.StoragePath,
                r.Kind,
                item?.FileName,
                topicName,
                r.FirstDetectedAt,
                r.LastSeenAt);
        }).ToList();
    }

    /// <summary>
    /// 处理一条缺失引用记录。action：delete-message（删除对应消息）或 dismiss（标记已处理）。
    /// 返回 false 表示记录不存在；已解决的记录重复处理返回 true（幂等）。
    /// </summary>
    public async Task<bool> ResolveMissingFileAsync(Guid reportId, string action, CancellationToken ct = default)
    {
        if (action is not ("delete-message" or "dismiss"))
        {
            throw new ArgumentException($"未知的处理方式：{action}。", nameof(action));
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var report = await db.MissingFileReports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null)
        {
            return false;
        }

        if (report.IsResolved)
        {
            return true;
        }

        if (action == "delete-message")
        {
            // 走正常的消息删除链（删行、删残留文件、广播），不要在这里手写删行——
            // 那条链还负责主题预览与客户端同步。文件本就缺失，删文件步骤会直接成功。
            var itemExists = await db.ShareItems
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == report.ShareItemId, ct);
            if (itemExists)
            {
                await shareService.DeleteShareItemsAsync([report.ShareItemId], ct);
            }
        }

        report.IsResolved = true;
        report.ResolvedAt = timeProvider.GetUtcNow();
        report.ResolvedBy = action;
        await db.SaveChangesWithRetryAsync(ct);
        return true;
    }
}
