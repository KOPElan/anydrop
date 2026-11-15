using System.Text.Json;
using AnyDrop.App.Models;

namespace AnyDrop.App.Infrastructure;

/// <summary>
/// 待处理的外部分享内容 + 通知深链接。
/// S2-06：分享内容持久化到磁盘（JSON 文件），不再是静态内存字段——
/// 进程被杀重启后仍能恢复；通知 topicId 仍是内存态（一次性的导航状态）。
/// </summary>
public interface IPendingShareStore
{
    SharedContent? Content { get; }
    bool HasContent { get; }

    /// <summary>通知点击携带的 topicId，用于深链接跳转（内存态，不落盘）。</summary>
    Guid? NotificationTopicId { get; set; }

    /// <summary>保存待分享内容（落盘）。</summary>
    Task SaveAsync(SharedContent content, CancellationToken ct = default);

    /// <summary>
    /// 清除待分享内容：删除 JSON 文件，并删除其中列出的、位于缓存目录内的已复制文件。
    /// 缓存目录外的路径绝不删除（防御性：只删接收时自己复制进缓存的文件）。
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>S2-06：<see cref="IPendingShareStore"/> 的文件实现。</summary>
public sealed class FilePendingShareStore : IPendingShareStore
{
    private const string FileName = "pending-share.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _filePath;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SharedContent? _content;

    /// <param name="appDataDirectory">pending-share.json 存放目录（MAUI 侧传 FileSystem.AppDataDirectory）。</param>
    /// <param name="cacheDirectory">接收时复制文件的缓存目录（MAUI 侧传 FileSystem.CacheDirectory）。</param>
    public FilePendingShareStore(string appDataDirectory, string cacheDirectory)
    {
        _filePath = Path.Combine(appDataDirectory, FileName);
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _content = ReadFromDisk();
    }

    // 引用读取本身是原子的；写入在锁内完成，读取方拿到旧值或新值都是自洽的快照。
    public SharedContent? Content => _content;

    public bool HasContent => Content is not null;

    public Guid? NotificationTopicId { get; set; }

    public Task SaveAsync(SharedContent content, CancellationToken ct = default)
    {
        // 小文件同步写盘：Android MainActivity 在静态入口调用，需要写盘完成才返回，
        // 避免进程在异步写盘完成前被杀导致分享丢失。
        _lock.Wait(ct);
        try
        {
            _content = content;
            WriteToDisk(content);
        }
        finally
        {
            _lock.Release();
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try
        {
            var files = _content?.Files ?? [];
            _content = null;

            foreach (var file in files)
            {
                try
                {
                    var fullPath = Path.GetFullPath(file.Path);
                    if (fullPath.StartsWith(_cacheDirectory, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PendingShareStore] 删除缓存文件失败 {file.Path}: {ex.Message}");
                }
            }

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PendingShareStore] 删除待分享记录失败: {ex.Message}");
            }
        }
        finally
        {
            _lock.Release();
        }

        return Task.CompletedTask;
    }

    private SharedContent? ReadFromDisk()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<SharedContent>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            // 记录损坏不阻塞启动：视为空，旧文件下次写入时覆盖。
            System.Diagnostics.Debug.WriteLine($"[PendingShareStore] 读取待分享记录失败，视为空: {ex.Message}");
            return null;
        }
    }

    private void WriteToDisk(SharedContent content)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(content, JsonOptions));
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
