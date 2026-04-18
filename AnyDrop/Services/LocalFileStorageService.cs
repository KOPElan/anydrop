namespace AnyDrop.Services;

public sealed class LocalFileStorageService : IFileStorageService
{
    public Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default)
        => throw new NotImplementedException("File storage not implemented in MVP");

    public Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default)
        => throw new NotImplementedException("File storage not implemented in MVP");

    public Task DeleteFileAsync(string storagePath, CancellationToken ct = default)
        => throw new NotImplementedException("File storage not implemented in MVP");
}
