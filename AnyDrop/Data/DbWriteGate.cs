namespace AnyDrop.Data;

/// <summary>
/// S0-04（DAT-04）：后台写操作的进程内串行化。
///
/// SQLite 是单写者模型；两个后台定时任务（过期清理每分钟、缩略图批处理）
/// 若同时写库，会互相争写锁。请求侧的写入（上传等）不经过这里，
/// 它们靠 WAL + busy_timeout + 有限重试兜底——只把「便宜」的后台写串起来，
/// 不给请求路径加全局锁。
/// </summary>
public sealed class DbWriteGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
