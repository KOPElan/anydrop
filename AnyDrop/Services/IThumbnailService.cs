namespace AnyDrop.Services;

/// <summary>
/// 缩略图生成服务：负责为图片和视频生成 JPEG 预览图，并持久化存储路径。
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// 为指定 ShareItem（图片或视频）生成缩略图，并将存储路径写回数据库。
    /// 若 ffmpeg 不可用或生成失败，不抛异常，仅记录日志并返回 null。
    /// </summary>
    /// <param name="itemId">ShareItem 的 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>生成的缩略图存储相对路径，失败时为 null。</returns>
    Task<string?> GenerateThumbnailAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// 批量处理所有尚未生成缩略图的图片/视频条目。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功生成缩略图的条目数。</returns>
    Task<int> ProcessPendingThumbnailsAsync(CancellationToken ct = default);
}
