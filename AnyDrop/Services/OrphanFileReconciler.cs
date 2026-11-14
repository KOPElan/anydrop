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
    IReadOnlyList<string> SampleOrphans);

/// <summary>
/// 存储目录与数据库的双向对账。
///
/// 存在意义：文件与数据库行会以两种方式失去同步——
///   * 磁盘上存在文件，但没有任何 ShareItem 引用它（孤儿文件，白占空间）；
///   * ShareItem 引用的文件在磁盘上已不存在（例如卷被部分恢复、文件被手工删除）。
/// 前者可以回收，后者只做报告：删除数据库行属于破坏性操作，不适合自动执行。
///
/// 与后台定时循环分离，便于直接用临时目录做单元测试。
///
/// S1-03（DAT-09）：用 <see cref="IDbContextFactory{TContext}"/>、
/// 每次调用建短寿命上下文；引用集合分批游标加载，不再全量 ToList。
/// </summary>
public sealed class OrphanFileReconciler(
    IDbContextFactory<AnyDropDbContext> dbContextFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<OrphanFileReconciler> logger)
{
    /// <summary>
    /// 宽限期：只处理「最后写入时间早于该阈值」的文件。
    /// 正在进行中的上传与缩略图生成，都会在磁盘上短暂存在尚未被引用的文件；
    /// 没有宽限期就会误删。
    /// </summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    /// <summary>返回样本路径的上限，避免日志被大量路径淹没。</summary>
    private const int MaxSamples = 10;

    public async Task<OrphanScanResult> ReconcileAsync(bool deleteOrphans, CancellationToken ct = default)
    {
        var basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");

        // S1-03：引用集合分批游标加载（每批 1000 条），不再把全部文件类
        // 消息一次性 ToList 进内存。游标是 (CreatedAt, Id) 复合键。
        // 注意必须 IgnoreQueryFilters：已写 DeleteAfter 标记的行对常规读
        // 路径不可见，但它们的文件在 7 天清扫期满之前仍受保护——若按过滤
        // 后的集合算引用，清扫期内的文件会被对账误判为孤儿而提前删掉。
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var liveReferenced = new HashSet<string>(StringComparer.Ordinal);
        await using (var dbContext = await dbContextFactory.CreateDbContextAsync(ct))
        {
            DateTimeOffset? cursorCreatedAt = null;
            Guid? cursorId = null;
            const int batchSize = 1000;
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
                    .Select(x => new { x.Content, x.ThumbnailPath, x.CreatedAt, x.Id, x.DeleteAfter })
                    .Take(batchSize)
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
                    var isLive = item.DeleteAfter is null;
                    if (!string.IsNullOrWhiteSpace(item.Content))
                    {
                        referenced.Add(NormalizeRelativePath(item.Content));
                        if (isLive)
                        {
                            liveReferenced.Add(NormalizeRelativePath(item.Content));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
                    {
                        referenced.Add(NormalizeRelativePath(item.ThumbnailPath));
                        if (isLive)
                        {
                            liveReferenced.Add(NormalizeRelativePath(item.ThumbnailPath));
                        }
                    }
                }

                cursorCreatedAt = page[^1].CreatedAt;
                cursorId = page[^1].Id;
                if (page.Count < batchSize)
                {
                    break;
                }
            }
        }

        // 先统计缺失文件。即使存储目录整体不存在也要统计：
        // 目录不存在通常意味着卷没挂上，此时所有引用文件都不可用，必须明确报警，
        // 而不是返回一个「一切正常」的空结果。
        // 只对活行计数（见上：待清扫行的缺失由清扫任务收尾）。
        var missing = CountMissingReferencedFiles(basePath, liveReferenced);

        if (!Directory.Exists(basePath))
        {
            logger.LogWarning(
                "存储目录不存在，跳过孤儿文件扫描：{BasePath}（数据库引用的文件中缺失 {Missing} 个）",
                basePath,
                missing);
            return new OrphanScanResult(0, 0, 0, 0, missing, []);
        }

        var cutoffUtc = (timeProvider.GetUtcNow() - GracePeriod).UtcDateTime;

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
                File.Delete(file);
                orphansDeleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "删除孤儿文件失败：{Path}", relativePath);
            }
        }

        return new OrphanScanResult(
            filesScanned, orphansFound, orphanBytes, orphansDeleted, missing, samples);
    }

    private static int CountMissingReferencedFiles(string basePath, HashSet<string> referenced)
    {
        var missing = 0;

        foreach (var relative in referenced)
        {
            var fullPath = Path.Combine(basePath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                missing++;
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
