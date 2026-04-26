using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

public sealed class ShareService(
    AnyDropDbContext dbContext,
    IHubContext<ShareHub> hubContext,
    ITopicService topicService,
    IFileStorageService fileStorageService,
    LinkMetadataService linkMetadataService,
    ISystemSettingsService systemSettingsService,
    IServiceScopeFactory scopeFactory) : IShareService
{
    public async Task<ShareItemDto> SendTextAsync(string content, Guid? topicId = null, bool burnAfterReading = false, CancellationToken ct = default)
    {
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
        var now = DateTimeOffset.UtcNow;
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
        await dbContext.SaveChangesAsync(ct);

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

            // 创建独立 Scope，避免复用已被请求 Scope 释放的 DbContext
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnyDropDbContext>();

            var item = await db.ShareItems.FindAsync(itemId);
            if (item is null)
            {
                return;
            }

            item.LinkTitle = title;
            item.LinkDescription = description;
            await db.SaveChangesAsync();

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
        var storagePath = await fileStorageService.SaveFileAsync(fileStream, fileName, mimeType, ct);

        Topic? topic = null;
        if (topicId.HasValue)
        {
            topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId.Value, ct);
            if (topic is null)
            {
                throw new ArgumentException("Topic does not exist.", nameof(topicId));
            }
        }

        // 优先使用调用方传入的已知大小，其次尝试从流读取（仅当流支持 Seek）
        var fileSize = knownFileSize ?? (fileStream.CanSeek ? fileStream.Length : null);

        var now = DateTimeOffset.UtcNow;
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
        await dbContext.SaveChangesAsync(ct);

        var dto = item.ToDto();
        await hubContext.Clients.All.SendAsync("ReceiveShareItem", dto, CancellationToken.None);

        if (topic is not null)
        {
            var topics = await topicService.GetAllTopicsAsync(ct);
            await hubContext.Clients.All.SendAsync("TopicsUpdated", topics, CancellationToken.None);
        }

        return dto;
    }

    public async Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default)
    {
        var normalizedCount = count <= 0 ? 50 : count;
        var safeCount = Math.Clamp(normalizedCount, 1, 200);

        return await dbContext.ShareItems
            .AsNoTracking()
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
        var normalizedSearch = normalized.ToLower();

        // 使用显式的大小写不敏感匹配，避免 SQLite 在未配置 NOCASE 时出现大小写行为与需求不一致。
        // 同时转义 LIKE 通配符，尽量保持原先 Contains 的“字面子串匹配”语义。
        var escapedSearch = normalizedSearch
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        var likePattern = $"%{escapedSearch}%";

        var queryable = dbContext.ShareItems
            .AsNoTracking()
            .Where(x => x.TopicId == topicId && EF.Functions.Like(x.Content.ToLower(), likePattern, @"\"));

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
        CancellationToken ct = default)
    {
        // 使用服务器本地时区将日期转换为 UTC 范围，与消息时间显示保持一致
        var localMidnight = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Local);
        var startOffset = new DateTimeOffset(localMidnight);
        var endOffset = startOffset.AddDays(1);

        return await dbContext.ShareItems
            .AsNoTracking()
            .Where(x => x.TopicId == topicId && x.CreatedAt >= startOffset && x.CreatedAt < endOffset)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDto())
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetTopicActiveDatesAsync(
        Guid topicId,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default)
    {
        // 将本地日期范围转换为 UTC 范围
        var localStart = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, DateTimeKind.Local);
        var localEndExclusive = new DateTime(end.Year, end.Month, end.Day, 0, 0, 0, DateTimeKind.Local).AddDays(1);
        var startOffset = new DateTimeOffset(localStart);
        var endOffset   = new DateTimeOffset(localEndExclusive);

        // 只拉取 CreatedAt 列，应用端转换为本地日期后去重
        var timestamps = await dbContext.ShareItems
            .AsNoTracking()
            .Where(x => x.TopicId == topicId && x.CreatedAt >= startOffset && x.CreatedAt < endOffset)
            .Select(x => x.CreatedAt)
            .ToListAsync(ct);

        return timestamps
            .Select(t => DateOnly.FromDateTime(t.ToLocalTime().DateTime))
            .ToHashSet();
    }

    public async Task<TopicMessagesResponse> GetTopicMessagesByTypeAsync(
        Guid topicId,
        ShareContentType contentType,
        int limit = 50,
        DateTimeOffset? before = null,
        CancellationToken ct = default)
    {
        var exists = await dbContext.Topics.AnyAsync(t => t.Id == topicId, ct);
        if (!exists)
        {
            return new TopicMessagesResponse([], false, null);
        }

        var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);

        var queryable = dbContext.ShareItems
            .AsNoTracking()
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

    private static ShareContentType ResolveContentType(string mimeType)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return ShareContentType.Image;
        }

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return ShareContentType.Video;
        }

        return ShareContentType.File;
    }
}
