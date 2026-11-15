namespace AnyDrop.Models;

/// <summary>分片上传会话状态（S2-07，CLI-05）。</summary>
public enum ChunkedUploadStatus
{
    /// <summary>进行中：可继续传片、可完成、可取消。</summary>
    Active,

    /// <summary>已完成：已合并落库，返回已有 ShareItem（幂等重放）。</summary>
    Completed,

    /// <summary>已取消：分片已删除。</summary>
    Cancelled,

    /// <summary>已过期：超过 24 小时未完成，由对账清扫。</summary>
    Expired
}

/// <summary>
/// 分片上传会话（S2-07）。分片文件落磁盘临时目录
/// （<c>{Storage:BasePath}/uploads-tmp/{UploadId}/{index}.chunk</c>），
/// 会话与已确认分片清单落库，服务端重启后仍可断点续传。
/// 幂等键在同一用户内唯一：同一键重复初始化返回同一会话。
/// </summary>
public sealed class ChunkedUpload
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>客户端幂等键（由文件名 + 大小 + 修改时间派生，见客户端）。</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string MimeType { get; set; } = "application/octet-stream";

    public long TotalSize { get; set; }

    /// <summary>客户端在初始化时提供的整文件 SHA-256（可空；完成时提交的为准）。</summary>
    public string? TotalSha256 { get; set; }

    /// <summary>分片大小（字节），初始化时确定，服务端返回给客户端。</summary>
    public int ChunkSize { get; set; }

    public Guid? TopicId { get; set; }

    public bool BurnAfterReading { get; set; }

    public ChunkedUploadStatus Status { get; set; } = ChunkedUploadStatus.Active;

    /// <summary>完成时创建的 ShareItem；重复完成直接返回该条（整流幂等）。</summary>
    public Guid? ShareItemId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>已确认收到的单个分片（S2-07）。复合主键（UploadId, ChunkIndex）。</summary>
public sealed class ChunkedUploadChunk
{
    public Guid UploadId { get; set; }

    public int ChunkIndex { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}
