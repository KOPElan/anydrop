using AnyDrop.Models;

namespace AnyDrop.Services;

public interface IShareService
{
    Task<ShareItemDto> SendTextAsync(string content, Guid? topicId = null, CancellationToken ct = default);

    /// <param name="knownFileSize">
    /// 已知的文件大小（字节），用于流不支持 Seek 时（如 JS 拖放流）仍能正确记录文件大小。
    /// </param>
    Task<ShareItemDto> SendFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid? topicId = null,
        long? knownFileSize = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default);
}
