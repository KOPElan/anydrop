using AnyDrop.Data;
using AnyDrop.Models;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AnyDrop.Services;

/// <summary>
/// 使用 SixLabors.ImageSharp（图片）和 ffmpeg CLI（视频）生成缩略图。
/// 缩略图以 JPEG 格式保存，最长边不超过 400 像素。
/// </summary>
public sealed class ThumbnailService(
    AnyDropDbContext dbContext,
    IFileStorageService fileStorageService,
    IConfiguration configuration,
    ILogger<ThumbnailService> logger) : IThumbnailService
{
    private const int MaxThumbnailSize = 400;
    private const string ThumbnailSubdir = "thumbnails";

    /// <inheritdoc />
    public async Task<string?> GenerateThumbnailAsync(Guid itemId, CancellationToken ct = default)
    {
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

        if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
        {
            return item.ThumbnailPath;
        }

        try
        {
            var thumbnailPath = item.ContentType == ShareContentType.Image
                ? await GenerateImageThumbnailAsync(item, ct)
                : await GenerateVideoThumbnailAsync(item, ct);

            if (thumbnailPath is not null)
            {
                item.ThumbnailPath = thumbnailPath;
                await dbContext.SaveChangesAsync(ct);
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
        // 分批处理，每次最多加载 BatchSize 条 ID，避免一次性加载全部数据导致内存峰值
        const int BatchSize = 100;
        var success = 0;
        DateTimeOffset? cursor = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var query = dbContext.ShareItems
                .Where(x => x.ThumbnailPath == null
                            && (x.ContentType == ShareContentType.Image || x.ContentType == ShareContentType.Video));

            if (cursor.HasValue)
            {
                query = query.Where(x => x.CreatedAt > cursor.Value);
            }

            var batch = await query
                .OrderBy(x => x.CreatedAt)
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

            cursor = batch[^1].CreatedAt;
            if (batch.Count < BatchSize) break;
        }

        logger.LogInformation("ThumbnailService: Generated {Count} thumbnails.", success);
        return success;
    }

    // ── 图片缩略图（SixLabors.ImageSharp，纯 .NET，无系统依赖）────────────────

    private async Task<string?> GenerateImageThumbnailAsync(ShareItem item, CancellationToken ct)
    {
        await using var originalStream = await fileStorageService.GetFileAsync(item.Content, ct);

        using var image = await Image.LoadAsync(originalStream, ct);

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

        if (!File.Exists(originalFullPath))
        {
            logger.LogWarning("ThumbnailService: Original video file not found at {Path}.", originalFullPath);
            return null;
        }

        var tempOutput = Path.Combine(Path.GetTempPath(), $"anydrop_thumb_{item.Id:N}.jpg");

        try
        {
            // 使用 ffmpeg 提取最具代表性的帧（-vf thumbnail 采样选取），
            // 再通过 scale 将宽度缩放到最大 400px，高度设为 -1 自动保持宽高比
            var exitCode = await RunFfmpegAsync(
                args: $"-y -i \"{originalFullPath}\" -vf \"thumbnail,scale={MaxThumbnailSize}:-1\" -frames:v 1 \"{tempOutput}\"",
                ct: ct);

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

    private async Task<int> RunFfmpegAsync(string args, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ffmpeg process.");

            // 必须异步读取 stdout 和 stderr，否则管道缓冲区满后进程阻塞，
            // 导致 WaitForExitAsync 永远不返回（后台任务卡死）。
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            // 等待管道读取完毕
            await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            {
                // 截断日志，避免超长 ffmpeg 输出塞满日志
                logger.LogDebug("ThumbnailService: ffmpeg stderr (first 500 chars): {Stderr}",
                    stderr[..Math.Min(500, stderr.Length)]);
            }

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning(ex, "ThumbnailService: ffmpeg not found on PATH. Video thumbnails will not be generated.");
            return -1;
        }
    }
}
