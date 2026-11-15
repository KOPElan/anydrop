using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// 一次孤儿文件对账的结果。
/// </summary>
public sealed record OrphanScanResult(
    int FilesScanned,
    int OrphansFound,
    long OrphanBytes,
    int OrphansDeleted,
    int MissingReferencedFiles,
    IReadOnlyList<string> SampleOrphans,
    // S1-01 新增计数（均为本次对账内完成的数量）
    int StalePlaceholdersRemoved = 0,
    int SweepRowsRemoved = 0,
    int SweepFilesDeleted = 0,
    int QueueEntriesDrained = 0,
    int QueueEntriesFailed = 0,
    int MissingReportsOpen = 0);

/// <summary>
/// 存储目录与数据库的双向对账 + 延迟删除清扫 + 待删队列消费（S1-01）。
///
/// 存在意义：文件与数据库行会以两种方式失去同步——
///   * 磁盘上存在文件，但没有任何 ShareItem 引用它（孤儿文件，白占空间）；
///   * ShareItem 引用的文件在磁盘上已不存在（例如卷被部分恢复、文件被手工删除）。
/// 前者可以回收，后者只做报告：删除数据库行属于破坏性操作，不适合自动执行；
/// S1-01 起改为记入 <see cref="MissingFileReport"/>，供设置页人工修复。
///
/// 除孤儿扫描外，本次对账还做三件事（均不受 <c>Storage:OrphanCleanupEnabled</c>
/// 控制——那是「孤儿文件删不删」的开关，而下面三件是已设计好的生命周期）：
///   * 超期预占位清扫：Uploading 行超过 24 小时宽限期仍未转正，删行删文件；
///   * 延迟删除清扫（D5）：DeleteAfter 已到的行，删行删文件；
///   * 待删队列消费：Stage 0 攒下的 <see cref="PendingFileDeletion"/> 重试删除。
///
/// 与后台定时循环分离，便于直接用临时目录做单元测试。
///
/// S1-03（DAT-09）：用 <see cref="IDbContextFactory{TContext}"/>、
/// 每次调用建短寿命上下文；引用集合分批游标加载，不再全量 ToList。
/// </summary>
public sealed class OrphanFileReconciler(
    IDbContextFactory<AnyDropDbContext> dbContextFactory,
    IConfiguration configuration,
    IFileStorageService fileStorageService,
    TimeProvider timeProvider,
    ILogger<OrphanFileReconciler> logger)
{
    /// <summary>
    /// 宽限期：只处理「最后写入时间早于该阈值」的文件。
    /// 正在进行中的上传与缩略图生成，都会在磁盘上短暂存在尚未被引用的文件；
    /// 没有宽限期就会误删。超期预占位行同样按本宽限期判定。
    /// </summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    /// <summary>返回样本路径的上限，避免日志被大量路径淹没。</summary>
    private const int MaxSamples = 10;

    /// <summary>分批游标的每批大小。</summary>
    private const int BatchSize = 1000;

    /// <summary>清扫（预占位 / 延迟删除）每批提交的大小。</summary>
    private const int SweepBatchSize = 500;

    public async Task<OrphanScanResult> ReconcileAsync(bool deleteOrphans, CancellationToken ct = default)
    {
        var basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");
        var now = timeProvider.GetUtcNow();

        // S1-03：引用集合分批游标加载（每批 1000 条），不再把全部文件类
        // 消息一次性 ToList 进内存。游标是 (CreatedAt, Id) 复合键。
        // 注意必须 IgnoreQueryFilters：已写 DeleteAfter 标记的行对常规读
        // 路径不可见，但它们的文件在 7 天清扫期满之前仍受保护——若按过滤
        // 后的集合算引用，清扫期内的文件会被对账误判为孤儿而提前删掉。
        // Uploading 预占位行同样进引用集合（保护正在上传的文件），
        // 但缺失统计只算 Ready 活行——上传中的行文件还没落盘是正常的。
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var liveReferenced = new HashSet<string>(StringComparer.Ordinal);
        var liveRefs = new List<(Guid ItemId, string Path, string Kind)>();
        var liveItemIds = new HashSet<Guid>();
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(ct))
        {
            DateTimeOffset? cursorCreatedAt = null;
            Guid? cursorId = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var pageQuery = dbContext.ShareItems
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.ContentType == ShareContentType.Image
                                || x.ContentType == ShareContentType.Video
                                || x.ContentType == ShareContentType.File);
                if (cursorCreatedAt.HasValue)
                {
                    pageQuery = pageQuery.Where(x => x.CreatedAt > cursorCreatedAt.Value
                        || (x.CreatedAt == cursorCreatedAt.Value && x.Id.CompareTo(cursorId!.Value) > 0));
                }

                var page = await pageQuery
                    .OrderBy(x => x.CreatedAt)
                    .ThenBy(x => x.Id)
                    .Select(x => new { x.Content, x.ThumbnailPath, x.CreatedAt, x.Id, x.DeleteAfter, x.UploadState })
                    .Take(BatchSize)
                    .ToListAsync(ct);
                if (page.Count == 0)
                {
                    break;
                }

                // 引用集合：一条消息可能同时占用原文件与缩略图两个路径。
                // 已标记延迟删除的行仍占用引用（保护文件到清扫期满），
                // 但「缺失文件」只对未标记的活行计数——待清扫行的文件丢了
                // 由清扫任务收尾，不应再进缺失修复入口。
                foreach (var item in page)
                {
                    var isLive = item.DeleteAfter is null && item.UploadState == UploadState.Ready;
                    if (isLive)
                    {
                        liveItemIds.Add(item.Id);
                    }

                    if (!string.IsNullOrWhiteSpace(item.Content))
                    {
                        var content = NormalizeRelativePath(item.Content);
                        referenced.Add(content);
                        if (isLive)
                        {
                            liveReferenced.Add(content);
                            liveRefs.Add((item.Id, content, "content"));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
                    {
                        var thumbnail = NormalizeRelativePath(item.ThumbnailPath);
                        referenced.Add(thumbnail);
                        if (isLive)
                        {
                            liveReferenced.Add(thumbnail);
                            liveRefs.Add((item.Id, thumbnail, "thumbnail"));
                        }
                    }
                }

                cursorCreatedAt = page[^1].CreatedAt;
                cursorId = page[^1].Id;
                if (page.Count < BatchSize)
                {
                    break;
                }
            }
        }

        // 先统计缺失文件。即使存储目录整体不存在也要统计：
        // 目录不存在通常意味着卷没挂上，此时所有引用文件都不可用，必须明确报警，
        // 而不是返回一个「一切正常」的空结果。
        // 只对活行计数（见上：待清扫行的缺失由清扫任务收尾）。
        var missing = FindMissingReferencedFiles(basePath, liveRefs);

        if (!Directory.Exists(basePath))
        {
            logger.LogWarning(
                "存储目录不存在，跳过孤儿文件扫描：{BasePath}（数据库引用的文件中缺失 {Missing} 个）",
                basePath,
                missing.Count);
            // 目录都没了，清扫与队列消费也无从谈起，但缺失报告仍要记——
            // 这正是最需要人工修复的时候。
            var openWhenNoDir = await SyncMissingFileReportsAsync(missing, liveItemIds, ct);
            return new OrphanScanResult(0, 0, 0, 0, missing.Count, [], MissingReportsOpen: openWhenNoDir);
        }

        var cutoffUtc = (now - GracePeriod).UtcDateTime;

        // 先物化文件列表再遍历：循环内会删除文件，边枚举边删除在部分文件系统上行为不确定
        string[] files;
        try
        {
            files = Directory.GetFiles(basePath, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "枚举存储目录失败，跳过本次对账：{BasePath}", basePath);
            return new OrphanScanResult(0, 0, 0, 0, 0, []);
        }

        var filesScanned = 0;
        var orphansFound = 0;
        var orphansDeleted = 0;
        long orphanBytes = 0;
        var samples = new List<string>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            filesScanned++;

            var relativePath = NormalizeRelativePath(Path.GetRelativePath(basePath, file));

            // 原子写入产生的临时文件（.{guid}.part）；崩溃残留的同样按宽限期回收
            var isTempFile = relativePath.EndsWith(".part", StringComparison.Ordinal);
            if (!isTempFile && referenced.Contains(relativePath))
            {
                continue;
            }

            // 宽限期内：可能正在写入或刚刚上传完成，跳过
            if (!TryGetLastWriteUtc(file, out var lastWriteUtc) || lastWriteUtc > cutoffUtc)
            {
                continue;
            }

            orphansFound++;
            orphanBytes += TryGetLength(file);
            if (samples.Count < MaxSamples)
            {
                samples.Add(relativePath);
            }

            if (!deleteOrphans)
            {
                continue;
            }

            try
            {
                await fileStorageService.DeleteFileAsync(relativePath, ct);
                orphansDeleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "删除孤儿文件失败：{Path}", relativePath);
            }
        }

        // S1-01：三件生命周期清扫（不受孤儿删除开关控制）+ 缺失报告同步。
        // 顺序：先孤儿扫描（引用集合是扫描开始时拍的快照），再清扫——
        // 清扫删掉的行，其文件在扫描里仍受保护，不会被孤儿扫描误删；
        // 清扫本身是这些文件的授权删除者。
        var missingReportsOpen = await SyncMissingFileReportsAsync(missing, liveItemIds, ct);
        var stalePlaceholdersRemoved = await SweepStalePlaceholdersAsync(now, ct);
        var (sweepRowsRemoved, sweepFilesDeleted) = await SweepExpiredDeletesAsync(now, liveReferenced, ct);
        var (queueDrained, queueFailed) = await DrainPendingDeletionQueueAsync(now, ct);

        return new OrphanScanResult(
            filesScanned, orphansFound, orphanBytes, orphansDeleted, missing.Count, samples,
            StalePlaceholdersRemoved: stalePlaceholdersRemoved,
            SweepRowsRemoved: sweepRowsRemoved,
            SweepFilesDeleted: sweepFilesDeleted,
            QueueEntriesDrained: queueDrained,
            QueueEntriesFailed: queueFailed,
            MissingReportsOpen: missingReportsOpen);
    }

    /// <summary>
    /// 缺失引用同步（S1-01）：活行引用但磁盘上不存在的文件，记入修复记录；
    /// 文件恢复或消息已删的旧记录自动标记已解决。
    /// 返回当前未解决的记录数。绝不自动删数据库行。
    /// </summary>
    private async Task<int> SyncMissingFileReportsAsync(
        IReadOnlyList<(Guid ItemId, string Path, string Kind)> missing,
        HashSet<Guid> liveItemIds,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var openReports = await db.MissingFileReports
            .Where(r => !r.IsResolved)
            .ToListAsync(ct);
        var openByKey = openReports.ToDictionary(
            r => (r.ShareItemId, r.StoragePath, r.Kind));

        var changed = false;
        var newlyDetected = 0;
        foreach (var (itemId, path, kind) in missing)
        {
            if (openByKey.TryGetValue((itemId, path, kind), out var existing))
            {
                existing.LastSeenAt = now;
                changed = true;
            }
            else
            {
                db.MissingFileReports.Add(new MissingFileReport
                {
                    ShareItemId = itemId,
                    StoragePath = path,
                    Kind = kind,
                    FirstDetectedAt = now,
                    LastSeenAt = now
                });
                newlyDetected++;
                changed = true;
            }
        }

        // 自动解决：不在本次缺失集合里的未解决记录——文件恢复了，
        // 或消息已不在活行里（被删、被清扫、被标记），都不再需要人工修复。
        var missingKeys = missing.Select(m => (m.ItemId, m.Path, m.Kind)).ToHashSet();
        foreach (var report in openReports)
        {
            if (missingKeys.Contains((report.ShareItemId, report.StoragePath, report.Kind)))
            {
                continue;
            }

            var itemStillLive = liveItemIds.Contains(report.ShareItemId);
            var fileBack = File.Exists(Path.Combine(
                Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files"),
                report.StoragePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!itemStillLive || fileBack)
            {
                report.IsResolved = true;
                report.ResolvedAt = now;
                report.ResolvedBy = "auto";
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesWithRetryAsync(ct);
        }

        if (newlyDetected > 0)
        {
            logger.LogWarning(
                "发现 {Count} 个缺失引用（数据库引用、磁盘上不存在），已记入修复记录，"
                + "请在设置页存储健康区处理；绝不自动删除数据库行。样本：{Samples}",
                newlyDetected,
                string.Join(", ", missing.Take(MaxSamples).Select(m => m.Path)));
        }

        return await db.MissingFileReports.CountAsync(r => !r.IsResolved, ct);
    }

    /// <summary>
    /// 超期预占位清扫（S1-01）：Uploading 行超过 24 小时宽限期仍未转正，
    /// 说明上传进程已死或确认步骤丢失。删行（先提交）再删文件，删文件失败入队。
    /// 返回清理的行数。
    /// </summary>
    private async Task<int> SweepStalePlaceholdersAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - GracePeriod;
        var removed = 0;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        DateTimeOffset? cursorCreatedAt = null;
        Guid? cursorId = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var pageQuery = db.ShareItems
                .IgnoreQueryFilters()
                .Where(x => x.UploadState == UploadState.Uploading && x.CreatedAt < cutoff);
            if (cursorCreatedAt.HasValue)
            {
                pageQuery = pageQuery.Where(x => x.CreatedAt > cursorCreatedAt.Value
                    || (x.CreatedAt == cursorCreatedAt.Value && x.Id.CompareTo(cursorId!.Value) > 0));
            }

            var page = await pageQuery
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Take(SweepBatchSize)
                .ToListAsync(ct);
            if (page.Count == 0)
            {
                break;
            }

            // 行先删先提交，再删文件；删文件失败入队重试，不回滚行删除。
            db.ShareItems.RemoveRange(page);
            await db.SaveChangesWithRetryAsync(ct);
            removed += page.Count;

            foreach (var item in page)
            {
                await DeleteItemFilesOrEnqueueAsync(db, item, "stale-placeholder-sweep", ct);
            }

            cursorCreatedAt = page[^1].CreatedAt;
            cursorId = page[^1].Id;
            if (page.Count < SweepBatchSize)
            {
                break;
            }
        }

        if (removed > 0)
        {
            logger.LogInformation("超期预占位清扫：清理 {Count} 行（上传超过 24 小时未转正）。", removed);
        }

        return removed;
    }

    /// <summary>
    /// 延迟删除清扫（S1-01，D5）：DeleteAfter 已到的行，删行（先提交）再删文件。
    /// 与另一活行共享同一路径的文件不删（只删行）。删文件失败入队。
    /// 返回（清理行数，删除文件数）。
    /// </summary>
    private async Task<(int Rows, int Files)> SweepExpiredDeletesAsync(
        DateTimeOffset now,
        HashSet<string> liveReferenced,
        CancellationToken ct)
    {
        var rowsRemoved = 0;
        var filesDeleted = 0;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        DateTimeOffset? cursorDeleteAfter = null;
        Guid? cursorId = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var pageQuery = db.ShareItems
                .IgnoreQueryFilters()
                .Where(x => x.DeleteAfter != null && x.DeleteAfter <= now);
            if (cursorDeleteAfter.HasValue)
            {
                pageQuery = pageQuery.Where(x => x.DeleteAfter > cursorDeleteAfter.Value
                    || (x.DeleteAfter == cursorDeleteAfter.Value && x.Id.CompareTo(cursorId!.Value) > 0));
            }

            var page = await pageQuery
                .OrderBy(x => x.DeleteAfter)
                .ThenBy(x => x.Id)
                .Take(SweepBatchSize)
                .ToListAsync(ct);
            if (page.Count == 0)
            {
                break;
            }

            db.ShareItems.RemoveRange(page);
            await db.SaveChangesWithRetryAsync(ct);
            rowsRemoved += page.Count;

            foreach (var item in page)
            {
                filesDeleted += await DeleteItemFilesOrEnqueueAsync(
                    db, item, "expired-delete-sweep", ct, liveReferenced);
            }

            cursorDeleteAfter = page[^1].DeleteAfter;
            cursorId = page[^1].Id;
            if (page.Count < SweepBatchSize)
            {
                break;
            }
        }

        if (rowsRemoved > 0)
        {
            logger.LogInformation(
                "延迟删除清扫：清理 {Rows} 行，删除 {Files} 个文件（DeleteAfter 已到）。",
                rowsRemoved,
                filesDeleted);
        }

        return (rowsRemoved, filesDeleted);
    }

    /// <summary>
    /// 待删队列消费（S1-01）：Stage 0 攒下的 <see cref="PendingFileDeletion"/> 重试删除。
    /// 规则：文件已不在 → 撤队；仍被活行引用 → 撤队（永不删活文件）并记警告；
    /// 指向 DeleteAfter 未到的行 → 跳过等清扫；其余按退避重试。
    /// 返回（成功撤队数，失败数）。
    /// </summary>
    private async Task<(int Drained, int Failed)> DrainPendingDeletionQueueAsync(
        DateTimeOffset now,
        CancellationToken ct)
    {
        var drained = 0;
        var failed = 0;
        var basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var entries = await db.PendingFileDeletions
            .OrderBy(e => e.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(basePath, entry.StoragePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                db.PendingFileDeletions.Remove(entry);
                drained++;
                continue;
            }

            // 永不删仍被活行引用的文件：队列记录过期了，撤队并警告。
            var stillReferenced = await db.ShareItems.AnyAsync(
                x => x.Content == entry.StoragePath || x.ThumbnailPath == entry.StoragePath, ct);
            if (stillReferenced)
            {
                logger.LogWarning(
                    "待删队列记录指向仍被活行引用的文件，已撤队不再删除：{Path}（原消息 {ItemId}，原因：{Reason}）",
                    entry.StoragePath,
                    entry.ShareItemId,
                    entry.Reason);
                db.PendingFileDeletions.Remove(entry);
                drained++;
                continue;
            }

            // 指向 DeleteAfter 未到的行：等清扫期满再说。
            if (entry.ShareItemId.HasValue)
            {
                var owner = await db.ShareItems
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == entry.ShareItemId.Value, ct);
                if (owner?.DeleteAfter > now)
                {
                    continue;
                }
            }

            // 退避重试：首次 1 小时，之后翻倍，封顶 24 小时。
            // 按入队时间 + 退避算，不另加字段。
            var backoff = RetryBackoff(entry.RetryCount);
            if (now < entry.CreatedAt + backoff)
            {
                continue;
            }

            try
            {
                await fileStorageService.DeleteFileAsync(entry.StoragePath, ct);
                db.PendingFileDeletions.Remove(entry);
                drained++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                entry.RetryCount++;
                failed++;
                logger.LogWarning(
                    ex,
                    "待删队列重试失败：{Path}（第 {RetryCount} 次，下次退避 {Backoff}）。",
                    entry.StoragePath,
                    entry.RetryCount,
                    RetryBackoff(entry.RetryCount));
                if (entry.RetryCount >= 7)
                {
                    logger.LogError(
                        "待删队列记录已失败 {RetryCount} 次，请人工检查文件权限或占用：{Path}（原消息 {ItemId}）。",
                        entry.RetryCount,
                        entry.StoragePath,
                        entry.ShareItemId);
                }
            }
        }

        if (drained > 0 || failed > 0)
        {
            await db.SaveChangesWithRetryAsync(ct);
        }

        return (drained, failed);
    }

    /// <summary>退避：1、2、4、8、16、24、24……小时。</summary>
    private static TimeSpan RetryBackoff(int retryCount)
    {
        var hours = 1 << Math.Min(retryCount, 5);
        return TimeSpan.FromHours(Math.Min(hours, 24));
    }

    /// <summary>
    /// 删一条消息关联的文件（原文件 + 缩略图）。与另一活行共享的路径跳过。
    /// 返回实际删除的文件数；删失败的入待删队列（不抛错，不阻断整批）。
    /// </summary>
    private async Task<int> DeleteItemFilesOrEnqueueAsync(
        AnyDropDbContext db,
        ShareItem item,
        string context,
        CancellationToken ct,
        HashSet<string>? liveReferenced = null)
    {
        var deleted = 0;
        var paths = new List<(string Path, Guid? ItemId)>();
        if (item.ContentType is ShareContentType.Image or ShareContentType.Video or ShareContentType.File
            && !string.IsNullOrWhiteSpace(item.Content))
        {
            paths.Add((item.Content, item.Id));
        }

        if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
        {
            paths.Add((item.ThumbnailPath, item.Id));
        }

        var basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");
        foreach (var (relativePath, itemId) in paths)
        {
            var normalized = NormalizeRelativePath(relativePath);
            if (liveReferenced is not null && liveReferenced.Contains(normalized))
            {
                continue;
            }

            var fullPath = Path.Combine(basePath, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                await fileStorageService.DeleteFileAsync(normalized, ct);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "清扫删文件失败，已入队重试：{Path}（{Context}）", normalized, context);
                await PendingFileDeletionQueue.EnqueueAsync(
                    db, itemId, normalized, $"{context}: {ex.Message}", ct);
            }
        }

        return deleted;
    }

    private static List<(Guid ItemId, string Path, string Kind)> FindMissingReferencedFiles(
        string basePath,
        List<(Guid ItemId, string Path, string Kind)> liveRefs)
    {
        var missing = new List<(Guid, string, string)>();
        foreach (var (itemId, relative, kind) in liveRefs)
        {
            var fullPath = Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                missing.Add((itemId, relative, kind));
            }
        }

        return missing;
    }

    /// <summary>统一为以正斜杠分隔的相对路径，确保与磁盘枚举结果比较时一致。</summary>
    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static bool TryGetLastWriteUtc(string file, out DateTime lastWriteUtc)
    {
        try
        {
            lastWriteUtc = File.GetLastWriteTimeUtc(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lastWriteUtc = default;
            return false;
        }
    }

    private static long TryGetLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
