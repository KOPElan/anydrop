using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

/// <summary>大文件流式上传，不缓冲到 MemoryStream。</summary>
public interface IFileUploadService
{
    Task<ShareItemDto> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid topicId,
        IProgress<double>? progress = null);
}
