namespace AnyDrop.Services;

public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default);

    Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default);

    Task DeleteFileAsync(string storagePath, CancellationToken ct = default);
}
