namespace AnyDrop.Models;

/// <summary>
/// 待删除文件队列（S0-05，DAT-01）。
///
/// 手动删除时数据库行先提交，随后删文件；若文件删除失败（例如权限或占用），
/// 把路径记在这里而不是只写一条警告日志——否则失败会被遗忘，磁盘上永久残留。
/// 队列的消费（重试删除 / 到期清理）落在 Stage 1，本阶段只建表 + 入队。
/// </summary>
public sealed class PendingFileDeletion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>原消息 ID（行已删，仅用于追溯；可为空）。</summary>
    public Guid? ShareItemId { get; set; }

    /// <summary>待删除的存储相对路径。</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>失败原因（异常消息或上下文，如 manual delete）。</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>累计重试次数（Stage 1 消费时递增）。</summary>
    public int RetryCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
