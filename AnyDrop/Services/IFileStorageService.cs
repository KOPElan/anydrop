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
}
