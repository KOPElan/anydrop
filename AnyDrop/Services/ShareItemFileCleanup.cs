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
    /// 返回删除失败的存储路径——调用方须把失败入队
    /// （<see cref="PendingFileDeletionQueue"/>），而不是只记一条日志。
    /// 文件本就不存在时视为成功（存储层删除不存在的文件是空操作）。
    /// </summary>
    public static async Task<IReadOnlyList<string>> DeleteItemFilesAsync(
        IFileStorageService fileStorageService,
        ILogger logger,
        ShareItem item,
        CancellationToken ct,
        string context)
    {
        var failed = new List<string>();

        if (item.ContentType is ShareContentType.Image or ShareContentType.Video or ShareContentType.File)
        {
            if (!await TryDeleteAsync(fileStorageService, logger, item.Content, item.Id, ct, context))
            {
                failed.Add(item.Content);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.ThumbnailPath))
        {
            if (!await TryDeleteAsync(fileStorageService, logger, item.ThumbnailPath, item.Id, ct, context))
            {
                failed.Add(item.ThumbnailPath);
            }
        }

        return failed;
    }

    /// <summary>
    /// 按存储路径删除单个文件。返回 <c>true</c> 表示已删或本就不存在；
    /// 返回 <c>false</c> 表示真实 IO 失败（调用方应入队重试）。
    /// 失败只记录警告，不向上抛出——否则单个文件删除失败会阻断整批清理。
    /// </summary>
    public static async Task<bool> TryDeleteAsync(
        IFileStorageService fileStorageService,
        ILogger logger,
        string? storagePath,
        Guid itemId,
        CancellationToken ct,
        string context)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return true;
        }

        try
        {
            await fileStorageService.DeleteFileAsync(storagePath, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to delete file {StoragePath} for item {ItemId} during {Context}.",
                storagePath,
                itemId,
                context);
            return false;
        }
    }
}
