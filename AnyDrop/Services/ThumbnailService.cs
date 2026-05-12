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
        var pending = await dbContext.ShareItems
            .Where(x => x.ThumbnailPath == null
                        && (x.ContentType == ShareContentType.Image || x.ContentType == ShareContentType.Video))
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .ToListAsync(ct);

        logger.LogInformation("ThumbnailService: Processing {Count} pending items.", pending.Count);

        var success = 0;
        foreach (var id in pending)
        {
            ct.ThrowIfCancellationRequested();
            var result = await GenerateThumbnailAsync(id, ct);
            if (result is not null) success++;
        }

        return success;
    }

    // ── 图片缩略图（SixLabors.ImageSharp，纯 .NET，无系统依赖）────────────────

    private async Task<string?> GenerateImageThumbnailAsync(ShareItem item, CancellationToken ct)
    {
        await using var originalStream = await fileStorageService.GetFileAsync(item.Content, ct);

        using var image = await Image.LoadAsync(originalStream, ct);

        // 等比缩放到最长边 400px
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
        var originalFullPath = Path.GetFullPath(Path.Combine(basePath, item.Content.TrimStart('/')));

        if (!File.Exists(originalFullPath))
        {
            logger.LogWarning("ThumbnailService: Original video file not found at {Path}.", originalFullPath);
            return null;
        }

        var tempOutput = Path.Combine(Path.GetTempPath(), $"anydrop_thumb_{item.Id:N}.jpg");

        try
        {
            // 使用 ffmpeg 提取第一帧，-vf thumbnail 选择最具代表性的帧
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
                try { File.Delete(tempOutput); } catch { /* 忽略清理失败 */ }
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

            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning(ex, "ThumbnailService: ffmpeg not found on PATH. Video thumbnails will not be generated.");
            return -1;
        }
    }
}
