namespace AnyDrop.Services;

public sealed class LocalFileStorageService(IConfiguration configuration) : IFileStorageService
{
    private readonly string _basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");

    /// <summary>
    /// 允许的扩展名形态：一个点加上 1..10 个字母或数字。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex SafeExtensionPattern =
        new(@"^\.[A-Za-z0-9]{1,10}$",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(200));

    public async Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default)
    {
        var extension = GetSafeExtension(fileName);
        var safeName = $"{DateTimeOffset.UtcNow:yyyyMMdd}/{Guid.NewGuid():N}{extension}";
        var fullPath = GetFullPath(safeName);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(output, ct);
        return safeName.Replace('\\', '/');
    }

    /// <summary>
    /// 从上传文件名中提取扩展名，并限制为安全字符集。
    ///
    /// 不能直接采用 <see cref="Path.GetExtension(string)"/> 的结果：它只是「最后一个点之后的部分」，
    /// 不做任何字符校验。例如文件名 <c>clip." -y evil</c> 会得到扩展名 <c>." -y evil</c>；
    /// 该值会拼进存储路径，进而进入缩略图生成的 ffmpeg 命令行，足以闭合引号注入任意参数
    /// （Linux 上双引号是合法文件名字符，而 Linux 正是 Docker 部署的主要目标平台）。
    /// 不符合白名单的扩展名一律丢弃。
    /// </summary>
    internal static string GetSafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return SafeExtensionPattern.IsMatch(extension) ? extension : string.Empty;
    }

    public async Task<string> SaveFileAtPathAsync(Stream content, string storagePath, string mimeType, CancellationToken ct = default)
    {
        var safePath = storagePath.Replace('\\', '/').TrimStart('/');
        var fullPath = GetFullPath(safePath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(output, ct);
        return safePath;
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
