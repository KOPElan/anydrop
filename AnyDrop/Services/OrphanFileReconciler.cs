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
/// </summary>
public sealed class OrphanFileReconciler(
    AnyDropDbContext dbContext,
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

        var items = await dbContext.ShareItems
            .AsNoTracking()
            .Where(x => x.ContentType == ShareContentType.Image
                        || x.ContentType == ShareContentType.Video
                        || x.ContentType == ShareContentType.File)
            .Select(x => new { x.Content, x.ThumbnailPath })
            .ToListAsync(ct);

        // 引用集合：一条消息可能同时占用原文件与缩略图两个路径
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Content))
            {
                referenced.Add(NormalizeRelativePath(item.Content));
            }

            if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
            {
                referenced.Add(NormalizeRelativePath(item.ThumbnailPath));
            }
        }

        // 先统计缺失文件。即使存储目录整体不存在也要统计：
        // 目录不存在通常意味着卷没挂上，此时所有引用文件都不可用，必须明确报警，
        // 而不是返回一个「一切正常」的空结果。
        var missing = CountMissingReferencedFiles(basePath, referenced);

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
