using AnyDrop.Models;

namespace AnyDrop.Services;

/// <summary>
/// 清理一条 <see cref="ShareItem"/> 在磁盘上关联的全部文件。
///
/// 独立出来的原因：一条消息可能同时拥有原文件（<c>Content</c>）与缩略图（<c>ThumbnailPath</c>），
/// 而此前三处删除逻辑（手动清理、批量删除、过期自动清理）都只删了 <c>Content</c>，
/// 导致 <c>Storage:BasePath/thumbnails/</c> 下的 JPEG 永久残留。
/// 把「一条消息对应哪些文件」收敛到一处，避免再次遗漏。
/// </summary>
internal static class ShareItemFileCleanup
{
    /// <summary>
    /// 删除条目自身携带的文件（原文件 + 缩略图）。
    /// 失败只记录警告，不向上抛出——否则单个文件删除失败会阻断整批清理。
    /// </summary>
    public static async Task DeleteItemFilesAsync(
        IFileStorageService fileStorageService,
        ILogger logger,
        ShareItem item,
        CancellationToken ct,
        string context)
    {
        if (item.ContentType is ShareContentType.Image or ShareContentType.Video or ShareContentType.File)
        {
            await TryDeleteAsync(fileStorageService, logger, item.Content, item.Id, ct, context);
        }

        if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
        {
            await TryDeleteAsync(fileStorageService, logger, item.ThumbnailPath, item.Id, ct, context);
        }
    }

    /// <summary>按存储路径删除单个文件，失败仅记录警告。</summary>
    public static async Task TryDeleteAsync(
        IFileStorageService fileStorageService,
        ILogger logger,
        string? storagePath,
        Guid itemId,
        CancellationToken ct,
        string context)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return;
        }

        try
        {
            await fileStorageService.DeleteFileAsync(storagePath, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to delete file {StoragePath} for item {ItemId} during {Context}.",
                storagePath,
                itemId,
                context);
        }
    }
}
