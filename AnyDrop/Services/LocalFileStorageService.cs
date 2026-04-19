namespace AnyDrop.Services;

public sealed class LocalFileStorageService(IConfiguration configuration) : IFileStorageService
{
    private readonly string _basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");

    public async Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        var safeName = $"{DateTimeOffset.UtcNow:yyyyMMdd}/{Guid.NewGuid():N}{extension}";
        var fullPath = GetFullPath(safeName);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(output, ct);
        return safeName.Replace('\\', '/');
    }

    public Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(storagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File not found.", storagePath);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteFileAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string GetFullPath(string storagePath)
    {
        var relative = storagePath.Replace('\\', '/').TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(_basePath, relative));

        // 确保 _basePath 以目录分隔符结尾，防止前缀匹配绕过
        // 例如 basePath=/data/files 时，/data/files_evil/x 不应通过
        var baseWithSep = _basePath.EndsWith(Path.DirectorySeparatorChar)
            ? _basePath
            : _basePath + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!combined.StartsWith(baseWithSep, comparison))
        {
            throw new InvalidOperationException("Invalid storage path.");
        }

        return combined;
    }
}
