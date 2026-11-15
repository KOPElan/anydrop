using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

/// <summary>
/// 大文件流式上传，不缓冲到 MemoryStream。
/// S2-07：小于 20MB 走单次 multipart；达到 20MB 走分片上传（断点续传）。
/// </summary>
public interface IFileUploadService
{
    Task<ShareItemDto> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid topicId,
        bool burnAfterReading = false,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
