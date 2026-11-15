using System.Security.Cryptography;
using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AnyDrop.Services;

/// <summary>分片上传的业务异常：携带 HTTP 状态码，端点层直接映射为响应。</summary>
public sealed class ChunkedUploadException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// 分片上传服务端（S2-07，CLI-05）：三步协议——初始化（幂等键）→ 按 uploadId + Content-Range 传片
/// → 完成时合并、校验总大小与总哈希，通过才落盘落库。分片文件在磁盘临时目录，
/// 会话与已确认分片清单落库，服务端重启后仍可断点续传。
/// </summary>
public sealed class ChunkedUploadService(
    IDbContextFactory<AnyDropDbContext> dbContextFactory,
    IFileStorageService fileStorageService,
    IConfiguration configuration,
    IShareService shareService,
    TimeProvider timeProvider)
{
    private string TmpRoot => Path.GetFullPath(
        Path.Combine(configuration["Storage:BasePath"] ?? "data/files", "uploads-tmp"));

    private string SessionDir(Guid uploadId) => Path.Combine(TmpRoot, uploadId.ToString("N"));

    private string ChunkPath(Guid uploadId, int chunkIndex) =>
        Path.Combine(SessionDir(uploadId), $"{chunkIndex}.chunk");

    /// <summary>按总大小与分片大小算分片数（最后一片可能不满）。</summary>
    internal static int ExpectedChunkCount(long totalSize, int chunkSize) =>
        (int)((totalSize + chunkSize - 1) / chunkSize);

    /// <summary>
    /// 初始化分片会话。同一用户同一幂等键重复初始化返回同一会话（与已确认分片清单）；
    /// 文件名或总大小不一致返回 409；已取消/已过期的旧会话先删后重建。
    /// </summary>
    public async Task<(InitUploadResponse Response, bool Created)> InitAsync(
        Guid userId, InitUploadRequest request, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(request.FileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest, "文件名不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
        {
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest, "幂等键不能为空且不超过 128 字符。");
        }

        if (request.TotalSize <= 0)
        {
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest, "文件大小必须大于 0。");
        }

        if (request.TotalSize > UploadLimits.MaxFileSizeBytes)
        {
            throw new ChunkedUploadException(StatusCodes.Status413PayloadTooLarge,
                $"文件大小（{request.TotalSize:N0} 字节）超出上传上限（{UploadLimits.MaxFileSizeBytes:N0} 字节）。");
        }

        if (!string.IsNullOrWhiteSpace(request.TotalSha256) && NormalizeHash(request.TotalSha256) is null)
        {
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest, "TotalSha256 必须是 64 位小写十六进制 SHA-256。");
        }

        if (!fileStorageService.HasFreeSpace(request.TotalSize))
        {
            throw new ChunkedUploadException(StatusCodes.Status507InsufficientStorage, "服务器剩余存储空间不足，已拒绝本次上传。");
        }

        var now = timeProvider.GetUtcNow();
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        if (request.TopicId.HasValue)
        {
            var topicExists = await db.Topics.AnyAsync(t => t.Id == request.TopicId.Value, ct);
            if (!topicExists)
            {
                throw new ChunkedUploadException(StatusCodes.Status400BadRequest, "目标主题不存在。");
            }
        }

        var existing = await db.ChunkedUploads
            .FirstOrDefaultAsync(u => u.UserId == userId && u.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.Status is ChunkedUploadStatus.Cancelled or ChunkedUploadStatus.Expired
                || (existing.Status == ChunkedUploadStatus.Active && existing.ExpiresAt <= now))
            {
                // 旧会话已终结：删行删文件，腾出幂等键后重建。
                db.ChunkedUploads.Remove(existing);
                await db.SaveChangesWithRetryAsync(ct);
                DeleteSessionDir(existing.Id);
                existing = null;
            }
            else
            {
                if (!string.Equals(existing.FileName, fileName, StringComparison.Ordinal)
                    || existing.TotalSize != request.TotalSize)
                {
                    throw new ChunkedUploadException(StatusCodes.Status409Conflict,
                        "同一幂等键对应不同文件（文件名或大小不一致），请换用新的幂等键。");
                }

                var confirmed = await GetConfirmedChunksAsync(db, existing.Id, ct);
                return (new InitUploadResponse(existing.Id, existing.ChunkSize, confirmed, existing.ExpiresAt), false);
            }
        }

        var session = new ChunkedUpload
        {
            UserId = userId,
            IdempotencyKey = request.IdempotencyKey,
            FileName = fileName,
            MimeType = MimeTypeHelper.Normalize(request.MimeType ?? "application/octet-stream"),
            TotalSize = request.TotalSize,
            TotalSha256 = NormalizeHash(request.TotalSha256),
            ChunkSize = UploadLimits.DefaultChunkSizeBytes,
            TopicId = request.TopicId,
            BurnAfterReading = request.BurnAfterReading,
            Status = ChunkedUploadStatus.Active,
            CreatedAt = now,
            ExpiresAt = now + UploadLimits.SessionLifetime
        };

        db.ChunkedUploads.Add(session);
        try
        {
            await db.SaveChangesWithRetryAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 并发重复初始化撞唯一索引：读回已建好的会话返回（幂等）。
            await using var retryDb = await dbContextFactory.CreateDbContextAsync(ct);
            var raced = await retryDb.ChunkedUploads
                .FirstOrDefaultAsync(u => u.UserId == userId && u.IdempotencyKey == request.IdempotencyKey, ct);
            if (raced is null)
            {
                throw;
            }

            var confirmed = await GetConfirmedChunksAsync(retryDb, raced.Id, ct);
            return (new InitUploadResponse(raced.Id, raced.ChunkSize, confirmed, raced.ExpiresAt), false);
        }

        return (new InitUploadResponse(session.Id, session.ChunkSize, [], session.ExpiresAt), true);
    }

    /// <summary>查询会话状态（断点续传用）。非本人会话返回 null（按不存在处理）。</summary>
    public async Task<UploadStatusResponse?> GetStatusAsync(Guid userId, Guid uploadId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var session = await db.ChunkedUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.UserId == userId, ct);
        if (session is null)
        {
            return null;
        }

        var confirmed = await GetConfirmedChunksAsync(db, session.Id, ct);
        return new UploadStatusResponse(
            session.Id, session.FileName, session.TotalSize, session.ChunkSize,
            confirmed, session.Status.ToString(), session.ExpiresAt);
    }

    /// <summary>
    /// 上传单个分片。请求头 Content-Range 必须为 <c>bytes {start}-{end}/{total}</c>，
    /// 且与分片序号对应的字节范围一致；重复提交同一分片直接返回已确认，不重复落盘。
    /// </summary>
    public async Task<ChunkUploadResponse> UploadChunkAsync(
        Guid userId, Guid uploadId, int chunkIndex, string? contentRange, Stream body,
        CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var session = await db.ChunkedUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.UserId == userId, ct)
            ?? throw new ChunkedUploadException(StatusCodes.Status404NotFound, "上传会话不存在。");

        if (session.Status != ChunkedUploadStatus.Active)
        {
            throw new ChunkedUploadException(StatusCodes.Status409Conflict, $"上传会话已{DescribeStatus(session.Status)}，不能再传片。");
        }

        if (session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            throw new ChunkedUploadException(StatusCodes.Status410Gone, "上传会话已过期，请重新初始化。");
        }

        var expectedCount = ExpectedChunkCount(session.TotalSize, session.ChunkSize);
        if (chunkIndex < 0 || chunkIndex >= expectedCount)
        {
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest,
                $"分片序号超出范围：应为 0..{expectedCount - 1}。");
        }

        var expectedStart = (long)chunkIndex * session.ChunkSize;
        var expectedEnd = Math.Min(expectedStart + session.ChunkSize, session.TotalSize) - 1;
        var expectedLength = expectedEnd - expectedStart + 1;

        if (!TryParseContentRange(contentRange, out var start, out var end, out var total)
            || start != expectedStart || end != expectedEnd || total != session.TotalSize)
        {
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest,
                $"Content-Range 必须为 bytes {expectedStart}-{expectedEnd}/{session.TotalSize}。");
        }

        var chunkPath = ChunkPath(uploadId, chunkIndex);
        var existingRow = await db.ChunkedUploadChunks.FindAsync([uploadId, chunkIndex], ct);
        if (existingRow is not null && File.Exists(chunkPath)
            && new FileInfo(chunkPath).Length == expectedLength)
        {
            // 重复分片：直接返回已确认，不重复落盘。
            var confirmed = await GetConfirmedChunksAsync(db, uploadId, ct);
            return new ChunkUploadResponse(chunkIndex, expectedLength, confirmed);
        }

        Directory.CreateDirectory(SessionDir(uploadId));
        var partPath = chunkPath + ".part";
        await using (var fileStream = File.Create(partPath))
        {
            await body.CopyToAsync(fileStream, ct);
        }

        var writtenLength = new FileInfo(partPath).Length;
        if (writtenLength != expectedLength)
        {
            File.Delete(partPath);
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest,
                $"分片大小不符：应为 {expectedLength} 字节，实际收到 {writtenLength} 字节。");
        }

        File.Move(partPath, chunkPath, overwrite: true);

        db.ChunkedUploadChunks.Add(new ChunkedUploadChunk
        {
            UploadId = uploadId,
            ChunkIndex = chunkIndex,
            SizeBytes = expectedLength,
            ReceivedAt = timeProvider.GetUtcNow()
        });
        try
        {
            await db.SaveChangesWithRetryAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 并发重复提交撞复合主键：另一请求已确认本片，按已确认返回。
        }

        var confirmedChunks = await GetConfirmedChunksAsync(db, uploadId, ct);
        return new ChunkUploadResponse(chunkIndex, expectedLength, confirmedChunks);
    }

    /// <summary>
    /// 完成上传：分片齐全后按序合并，核对总大小与总哈希，通过才经
    /// <see cref="IShareService.SendFileAsync"/> 落盘落库（复用单次上传的
    /// 预占位行 + 缩略图 + 推送逻辑）。重复完成返回已创建的消息（整流幂等）。
    /// 哈希不一致时丢弃全部已确认分片（疑似某片损坏），客户端需重传。
    /// </summary>
    public async Task<ShareItemDto> CompleteAsync(
        Guid userId, Guid uploadId, string? totalSha256, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var session = await db.ChunkedUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.UserId == userId, ct)
            ?? throw new ChunkedUploadException(StatusCodes.Status404NotFound, "上传会话不存在。");

        if (session.Status == ChunkedUploadStatus.Completed && session.ShareItemId.HasValue)
        {
            var existingItem = await db.ShareItems
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.Id == session.ShareItemId.Value, ct);
            if (existingItem is not null)
            {
                return existingItem.ToDto();
            }

            throw new ChunkedUploadException(StatusCodes.Status409Conflict, "本次上传已完成，但对应消息已被删除。");
        }

        if (session.Status != ChunkedUploadStatus.Active)
        {
            throw new ChunkedUploadException(StatusCodes.Status409Conflict, $"上传会话已{DescribeStatus(session.Status)}，不能完成。");
        }

        if (session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            throw new ChunkedUploadException(StatusCodes.Status410Gone, "上传会话已过期，请重新初始化。");
        }

        var expectedCount = ExpectedChunkCount(session.TotalSize, session.ChunkSize);
        var confirmed = await GetConfirmedChunksAsync(db, uploadId, ct);
        if (confirmed.Count != expectedCount)
        {
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest,
                $"分片不全：已确认 {confirmed.Count}/{expectedCount} 片，请先补传缺失分片。");
        }

        foreach (var index in confirmed)
        {
            if (!File.Exists(ChunkPath(uploadId, index)))
            {
                throw new ChunkedUploadException(StatusCodes.Status400BadRequest,
                    $"分片 {index} 的文件已丢失，请重新上传该分片。");
            }
        }

        var mergedPath = Path.Combine(SessionDir(uploadId), "merged.tmp");
        await using (var merged = File.Create(mergedPath))
        {
            for (var index = 0; index < expectedCount; index++)
            {
                await using var chunk = File.OpenRead(ChunkPath(uploadId, index));
                await chunk.CopyToAsync(merged, ct);
            }
        }

        if (new FileInfo(mergedPath).Length != session.TotalSize)
        {
            File.Delete(mergedPath);
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest, "合并后文件大小与声明不符。");
        }

        var expectedHash = NormalizeHash(totalSha256) ?? session.TotalSha256;
        if (!string.IsNullOrWhiteSpace(totalSha256) && NormalizeHash(totalSha256) is null)
        {
            throw new ChunkedUploadException(StatusCodes.Status400BadRequest, "TotalSha256 必须是 64 位小写十六进制 SHA-256。");
        }
        if (!string.IsNullOrEmpty(expectedHash))
        {
            await using var mergedRead = File.OpenRead(mergedPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(mergedRead, ct)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                File.Delete(mergedPath);
                await DeleteChunkRowsAsync(db, uploadId, ct);
                DeleteSessionFiles(uploadId);
                throw new ChunkedUploadException(StatusCodes.Status400BadRequest,
                    "文件 SHA-256 校验失败：已确认分片已丢弃，请重新上传。");
            }
        }

        ShareItemDto dto;
        await using (var mergedRead = File.OpenRead(mergedPath))
        {
            dto = await shareService.SendFileAsync(
                mergedRead,
                session.FileName,
                session.MimeType,
                session.TopicId,
                knownFileSize: session.TotalSize,
                burnAfterReading: session.BurnAfterReading,
                ct);
        }

        session.Status = ChunkedUploadStatus.Completed;
        session.ShareItemId = dto.Id;
        await db.SaveChangesWithRetryAsync(ct);
        DeleteSessionDir(uploadId);
        return dto;
    }

    /// <summary>取消上传：删会话行与全部临时分片。已完成的会话不能取消。</summary>
    public async Task CancelAsync(Guid userId, Guid uploadId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var session = await db.ChunkedUploads
            .FirstOrDefaultAsync(u => u.Id == uploadId && u.UserId == userId, ct)
            ?? throw new ChunkedUploadException(StatusCodes.Status404NotFound, "上传会话不存在。");

        if (session.Status == ChunkedUploadStatus.Completed)
        {
            throw new ChunkedUploadException(StatusCodes.Status409Conflict, "上传已完成，不能取消。");
        }

        db.ChunkedUploads.Remove(session);
        await db.SaveChangesWithRetryAsync(ct);
        DeleteSessionDir(uploadId);
    }

    /// <summary>
    /// 清扫过期未完成的会话（S1-01 孤儿治理的一部分）：删行删临时分片。
    /// 由 <see cref="OrphanFileReconciler"/> 每轮调用；初始化时不顺带清扫，
    /// 避免上传请求被不相关的清扫拖慢。
    /// </summary>
    public async Task<int> SweepExpiredAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow();
        var expired = await db.ChunkedUploads
            .Where(u => u.Status == ChunkedUploadStatus.Active && u.ExpiresAt <= now)
            .ToListAsync(ct);
        if (expired.Count == 0)
        {
            return 0;
        }

        db.ChunkedUploads.RemoveRange(expired);
        await db.SaveChangesWithRetryAsync(ct);
        foreach (var session in expired)
        {
            DeleteSessionDir(session.Id);
        }

        return expired.Count;
    }

    private async Task<IReadOnlyList<int>> GetConfirmedChunksAsync(AnyDropDbContext db, Guid uploadId, CancellationToken ct)
    {
        var indices = await db.ChunkedUploadChunks
            .AsNoTracking()
            .Where(c => c.UploadId == uploadId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.ChunkIndex)
            .ToListAsync(ct);
        return indices;
    }

    private async Task DeleteChunkRowsAsync(AnyDropDbContext db, Guid uploadId, CancellationToken ct)
    {
        var rows = await db.ChunkedUploadChunks.Where(c => c.UploadId == uploadId).ToListAsync(ct);
        db.ChunkedUploadChunks.RemoveRange(rows);
        await db.SaveChangesWithRetryAsync(ct);
    }

    private void DeleteSessionDir(Guid uploadId)
    {
        try
        {
            var dir = SessionDir(uploadId);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChunkedUploadService] 删除会话临时目录失败 {uploadId}: {ex.Message}");
        }
    }

    private void DeleteSessionFiles(Guid uploadId)
    {
        // 只删分片文件（.chunk），保留目录本身；调用方随后可能重建会话。
        try
        {
            var dir = SessionDir(uploadId);
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.chunk"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChunkedUploadService] 删除会话分片文件失败 {uploadId}: {ex.Message}");
        }
    }

    private static string DescribeStatus(ChunkedUploadStatus status) => status switch
    {
        ChunkedUploadStatus.Completed => "完成",
        ChunkedUploadStatus.Cancelled => "取消",
        ChunkedUploadStatus.Expired => "过期",
        _ => "未知状态"
    };

    private static bool TryParseContentRange(string? header, out long start, out long end, out long total)
    {
        start = end = total = 0;
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        // bytes 0-4194303/524288000
        var value = header.Trim();
        if (!value.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rangeAndTotal = value["bytes ".Length..].Split('/');
        if (rangeAndTotal.Length != 2)
        {
            return false;
        }

        var range = rangeAndTotal[0].Split('-');
        return range.Length == 2
            && long.TryParse(range[0], out start)
            && long.TryParse(range[1], out end)
            && long.TryParse(rangeAndTotal[1], out total)
            && start >= 0 && end >= start && total > 0;
    }

    private static string? NormalizeHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        var normalized = hash.Trim().ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))
            ? normalized
            : null;
    }
}
