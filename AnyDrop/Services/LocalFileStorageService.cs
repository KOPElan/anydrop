namespace AnyDrop.Services;

public sealed class LocalFileStorageService(
    IConfiguration configuration,
    TimeProvider timeProvider) : IFileStorageService
{
    private readonly string _basePath = Path.GetFullPath(configuration["Storage:BasePath"] ?? "data/files");

    /// <summary>
    /// 写入时需要预留的安全余量。除了文件本身，还要求额外的 64 MB：
    /// 磁盘被写满会让 SQLite 写入一并失败，导致整个实例不可用。
    /// </summary>
    public const long DefaultReserveBytes = 64L * 1024 * 1024;

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
        var safeName = $"{timeProvider.GetUtcNow():yyyyMMdd}/{Guid.NewGuid():N}{extension}";

        await WriteAtomicallyAsync(safeName, content, ct);

        return safeName.Replace('\\', '/');
    }

    public async Task<string> SaveFileAtPathAsync(Stream content, string storagePath, string mimeType, CancellationToken ct = default)
    {
        var safePath = storagePath.Replace('\\', '/').TrimStart('/');

        await WriteAtomicallyAsync(safePath, content, ct);

        return safePath;
    }

    /// <summary>
    /// 写入同目录下的临时文件，再原子改名到目标路径。
    ///
    /// 直接写目标路径的问题是：进程崩溃、容器被 kill、客户端中途断开，都会在最终位置
    /// 留下一个长度不完整、却能被正常读取的半截文件。临时文件与目标同目录，
    /// 因此 <see cref="File.Move(string, string, bool)"/> 是同卷改名（原子，无拷贝开销）。
    /// </summary>
    private async Task WriteAtomicallyAsync(string storagePath, Stream content, CancellationToken ct)
    {
        var finalPath = GetFullPath(storagePath);
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Guid.NewGuid():N}.part");

        try
        {
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(output, ct);
            }

            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            // 失败时清理半截临时文件，不留垃圾
            TryDeleteQuietly(tempPath);
            throw;
        }
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败不应掩盖原始异常
        }
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

    public bool HasFreeSpace(long requiredBytes)
    {
        if (requiredBytes < 0)
        {
            return true;
        }

        var available = GetAvailableFreeSpace();
        if (available == long.MaxValue)
        {
            return true;
        }

        // 用减法而非 requiredBytes + 余量，避免 requiredBytes 接近 long.MaxValue 时相加溢出
        return available - DefaultReserveBytes >= requiredBytes;
    }

    /// <summary>存储卷剩余可用字节数；无法确定时返回 <see cref="long.MaxValue"/>。</summary>
    internal long GetAvailableFreeSpace()
    {
        try
        {
            var root = Path.GetPathRoot(_basePath);
            if (string.IsNullOrEmpty(root))
            {
                return long.MaxValue;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception)
        {
            // 平台差异或路径异常时不做拦截，避免误伤正常上传
            return long.MaxValue;
        }
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
