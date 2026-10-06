namespace AnyDrop.Services;

public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default);

    /// <summary>
    /// 将文件保存到指定的存储相对路径（而非自动生成路径）。
    /// </summary>
    Task<string> SaveFileAtPathAsync(Stream content, string storagePath, string mimeType, CancellationToken ct = default);

    Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default);

    Task DeleteFileAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// 存储卷是否有足够剩余空间写入 <paramref name="requiredBytes"/>。
    ///
    /// 磁盘写满会让 SQLite 写入与文件落盘一起失败，是自托管实例最常见的整体故障，
    /// 因此在接收上传前先做一次可负担性判断。
    /// 无法确定剩余空间时返回 <see langword="true"/>，避免误伤正常上传。
    /// </summary>
    bool HasFreeSpace(long requiredBytes);
}
