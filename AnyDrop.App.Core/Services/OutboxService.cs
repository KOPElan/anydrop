using System.Text.Json;

namespace AnyDrop.App.Services;

/// <summary>离线发件箱里的一条待发送文本消息（S2-05）。</summary>
public sealed record OutboxMessage(
    Guid Id,
    Guid TopicId,
    string Content,
    bool BurnAfterReading,
    DateTimeOffset CreatedAt);

/// <summary>
/// S2-05：持久化离线发件箱。断网时发送失败的文本消息写盘保存（JSON 文件），
/// 网络恢复或 App 回到前台时按入箱顺序补发；补发成功即删除，失败则保留并停止本轮补发。
/// 文件消息不在此列（文件内容无法仅靠文本重发，仍需用户重新选择上传）。
/// </summary>
public interface IOutboxService
{
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(CancellationToken ct = default);
    Task EnqueueAsync(Guid topicId, string content, bool burnAfterReading, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 按入箱顺序逐条补发；<paramref name="send"/> 成功后删除该条，
    /// 失败（抛异常）即停止本轮，剩余条目保留待下次触发。
    /// 返回本轮成功发出的条数。
    /// </summary>
    Task<int> DrainAsync(Func<OutboxMessage, Task> send, CancellationToken ct = default);
}

public sealed class OutboxService : IOutboxService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <param name="appDataDirectory">发件箱文件存放目录（MAUI 侧传 FileSystem.AppDataDirectory）。</param>
    public OutboxService(string appDataDirectory)
    {
        _filePath = Path.Combine(appDataDirectory, "outbox.json");
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadAllLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EnqueueAsync(Guid topicId, string content, bool burnAfterReading, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAllLockedAsync().ConfigureAwait(false);
            all.Add(new OutboxMessage(Guid.NewGuid(), topicId, content, burnAfterReading, DateTimeOffset.UtcNow));
            await WriteAllLockedAsync(all).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = await ReadAllLockedAsync().ConfigureAwait(false);
            if (all.RemoveAll(m => m.Id == id) > 0)
            {
                await WriteAllLockedAsync(all).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> DrainAsync(Func<OutboxMessage, Task> send, CancellationToken ct = default)
    {
        // 先快照后释放锁：补发是网络调用，不能在持锁期间做，否则 UI 侧的入箱会被整轮补发堵住。
        List<OutboxMessage> pending;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            pending = (await ReadAllLockedAsync().ConfigureAwait(false))
                .OrderBy(m => m.CreatedAt)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }

        var sent = 0;
        foreach (var message in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await send(message).ConfigureAwait(false);
            }
            catch
            {
                // 本条失败即停止本轮，剩余条目保留待下次触发。
                break;
            }

            await RemoveAsync(message.Id, ct).ConfigureAwait(false);
            sent++;
        }

        return sent;
    }

    private async Task<List<OutboxMessage>> ReadAllLockedAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<OutboxMessage>>(stream, JsonOptions)
                .ConfigureAwait(false) ?? [];
        }
        catch (Exception ex)
        {
            // 发件箱文件损坏：不阻塞主流程，视为空箱；下次写入会覆盖。
            System.Diagnostics.Debug.WriteLine($"[OutboxService] 读取发件箱失败，视为空箱: {ex.Message}");
            return [];
        }
    }

    private async Task WriteAllLockedAsync(List<OutboxMessage> messages)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, messages, JsonOptions).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
