using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Services;

/// <summary>
/// S1-03（DAT-09）：用 <see cref="IDbContextFactory{TContext}"/>、
/// 每次调用建短寿命上下文，不再随 Blazor 电路持有同一上下文数小时。
/// 同一方法内「读后改」共用一个上下文；私有辅助方法各建自己的上下文，
/// 调用时调用方的写操作已提交，因此读到的是已提交状态。
/// </summary>
public sealed class ShareService(
    IDbContextFactory<AnyDropDbContext> dbContextFactory,
    IHubContext<ShareHub> hubContext,
    ITopicService topicService,
    IFileStorageService fileStorageService,
    IThumbnailService thumbnailService,
    LinkMetadataService linkMetadataService,
    ISystemSettingsService systemSettingsService,
    TimeProvider timeProvider,
    ILogger<ShareService> logger) : IShareService
{
    public async Task<ShareItemDto> SendTextAsync(string content, Guid? topicId = null, bool burnAfterReading = false, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var normalizedContent = content.Trim();

        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            throw new ArgumentException("Content is required", nameof(content));
        }

        if (normalizedContent.Length > 10_000)
        {
            throw new ArgumentException("Content length must be less than or equal to 10000 characters", nameof(content));
        }

        Topic? topic = null;
        if (topicId.HasValue)
        {
            topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId.Value, ct);
            if (topic is null)
            {
                throw new ArgumentException("Topic does not exist.", nameof(topicId));
            }
        }

        var isLink = IsLink(normalizedContent);
        var now = timeProvider.GetUtcNow();
        var burnMinutes = burnAfterReading ? await systemSettingsService.GetBurnAfterReadingMinutesAsync(ct) : 0;
        var item = new ShareItem
        {
            ContentType = isLink ? ShareContentType.Link : ShareContentType.Text,
            Content = normalizedContent,
            CreatedAt = now,
            ExpiresAt = burnAfterReading ? now.AddMinutes(burnMinutes) : null,
            TopicId = topicId
        };

        if (topic is not null)
        {
            topic.LastMessageAt = now;
            topic.LastMessagePreview = BuildPreview(normalizedContent);
        }

        dbContext.ShareItems.Add(item);
        await dbContext.SaveChangesWithRetryAsync(ct);

        // 先广播消息（让用户立即看到），再后台补全链接元数据
        var dto = item.ToDto();
        await hubContext.Clients.All.SendAsync("ReceiveShareItem", dto, CancellationToken.None);

        if (topic is not null)
        {
            var topics = await topicService.GetAllTopicsAsync(ct);
            await hubContext.Clients.All.SendAsync("TopicsUpdated", topics, CancellationToken.None);
        }

        // 异步抓取 OGP 元数据并更新，不阻塞本次返回
        if (isLink && await systemSettingsService.IsAutoFetchLinkPreviewEnabledAsync(ct))
        {
            _ = FetchAndUpdateLinkMetadataAsync(item.Id, normalizedContent);
        }

        return dto;
    }

    /// <summary>
    /// 后台抓取链接元数据，更新数据库并推送更新后的 DTO 给所有客户端。
    /// 此方法独立于请求生命周期运行，通过独立 DI Scope 操作 DbContext，失败时静默忽略。
    /// </summary>
    private async Task FetchAndUpdateLinkMetadataAsync(Guid itemId, string url)
    {
        try
        {
            var (title, description) = await linkMetadataService.FetchAsync(url);

            if (title is null && description is null)
            {
                return;
            }

            // 工厂建短寿命上下文：后台任务独立于请求生命周期，不能复用
            // 已被请求 Scope 释放的 DbContext。
            await using var db = await dbContextFactory.CreateDbContextAsync();

            var item = await db.ShareItems.FindAsync(itemId);
            if (item is null)
            {
                return;
            }

            item.LinkTitle = title;
            item.LinkDescription = description;
            await db.SaveChangesWithRetryAsync();

            // 推送补全后的消息，让客户端更新气泡内容
            await hubContext.Clients.All.SendAsync("ReceiveShareItem", item.ToDto());
        }
        catch (Exception)
        {
            // 后台任务失败不影响主流程
        }
    }

    public async Task<ShareItemDto> SendFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid? topicId = null,
        long? knownFileSize = null,
        bool burnAfterReading = false,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        if (fileStream is null)
        {
            throw new ArgumentNullException(nameof(fileStream));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(mimeType))
        {
            mimeType = "application/octet-stream";
        }

        var contentType = ResolveContentType(mimeType);

        // 先校验主题、再落盘文件。
        // 反过来（先落盘）时，主题不存在会抛出 ArgumentException，端点转换成 400 返回，
        // 但磁盘上的文件已经写入且永远不会被任何 ShareItem 引用，成为永久孤儿。
        Topic? topic = null;
        if (topicId.HasValue)
        {
            topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId.Value, ct);
            if (topic is null)
            {
                throw new ArgumentException("Topic does not exist.", nameof(topicId));
            }
        }

        var storagePath = await fileStorageService.SaveFileAsync(fileStream, fileName, mimeType, ct);

        // 优先使用调用方传入的已知大小，其次尝试从流读取（仅当流支持 Seek）
        var fileSize = knownFileSize ?? (fileStream.CanSeek ? fileStream.Length : null);

        var now = timeProvider.GetUtcNow();
        var burnMinutes = burnAfterReading ? await systemSettingsService.GetBurnAfterReadingMinutesAsync(ct) : 0;
        var item = new ShareItem
        {
            ContentType = contentType,
            Content = storagePath,
            FileName = fileName,
            FileSize = fileSize,
            MimeType = mimeType,
            CreatedAt = now,
            ExpiresAt = burnAfterReading ? now.AddMinutes(burnMinutes) : null,
            TopicId = topicId
        };

        if (topic is not null)
        {
            topic.LastMessageAt = now;
            topic.LastMessagePreview = BuildPreview(fileName);
        }

        dbContext.ShareItems.Add(item);
        try
        {
            await dbContext.SaveChangesWithRetryAsync(ct);
        }
        catch
        {
            // 数据库写入失败时回收已落盘的文件，否则磁盘上会留下无人引用的孤儿文件
            await ShareItemFileCleanup.TryDeleteAsync(
                fileStorageService, logger, storagePath, item.Id, ct, "file upload rollback");
            throw;
        }

        var shouldGenerateThumbnailInBackground = false;
        if (contentType is ShareContentType.Image or ShareContentType.Video)
        {
            var scheduledGenerationEnabled = await systemSettingsService.IsScheduledThumbnailGenerationEnabledAsync(ct);
            if (!scheduledGenerationEnabled)
            {
                shouldGenerateThumbnailInBackground = true;
            }
        }

        var dto = item.ToDto();
        await hubContext.Clients.All.SendAsync("ReceiveShareItem", dto, CancellationToken.None);

        if (topic is not null)
        {
            var topics = await topicService.GetAllTopicsAsync(ct);
            await hubContext.Clients.All.SendAsync("TopicsUpdated", topics, CancellationToken.None);
        }

        if (shouldGenerateThumbnailInBackground)
        {
            _ = GenerateThumbnailInBackgroundAsync(item.Id);
        }

        return dto;
    }

    private async Task GenerateThumbnailInBackgroundAsync(Guid itemId)
    {
        try
        {
            await thumbnailService.GenerateThumbnailAsync(itemId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background thumbnail generation failed for item {ItemId}", itemId);
        }
    }

    public async Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var normalizedCount = count <= 0 ? 50 : count;
        var safeCount = Math.Clamp(normalizedCount, 1, 200);

        // S0-06：已到期的定时销毁消息立即不可见，不等清理任务。
        return await dbContext.ShareItems
            .AsNoTracking()
            .NotExpired(timeProvider.GetUtcNow())
            .OrderByDescending(x => x.CreatedAt)
            .Take(safeCount)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
    }

    public async Task<TopicMessagesResponse> SearchTopicMessagesAsync(
        Guid topicId,
        string query,
        int limit = 50,
        DateTimeOffset? before = null,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var normalized = query.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new TopicMessagesResponse([], false, null);
        }

        var exists = await dbContext.Topics.AnyAsync(t => t.Id == topicId, ct);
        if (!exists)
        {
            return new TopicMessagesResponse([], false, null);
        }

        var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);

        // 关键词在 .NET 侧用不变文化小写化：区域性规则（如土耳其语的 I/i）会让 ToLower
        // 产生与预期不同的结果。注意列本身必须用 ToLower()——EF Core 的 SQLite provider
        // 不支持翻译 ToLowerInvariant()，在查询里使用它会在运行时抛「could not be translated」。
        var normalizedSearch = normalized.ToLowerInvariant();

        // 使用显式的大小写不敏感匹配，避免 SQLite 在未配置 NOCASE 时出现大小写行为与需求不一致。
        // 同时转义 LIKE 通配符，尽量保持原先 Contains 的“字面子串匹配”语义。
        var escapedSearch = normalizedSearch
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        var likePattern = $"%{escapedSearch}%";

        // 正文、文件名、链接标题与描述都要参与匹配。
        // 此前只匹配 Content，而图片/视频/文件消息的 Content 是内部存储路径
        // （形如 20260418/<guid>.png），用户按文件名完全搜不到——
        // 这与 README 宣称的「全文搜索」不符。
        var queryable = dbContext.ShareItems
            .AsNoTracking()
            .NotExpired(timeProvider.GetUtcNow())
            .Where(x => x.TopicId == topicId
                        && (EF.Functions.Like(x.Content.ToLower(), likePattern, @"\")
                            || (x.FileName != null
                                && EF.Functions.Like(x.FileName.ToLower(), likePattern, @"\"))
                            || (x.LinkTitle != null
                                && EF.Functions.Like(x.LinkTitle.ToLower(), likePattern, @"\"))
                            || (x.LinkDescription != null
                                && EF.Functions.Like(x.LinkDescription.ToLower(), likePattern, @"\"))));

        if (before.HasValue)
        {
            queryable = queryable.Where(x => x.CreatedAt < before.Value);
        }

        // 多取一条以判断是否有更多数据，避免额外的 COUNT/ANY 查询
        var rawMessages = await queryable
            .OrderByDescending(x => x.CreatedAt)
            .Take(safeLimit + 1)
            .Select(x => x.ToDto())
            .ToListAsync(ct);

        var hasMore = rawMessages.Count > safeLimit;
        var messages = hasMore ? rawMessages.Take(safeLimit).ToList() : rawMessages;

        string? nextCursor = null;
        if (hasMore)
        {
            nextCursor = messages[^1].CreatedAt.ToString("O");
        }

        return new TopicMessagesResponse(messages, hasMore, nextCursor);
    }

    public async Task<IReadOnlyList<ShareItemDto>> GetTopicMessagesByDateAsync(
        Guid topicId,
        DateOnly date,
        TimeZoneInfo timeZone,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var (startUtc, endUtc) = GetUtcRange(date, date, timeZone);

        // S0-06：已到期的定时销毁消息立即不可见，不等清理任务。
        return await dbContext.ShareItems
            .AsNoTracking()
            .NotExpired(timeProvider.GetUtcNow())
            .Where(x => x.TopicId == topicId && x.CreatedAt >= startUtc && x.CreatedAt < endUtc)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetTopicActiveDatesAsync(
        Guid topicId,
        DateOnly start,
        DateOnly end,
        TimeZoneInfo timeZone,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var (startUtc, endUtc) = GetUtcRange(start, end, timeZone);

        // S1-03：分批游标扫描，只拉取 (CreatedAt, Id) 两列，每批 1000 条，
        // 不再把整个日期范围的时间戳一次性全量加载进内存。
        // S0-06：已到期的定时销毁消息不产生活跃日期。
        var activeDates = new HashSet<DateOnly>();
        DateTimeOffset? cursorCreatedAt = null;
        Guid? cursorId = null;
        const int batchSize = 1000;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var pageQuery = dbContext.ShareItems
                .AsNoTracking()
                .NotExpired(timeProvider.GetUtcNow())
                .Where(x => x.TopicId == topicId && x.CreatedAt >= startUtc && x.CreatedAt < endUtc);
            if (cursorCreatedAt.HasValue)
            {
                pageQuery = pageQuery.Where(x => x.CreatedAt > cursorCreatedAt.Value
                    || (x.CreatedAt == cursorCreatedAt.Value && x.Id.CompareTo(cursorId!.Value) > 0));
            }

            var page = await pageQuery
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Select(x => new { x.CreatedAt, x.Id })
                .Take(batchSize)
                .ToListAsync(ct);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var row in page)
            {
                activeDates.Add(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.CreatedAt, timeZone).DateTime));
            }

            cursorCreatedAt = page[^1].CreatedAt;
            cursorId = page[^1].Id;
            if (page.Count < batchSize)
            {
                break;
            }
        }

        return activeDates;
    }

    /// <summary>
    /// 把「某个时区里的某段本地日期」转换为 UTC 的 [start, end) 区间。
    ///
    /// 不能用 <c>DateTimeKind.Local</c> 拼 <see cref="DateTimeOffset"/>：那取的是**服务器**
    /// 的时区，而用户看到的日期是按浏览器/设备时区划分的，两者不一致时日期会整体错位一天。
    ///
    /// 也不能简单地把本地时间当成固定偏移：带夏令时的时区在同一天内偏移量可能变化，
    /// DST 切换日甚至会是 23 或 25 小时。这里用 <see cref="TimeZoneInfo"/> 做真实转换。
    /// </summary>
    internal static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetUtcRange(
        DateOnly startInclusive,
        DateOnly endInclusive,
        TimeZoneInfo timeZone)
    {
        var localStart = startInclusive.ToDateTime(TimeOnly.MinValue);
        var localEndExclusive = endInclusive.ToDateTime(TimeOnly.MinValue).AddDays(1);

        return (ToUtcOffset(localStart, timeZone), ToUtcOffset(localEndExclusive, timeZone));
    }

    private static DateTimeOffset ToUtcOffset(DateTime localUnspecified, TimeZoneInfo timeZone)
    {
        // 夏令时春季跳变时这一小时在本地并不存在，按惯例顺延一小时；
        // 秋季重复的一小时则取标准偏移（GetUtcOffset 对歧义时间返回标准时间偏移）。
        if (timeZone.IsInvalidTime(localUnspecified))
        {
            localUnspecified = localUnspecified.AddHours(1);
        }

        return new DateTimeOffset(localUnspecified, timeZone.GetUtcOffset(localUnspecified));
    }

    public async Task<TopicMessagesResponse> GetTopicMessagesByTypeAsync(
        Guid topicId,
        ShareContentType contentType,
        int limit = 50,
        DateTimeOffset? before = null,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.Topics.AnyAsync(t => t.Id == topicId, ct);
        if (!exists)
        {
            return new TopicMessagesResponse([], false, null);
        }

        var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);

        var queryable = dbContext.ShareItems
            .AsNoTracking()
            .NotExpired(timeProvider.GetUtcNow())
            .Where(x => x.TopicId == topicId && x.ContentType == contentType);

        if (before.HasValue)
        {
            queryable = queryable.Where(x => x.CreatedAt < before.Value);
        }

        // 多取一条以判断是否有更多数据，避免额外的 COUNT/ANY 查询
        var rawMessages = await queryable
            .OrderByDescending(x => x.CreatedAt)
            .Take(safeLimit + 1)
            .Select(x => x.ToDto())
            .ToListAsync(ct);

        var hasMore = rawMessages.Count > safeLimit;
        var messages = hasMore ? rawMessages.Take(safeLimit).ToList() : rawMessages;

        string? nextCursor = null;
        if (hasMore)
        {
            nextCursor = messages[^1].CreatedAt.ToString("O");
        }

        return new TopicMessagesResponse(messages, hasMore, nextCursor);
    }

    private static string BuildPreview(string content)
    {
        const int maxLen = 80;
        return content.Length > maxLen ? content[..maxLen] + "…" : content;
    }

    private static bool IsLink(string content)
        => Uri.TryCreate(content, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public async Task<int> CleanupOldMessagesAsync(int months, Guid? topicId = null, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var safeMonths = months is 1 or 3 or 6 ? months : 1;
        var cutoff = timeProvider.GetUtcNow().AddMonths(-safeMonths);

        // S1-03：分批游标标记，每批 500 条、每批一次提交，不再把全部超龄行
        // 一次性加载进内存再一次性提交（万级消息时既占内存又是长事务）。
        // 全局查询过滤器已排除已标记的行，所以游标推进时不会重复读到本批
        // 刚标记的行；每批提交后立即广播本批 ID，进程中途被 kill 时已标记
        // 的批不会丢、未标记的批下次清理继续。
        var affectedTopicIds = new HashSet<Guid>();
        var deletedIds = new List<Guid>();
        var deleteAfter = timeProvider.GetUtcNow().AddDays(DeletionPolicy.DelayedDeleteDays);
        DateTimeOffset? cursorCreatedAt = null;
        Guid? cursorId = null;
        const int batchSize = 500;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var pageQuery = dbContext.ShareItems
                .Where(x => x.CreatedAt < cutoff);
            if (topicId.HasValue)
            {
                pageQuery = pageQuery.Where(x => x.TopicId == topicId.Value);
            }

            if (cursorCreatedAt.HasValue)
            {
                pageQuery = pageQuery.Where(x => x.CreatedAt > cursorCreatedAt.Value
                    || (x.CreatedAt == cursorCreatedAt.Value && x.Id.CompareTo(cursorId!.Value) > 0));
            }

            var page = await pageQuery
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var item in page)
            {
                item.DeleteAfter = deleteAfter;
                deletedIds.Add(item.Id);
                if (item.TopicId.HasValue)
                {
                    affectedTopicIds.Add(item.TopicId.Value);
                }
            }

            await dbContext.SaveChangesWithRetryAsync(ct);
            // 清掉本批的变更跟踪，否则上下文会随批数增长一直攒着所有已标记行。
            dbContext.ChangeTracker.Clear();

            // 广播本批被清理的消息 ID，让已打开的客户端同步移除对应气泡
            await hubContext.Clients.All.SendAsync("ShareItemsDeleted", page.Select(x => x.Id).ToList(), CancellationToken.None);

            cursorCreatedAt = page[^1].CreatedAt;
            cursorId = page[^1].Id;
            if (page.Count < batchSize)
            {
                break;
            }
        }

        if (deletedIds.Count == 0)
        {
            return 0;
        }

        // 更新受影响主题的预览并广播
        if (affectedTopicIds.Count > 0)
        {
            await RefreshTopicsAndBroadcastAsync(affectedTopicIds, ct);
        }

        return deletedIds.Count;
    }

    public async Task<int> DeleteShareItemsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return 0;
        }

        var items = await dbContext.ShareItems
            .Where(x => idList.Contains(x.Id))
            .ToListAsync(ct);

        if (items.Count == 0)
        {
            return 0;
        }

        var affectedTopicIds = items
            .Where(x => x.TopicId.HasValue)
            .Select(x => x.TopicId!.Value)
            .Distinct()
            .ToHashSet();

        var deletedIds = items.Select(x => x.Id).ToList();

        // S0-05（DAT-01）：先提交数据库删除，再删文件。顺序反过来之前是
        // 「先删文件、再提交」，进程在中间被 kill 就会留下「行还在、文件已删」
        // 的悬空引用；现在中间被 kill 只会留下孤儿文件（对账可发现），
        // 行一定已经不可见。文件删失败不静默丢失，进待删除队列。
        dbContext.ShareItems.RemoveRange(items);
        await dbContext.SaveChangesWithRetryAsync(ct);

        foreach (var item in items)
        {
            var failedPaths = await ShareItemFileCleanup.DeleteItemFilesAsync(
                fileStorageService, logger, item, ct, "batch delete");
            foreach (var path in failedPaths)
            {
                await PendingFileDeletionQueue.EnqueueAsync(dbContext, item.Id, path, "batch delete", ct);
            }
        }

        // 广播被删除的消息 ID，让所有客户端同步移除对应气泡；
        // 使用 CancellationToken.None 确保请求取消不影响其他客户端的同步
        await hubContext.Clients.All.SendAsync("ShareItemsDeleted", deletedIds, CancellationToken.None);

        if (affectedTopicIds.Count > 0)
        {
            await RefreshTopicsAndBroadcastAsync(affectedTopicIds, ct);
        }

        return deletedIds.Count;
    }

    /// <summary>
    /// 刷新指定主题的最新消息预览，并通过 SignalR 广播 TopicsUpdated。
    /// </summary>
    private async Task RefreshTopicsAndBroadcastAsync(HashSet<Guid> topicIds, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var topics = await dbContext.Topics
            .Where(t => topicIds.Contains(t.Id))
            .ToListAsync(ct);

        // S1-03：不再一次性加载所有受影响主题的全部剩余消息（万级消息时
        // 全量进内存）；受影响主题数通常很少，逐主题取最新一条即可。
        // S0-06：主题预览不取已到期的消息。
        foreach (var topic in topics)
        {
            var latest = await dbContext.ShareItems
                .NotExpired(timeProvider.GetUtcNow())
                .Where(x => x.TopicId == topic.Id)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            topic.LastMessageAt = latest?.CreatedAt;
            topic.LastMessagePreview = latest is null
                ? null
                : (latest.FileName ?? latest.Content) is { } preview
                    ? preview.Length > 80 ? preview[..80] + "…" : preview
                    : null;
        }

        await dbContext.SaveChangesWithRetryAsync(ct);

        var allTopics = await topicService.GetAllTopicsAsync(ct);
        await hubContext.Clients.All.SendAsync("TopicsUpdated", allTopics, CancellationToken.None);
    }


    private static ShareContentType ResolveContentType(string mimeType)
    {
        // 先规范化：带参数的 "image/png; charset=x" 之类不应影响分类，
        // 且入库的 MimeType 与服务端内联判断用同一份规范化结果。
        var normalized = MimeTypeHelper.Normalize(mimeType);
        if (normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return ShareContentType.Image;
        }

        if (normalized.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return ShareContentType.Video;
        }

        return ShareContentType.File;
    }
}
