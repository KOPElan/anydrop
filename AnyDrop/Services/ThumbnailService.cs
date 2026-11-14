using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AnyDrop.Services;

/// <summary>
/// 使用 SixLabors.ImageSharp（图片）和 ffmpeg CLI（视频）生成缩略图。
/// 缩略图以 JPEG 格式保存，最长边不超过 400 像素。
///
/// S1-03（DAT-09）：用 <see cref="IDbContextFactory{TContext}"/>、
/// 每次调用建短寿命上下文；批量游标改为 (CreatedAt, Id) 复合键，
/// 同一时间戳的多条记录不再被跳过。
///
/// S1-02（DAT-07、DAT-08）：生成前校验原文件存在；图片先读头信息
/// （<see cref="Image.IdentifyAsync(Stream, CancellationToken)"/>，不解码像素）
/// 再按 <c>Thumbnails:MaxImageDimensionPixels</c> /
/// <c>Thumbnails:MaxImagePixels</c> 限流，超限拒绝全量解码；ffmpeg
/// 每文件 <c>Thumbnails:FfmpegTimeoutSeconds</c> 秒超时（默认 60 秒），
/// 超时杀进程、本文件跳过、不堵整批；已存缩略图路径但文件丢失时清除并重生成。
/// </summary>
public sealed class ThumbnailService(
    IDbContextFactory<AnyDropDbContext> dbContextFactory,
    IFileStorageService fileStorageService,
    IConfiguration configuration,
    ILogger<ThumbnailService> logger) : IThumbnailService
{
    private const int MaxThumbnailSize = 400;
    private const string ThumbnailSubdir = "thumbnails";

    /// <summary>
    /// 图片单边像素上限（默认 8192：覆盖 8K 立绘，解码峰值约 256MB RGBA）。
    /// </summary>
    private readonly int _maxImageDimensionPixels = Math.Clamp(
        configuration.GetValue("Thumbnails:MaxImageDimensionPixels", 8192), 1, 65536);

    /// <summary>
    /// 图片总像素上限（默认 33_554_432 ≈ 33.5MP，约 8K 画面，解码峰值约 134MB RGBA）。
    /// </summary>
    private readonly long _maxImagePixels = Math.Clamp(
        configuration.GetValue("Thumbnails:MaxImagePixels", 33_554_432L), 1L, 1_000_000_000L);

    /// <summary>
    /// 单个视频文件允许 ffmpeg 运行的最长时间（秒，默认 60，计划要求 30-60 秒）。
    /// </summary>
    private readonly int _ffmpegTimeoutSeconds = Math.Clamp(
        configuration.GetValue("Thumbnails:FfmpegTimeoutSeconds", 60), 5, 600);

    /// <summary>
    /// ffmpeg 可执行文件路径（默认 "ffmpeg"，走 PATH；可配绝对路径）。
    /// </summary>
    private readonly string _ffmpegPath =
        configuration["Thumbnails:FfmpegPath"] is { Length: > 0 } path ? path : "ffmpeg";

    /// <inheritdoc />
    public async Task<string?> GenerateThumbnailAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var item = await dbContext.ShareItems.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (item is null)
        {
            logger.LogWarning("ThumbnailService: ShareItem {Id} not found.", itemId);
            return null;
        }

        if (item.ContentType is not (ShareContentType.Image or ShareContentType.Video))
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
            {
                // S1-02：已存路径但文件丢失（被手动删掉）时不返回死路径，
                // 清掉后继续走重生成，缩略图自愈。
                if (await fileStorageService.FileExistsAsync(item.ThumbnailPath, ct))
                {
                    return item.ThumbnailPath;
                }

                logger.LogInformation(
                    "ThumbnailService: Thumbnail file missing for {Id} ({Path}), regenerating.",
                    itemId,
                    item.ThumbnailPath);
                item.ThumbnailPath = null;
                await dbContext.SaveChangesWithRetryAsync(ct);
            }

            // S1-02：生成前校验原文件存在，缺失直接跳过（不抛异常、不走解码）。
            if (!await fileStorageService.FileExistsAsync(item.Content, ct))
            {
                logger.LogWarning("ThumbnailService: Original file not found for {Id} ({Path}).", itemId, item.Content);
                return null;
            }

            var thumbnailPath = item.ContentType == ShareContentType.Image
                ? await GenerateImageThumbnailAsync(item, ct)
                : await GenerateVideoThumbnailAsync(item, ct);

            if (thumbnailPath is not null)
            {
                item.ThumbnailPath = thumbnailPath;
                await dbContext.SaveChangesWithRetryAsync(ct);
            }

            return thumbnailPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ThumbnailService: Failed to generate thumbnail for {Id}.", itemId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<int> ProcessPendingThumbnailsAsync(CancellationToken ct = default)
    {
        // 分批处理，每次最多加载 BatchSize 条 ID，避免一次性加载全部数据导致内存峰值。
        // 游标是 (CreatedAt, Id) 复合键：同一时间戳批量上传时，只用 CreatedAt
        // 做游标会跳过与上一批最后一条同时间戳的记录（S1-02，DAT-07/08）。
        const int BatchSize = 100;
        var success = 0;
        DateTimeOffset? cursorCreatedAt = null;
        Guid? cursorId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
            var query = dbContext.ShareItems
                .Where(x => x.ThumbnailPath == null
                            && (x.ContentType == ShareContentType.Image || x.ContentType == ShareContentType.Video));

            if (cursorCreatedAt.HasValue)
            {
                query = query.Where(x => x.CreatedAt > cursorCreatedAt.Value
                    || (x.CreatedAt == cursorCreatedAt.Value && x.Id.CompareTo(cursorId!.Value) > 0));
            }

            var batch = await query
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Take(BatchSize)
                .Select(x => new { x.Id, x.CreatedAt })
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            logger.LogDebug("ThumbnailService: Processing batch of {Count} pending items.", batch.Count);

            foreach (var item in batch)
            {
                ct.ThrowIfCancellationRequested();
                var result = await GenerateThumbnailAsync(item.Id, ct);
                if (result is not null) success++;
            }

            cursorCreatedAt = batch[^1].CreatedAt;
            cursorId = batch[^1].Id;
            if (batch.Count < BatchSize) break;
        }

        logger.LogInformation("ThumbnailService: Generated {Count} thumbnails.", success);
        return success;
    }

    // ── 图片缩略图（SixLabors.ImageSharp，纯 .NET，无系统依赖）────────────────

    private async Task<string?> GenerateImageThumbnailAsync(ShareItem item, CancellationToken ct)
    {
        await using var originalStream = await fileStorageService.GetFileAsync(item.Content, ct);

        // S1-02：先只读头信息拿尺寸（不解码像素），超限直接跳过——
        // 畸形大图（解压炸弹）绝不能走到全量解码那一步。
        var info = await Image.IdentifyAsync(originalStream, ct);
        if (info is null)
        {
            logger.LogWarning("ThumbnailService: Cannot identify image format for {Id}.", item.Id);
            return null;
        }

        if (info.Width > _maxImageDimensionPixels || info.Height > _maxImageDimensionPixels)
        {
            logger.LogWarning(
                "ThumbnailService: Image too large for thumbnail ({Width}x{Height}, limit {Limit}px per side), skipping {Id}.",
                info.Width,
                info.Height,
                _maxImageDimensionPixels,
                item.Id);
            return null;
        }

        var pixels = (long)info.Width * info.Height;
        if (pixels > _maxImagePixels)
        {
            logger.LogWarning(
                "ThumbnailService: Image too large for thumbnail ({Width}x{Height} = {Pixels}px, limit {Limit}px), skipping {Id}.",
                info.Width,
                info.Height,
                pixels,
                _maxImagePixels,
                item.Id);
            return null;
        }

        // IdentifyAsync 已把流读到头之后：可定位的流倒回开头再解码；
        // 不可定位的流重新取一次（当前存储实现返回的都是可定位流，此为防御）。
        Stream decodeStream = originalStream;
        IAsyncDisposable? refetched = null;
        if (originalStream.CanSeek)
        {
            originalStream.Position = 0;
        }
        else
        {
            decodeStream = await fileStorageService.GetFileAsync(item.Content, ct);
            refetched = decodeStream;
        }

        await using var _ = refetched; // null 时不做任何事；否则方法结束时释放重取的流
        using var image = await Image.LoadAsync(decodeStream, ct);

        // 等比缩放到最长边不超过 400px（ResizeMode.Max：保持宽高比，长边限制为给定尺寸）
        if (image.Width > MaxThumbnailSize || image.Height > MaxThumbnailSize)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxThumbnailSize, MaxThumbnailSize)
            }));
        }

        await using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, ct);
        ms.Position = 0;

        var thumbName = $"{ThumbnailSubdir}/{item.Id:N}.jpg";
        return await fileStorageService.SaveFileAtPathAsync(ms, thumbName, "image/jpeg", ct);
    }

    // ── 视频缩略图（调用系统 ffmpeg CLI）─────────────────────────────────────

    private async Task<string?> GenerateVideoThumbnailAsync(ShareItem item, CancellationToken ct)
    {
        var basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");

        // 防路径穿越：规范化路径后验证仍在 basePath 下（与 LocalFileStorageService.GetFullPath 逻辑一致）
        var safePath = item.Content.Replace('\\', '/').TrimStart('/');
        var originalFullPath = Path.GetFullPath(Path.Combine(basePath, safePath));
        var baseWithSep = basePath.EndsWith(Path.DirectorySeparatorChar)
            ? basePath
            : basePath + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!originalFullPath.StartsWith(baseWithSep, comparison))
        {
            logger.LogWarning("ThumbnailService: Path traversal attempt detected for ShareItem {Id}.", item.Id);
            return null;
        }

        // 原文件存在性已由 GenerateThumbnailAsync 的前置校验保证，这里不再重复检查。

        var tempOutput = Path.Combine(Path.GetTempPath(), $"anydrop_thumb_{item.Id:N}.jpg");

        try
        {
            // 使用 ffmpeg 提取最具代表性的帧（-vf thumbnail 采样选取），
            // 再通过 scale 将宽度缩放到最大 400px，高度设为 -1 自动保持宽高比
            var exitCode = await RunFfmpegAsync(
                [
                    "-y",
                    "-i", originalFullPath,
                    "-vf", $"thumbnail,scale={MaxThumbnailSize}:-1",
                    "-frames:v", "1",
                    tempOutput
                ],
                ct);

            if (exitCode != 0 || !File.Exists(tempOutput))
            {
                logger.LogWarning("ThumbnailService: ffmpeg exited with code {Code} for {Id}.", exitCode, item.Id);
                return null;
            }

            await using var fs = new FileStream(tempOutput, FileMode.Open, FileAccess.Read, FileShare.Read);
            var thumbStorageName = $"{ThumbnailSubdir}/{item.Id:N}.jpg";
            return await fileStorageService.SaveFileAtPathAsync(fs, thumbStorageName, "image/jpeg", ct);
        }
        finally
        {
            if (File.Exists(tempOutput))
            {
                try { File.Delete(tempOutput); }
                catch (Exception ex) { logger.LogDebug(ex, "ThumbnailService: Failed to delete temp file {Path}.", tempOutput); }
            }
        }
    }

    /// <summary>
    /// 以参数列表方式调用 ffmpeg。
    ///
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> 会按平台规则逐个转义参数，
    /// 因此即使文件路径含有引号或空格也不会被解释成额外选项。此前把路径插值进
    /// <c>Arguments</c> 字符串再用引号包裹，攻击者只要让文件名包含一个双引号即可闭合引号、
    /// 注入任意 ffmpeg 参数（Linux 上双引号是合法文件名字符）。
    /// </summary>
    /// <summary>
    /// 以参数列表方式调用 ffmpeg，返回退出码；-1 表示 ffmpeg 不存在或无法启动，
    /// -2 表示超过 <see cref="_ffmpegTimeoutSeconds"/> 秒被超时杀掉。
    /// </summary>
    private async Task<int> RunFfmpegAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ffmpeg process.");

            // 必须异步读取 stdout 和 stderr，否则管道缓冲区满后进程阻塞，
            // 导致 WaitForExitAsync 永远不返回（后台任务卡死）。
            // 读取时通过限制最大字节数避免大视频产生的海量日志消耗内存。
            var stdoutTask = ReadLimitedAsync(process.StandardOutput, maxChars: 2000, ct);
            var stderrTask = ReadLimitedAsync(process.StandardError, maxChars: 2000, ct);

            // S1-02：每文件超时。超时只杀 ffmpeg、本文件跳过，不堵整批；
            // 外层 ct（停机）触发时照常取消并向上传播。
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_ffmpegTimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    "ThumbnailService: ffmpeg timed out after {Timeout}s, killing it.",
                    _ffmpegTimeoutSeconds);
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // 进程刚好在此时自己退出了，无需再杀
                    logger.LogDebug(ex, "ThumbnailService: ffmpeg already exited when killing after timeout.");
                }

                await process.WaitForExitAsync(ct);
                await stdoutTask;
                await stderrTask;
                return -2;
            }

            // 等待管道读取完毕
            await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                logger.LogWarning(
                    "ThumbnailService: ffmpeg exited with code {Code}. Stderr (truncated): {Stderr}",
                    process.ExitCode,
                    string.IsNullOrWhiteSpace(stderr) ? "(empty)" : stderr);
            }

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning(ex, "ThumbnailService: ffmpeg not found on PATH. Video thumbnails will not be generated.");
            return -1;
        }
    }

    /// <summary>
    /// 从 <see cref="System.IO.TextReader"/> 异步读取最多 <paramref name="maxChars"/> 个字符，
    /// 剩余部分丢弃（继续读取以防管道阻塞），返回已读取的片段。
    /// </summary>
    private static async Task<string> ReadLimitedAsync(System.IO.TextReader reader, int maxChars, CancellationToken ct)
    {
        var buffer = new char[maxChars];
        int read = await reader.ReadAsync(buffer.AsMemory(0, maxChars), ct);
        // 丢弃剩余输出，确保管道不阻塞
        _ = reader.ReadToEndAsync(ct);
        return new string(buffer, 0, read);
    }
}
