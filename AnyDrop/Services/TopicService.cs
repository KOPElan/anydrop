using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AnyDrop.Services;

/// <summary>
/// S1-03（DAT-09）：用 <see cref="IDbContextFactory{TContext}"/>、
/// 每次调用建短寿命上下文，不再随 Blazor 电路持有同一上下文数小时。
/// S3-06：时间统一走注入的 <see cref="TimeProvider"/>（与后台任务、清理服务同一时钟源），
/// 不再直接读 <c>DateTimeOffset.UtcNow</c>。
/// </summary>
public sealed class TopicService(IDbContextFactory<AnyDropDbContext> dbContextFactory, IHubContext<ShareHub> hubContext, TimeProvider timeProvider) : ITopicService
{
    private const int TopicIconMaxLength = 100;

    public async Task<IReadOnlyList<TopicDto>> GetAllTopicsAsync(CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var topics = await BuildOrderedTopicsQuery(dbContext)
            .Where(x => !x.IsArchived)
            .AsNoTracking()
            .ToListAsync(ct);

        // S0-06：计数不含已到期的定时销毁消息。
        var counts = await dbContext.ShareItems
            .AsNoTracking()
            .NotExpired(timeProvider.GetUtcNow())
            .Where(x => x.TopicId != null)
            .GroupBy(x => x.TopicId)
            .Select(g => new { TopicId = g.Key!.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        return topics
            .Select(t => new TopicDto(
                t.Id,
                t.Name,
                t.Icon,
                t.SortOrder,
                t.CreatedAt,
                t.LastMessageAt,
                counts.GetValueOrDefault(t.Id, 0),
                t.IsBuiltIn,
                t.LastMessagePreview,
                t.IsPinned,
                t.PinnedAt,
                t.IsArchived,
                t.ArchivedAt))
            .ToList();
    }

    public async Task<TopicDto?> GetTopicByIdAsync(Guid topicId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var topic = await dbContext.Topics
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId, ct);

        if (topic is null)
        {
            return null;
        }

        var messageCount = await dbContext.ShareItems
            .AsNoTracking()
            .NotExpired(timeProvider.GetUtcNow())
            .CountAsync(x => x.TopicId == topicId, ct);

        return new TopicDto(
            topic.Id,
            topic.Name,
            topic.Icon,
            topic.SortOrder,
            topic.CreatedAt,
            topic.LastMessageAt,
            messageCount,
            topic.IsBuiltIn,
            topic.LastMessagePreview,
            topic.IsPinned,
            topic.PinnedAt,
            topic.IsArchived,
            topic.ArchivedAt);
    }

    public async Task<IReadOnlyList<TopicDto>> GetArchivedTopicsAsync(CancellationToken ct = default)    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var topics = await dbContext.Topics
            .Where(x => x.IsArchived)
            .OrderByDescending(x => x.ArchivedAt ?? x.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        var topicIds = topics.Select(t => t.Id).ToHashSet();
        // S0-06：计数不含已到期的定时销毁消息。
        var counts = await dbContext.ShareItems
            .AsNoTracking()
            .NotExpired(timeProvider.GetUtcNow())
            .Where(x => x.TopicId != null && topicIds.Contains(x.TopicId.Value))
            .GroupBy(x => x.TopicId)
            .Select(g => new { TopicId = g.Key!.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        return topics
            .Select(t => new TopicDto(
                t.Id,
                t.Name,
                t.Icon,
                t.SortOrder,
                t.CreatedAt,
                t.LastMessageAt,
                counts.GetValueOrDefault(t.Id, 0),
                t.IsBuiltIn,
                t.LastMessagePreview,
                t.IsPinned,
                t.PinnedAt,
                t.IsArchived,
                t.ArchivedAt))
            .ToList();
    }

    public async Task<TopicDto> CreateTopicAsync(CreateTopicRequest request, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var name = ValidateName(request.Name);
        var topic = new Topic
        {
            Name = name,
            SortOrder = int.MaxValue,
            CreatedAt = timeProvider.GetUtcNow()
        };

        dbContext.Topics.Add(topic);
        await dbContext.SaveChangesWithRetryAsync(ct);

        await BroadcastTopicsUpdatedAsync(ct);

        return new TopicDto(topic.Id, topic.Name, topic.Icon, topic.SortOrder, topic.CreatedAt, topic.LastMessageAt, 0, topic.IsBuiltIn, topic.LastMessagePreview, topic.IsPinned, topic.PinnedAt, topic.IsArchived, topic.ArchivedAt);
    }

    public async Task<TopicDto?> UpdateTopicAsync(Guid topicId, UpdateTopicRequest request, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId, ct);
        if (topic is null)
        {
            return null;
        }

        topic.Name = ValidateName(request.Name);
        await dbContext.SaveChangesWithRetryAsync(ct);
        await BroadcastTopicsUpdatedAsync(ct);

        var messageCount = await dbContext.ShareItems
            .NotExpired(timeProvider.GetUtcNow())
            .CountAsync(s => s.TopicId == topic.Id, ct);
        return new TopicDto(topic.Id, topic.Name, topic.Icon, topic.SortOrder, topic.CreatedAt, topic.LastMessageAt, messageCount, topic.IsBuiltIn, topic.LastMessagePreview, topic.IsPinned, topic.PinnedAt, topic.IsArchived, topic.ArchivedAt);
    }

    public async Task<TopicDto?> UpdateTopicIconAsync(Guid topicId, UpdateTopicIconRequest request, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId, ct);
        if (topic is null)
        {
            return null;
        }

        topic.Icon = NormalizeAndValidateIcon(request.Icon);
        await dbContext.SaveChangesWithRetryAsync(ct);
        await BroadcastTopicsUpdatedAsync(ct);

        var messageCount = await dbContext.ShareItems
            .NotExpired(timeProvider.GetUtcNow())
            .CountAsync(s => s.TopicId == topic.Id, ct);
        return new TopicDto(topic.Id, topic.Name, topic.Icon, topic.SortOrder, topic.CreatedAt, topic.LastMessageAt, messageCount, topic.IsBuiltIn, topic.LastMessagePreview, topic.IsPinned, topic.PinnedAt, topic.IsArchived, topic.ArchivedAt);
    }

    public async Task<bool> DeleteTopicAsync(Guid topicId, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId, ct);
        if (topic is null)
        {
            return false;
        }

        // Protect built-in topics from deletion.
        if (topic.IsBuiltIn)
        {
            return false;
        }

        // 注意：这里必须 IgnoreQueryFilters——已标记延迟删除 / 已过期的消息
        // 也要解开关联，否则删主题会在外键上失败。
        var messages = await dbContext.ShareItems
            .IgnoreQueryFilters()
            .Where(s => s.TopicId == topicId)
            .ToListAsync(ct);
        foreach (var message in messages)
        {
            message.TopicId = null;
        }

        dbContext.Topics.Remove(topic);
        await dbContext.SaveChangesWithRetryAsync(ct);
        await BroadcastTopicsUpdatedAsync(ct);

        return true;
    }

    // Normalizes and validates the topic icon before persisting it so invalid input
    // is rejected at the service boundary instead of surfacing as a DbUpdateException.
    private static string NormalizeAndValidateIcon(string? icon)
    {
        var normalizedIcon = icon?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedIcon))
        {
            return "chat_bubble";
        }

        if (normalizedIcon.Length > TopicIconMaxLength)
        {
            throw new ArgumentException($"Icon must be {TopicIconMaxLength} characters or fewer.", nameof(icon));
        }

        return normalizedIcon;
    }

    public async Task ReorderTopicsAsync(ReorderTopicsRequest request, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("items must not be empty", nameof(request));
        }

        if (request.Items.Any(i => i.SortOrder < 0))
        {
            throw new ArgumentException("sortOrder must be >= 0", nameof(request));
        }

        var orderedItems = request.Items
            .DistinctBy(i => i.TopicId)
            .ToList();

        IDbContextTransaction? tx = null;
        if (dbContext.Database.IsRelational())
        {
            tx = await dbContext.Database.BeginTransactionAsync(ct);
        }

        try
        {
            var topicIds = orderedItems.Select(i => i.TopicId).ToHashSet();
            var topics = await dbContext.Topics
                .Where(t => topicIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, ct);

            if (topics.Count != topicIds.Count)
            {
                throw new ArgumentException("One or more topics do not exist.", nameof(request));
            }

            foreach (var item in orderedItems)
            {
                topics[item.TopicId].SortOrder = item.SortOrder;
            }

            await dbContext.SaveChangesWithRetryAsync(ct);
            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            await BroadcastTopicsUpdatedAsync(ct);
        }
        catch
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }

            throw;
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }
    }

    public async Task<TopicDto> PinTopicAsync(Guid topicId, bool isPinned, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId, ct)
            ?? throw new KeyNotFoundException("主题不存在");

        topic.IsPinned = isPinned;
        topic.PinnedAt = isPinned ? timeProvider.GetUtcNow() : null;

        await dbContext.SaveChangesWithRetryAsync(ct);
        await BroadcastTopicsUpdatedAsync(ct);

        var messageCount = await dbContext.ShareItems
            .NotExpired(timeProvider.GetUtcNow())
            .CountAsync(s => s.TopicId == topic.Id, ct);
        return new TopicDto(topic.Id, topic.Name, topic.Icon, topic.SortOrder, topic.CreatedAt, topic.LastMessageAt, messageCount, topic.IsBuiltIn, topic.LastMessagePreview, topic.IsPinned, topic.PinnedAt, topic.IsArchived, topic.ArchivedAt);
    }

    public async Task<TopicDto> ArchiveTopicAsync(Guid topicId, bool isArchived, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId, ct)
            ?? throw new KeyNotFoundException("主题不存在");

        topic.IsArchived = isArchived;
        topic.ArchivedAt = isArchived ? timeProvider.GetUtcNow() : null;

        // 归档时自动取消置顶
        if (isArchived)
        {
            topic.IsPinned = false;
            topic.PinnedAt = null;
        }

        await dbContext.SaveChangesWithRetryAsync(ct);
        await BroadcastTopicsUpdatedAsync(ct);

        var messageCount = await dbContext.ShareItems
            .NotExpired(timeProvider.GetUtcNow())
            .CountAsync(s => s.TopicId == topic.Id, ct);
        return new TopicDto(topic.Id, topic.Name, topic.Icon, topic.SortOrder, topic.CreatedAt, topic.LastMessageAt, messageCount, topic.IsBuiltIn, topic.LastMessagePreview, topic.IsPinned, topic.PinnedAt, topic.IsArchived, topic.ArchivedAt);
    }

    public async Task<TopicMessagesResponse?> GetTopicMessagesAsync(Guid topicId, int limit, DateTimeOffset? before, CancellationToken ct = default)
        => await GetTopicMessagesAsync(topicId, limit, before, after: null, ct);

    /// <summary>
    /// S2-04：重连补拉——<paramref name="after"/> 之后的新消息（按时间升序返回），
    /// 供断线重连后增量补拉断线期消息。after 与 before 不可同时使用。
    /// </summary>
    public async Task<TopicMessagesResponse?> GetTopicMessagesAsync(Guid topicId, int limit, DateTimeOffset? before, DateTimeOffset? after, CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var exists = await dbContext.Topics.AnyAsync(t => t.Id == topicId, ct);
        if (!exists)
        {
            return null;
        }

        var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);
        // S0-06：已到期的定时销毁消息立即不可见，不等清理任务。
        var query = dbContext.ShareItems
            .AsNoTracking()
            .NotExpired(timeProvider.GetUtcNow())
            .Where(x => x.TopicId == topicId);

        if (after.HasValue)
        {
            // 用 >= 而不是 >：同一 CreatedAt 刻度（tick）内的多条消息不会被分页边界漏掉；
            // 边界消息会被重复返回一次，调用方按 Id 去重（并以“本页无新增”作为分页终止条件）。
            query = query.Where(x => x.CreatedAt >= after.Value);
            var newMessages = await query
                .OrderBy(x => x.CreatedAt)
                .Take(safeLimit)
                .Select(x => x.ToDto())
                .ToListAsync(ct);
            return new TopicMessagesResponse(newMessages, false, null);
        }

        if (before.HasValue)
        {
            query = query.Where(x => x.CreatedAt < before.Value);
        }

        var messages = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(safeLimit)
            .Select(x => x.ToDto())
            .ToListAsync(ct);

        var hasMore = false;
        string? nextCursor = null;
        if (messages.Count == safeLimit)
        {
            var lastCreatedAt = messages[^1].CreatedAt;
            hasMore = await dbContext.ShareItems
                .AsNoTracking()
                .NotExpired(timeProvider.GetUtcNow())
                .AnyAsync(x => x.TopicId == topicId && x.CreatedAt < lastCreatedAt, ct);
            if (hasMore)
            {
                nextCursor = lastCreatedAt.ToString("O");
            }
        }

        return new TopicMessagesResponse(messages, hasMore, nextCursor);
    }

    private async Task BroadcastTopicsUpdatedAsync(CancellationToken ct)
    {
        var topics = await GetAllTopicsAsync(ct);
        await hubContext.Clients.All.SendAsync("TopicsUpdated", topics, CancellationToken.None);
    }

    private static IQueryable<Topic> BuildOrderedTopicsQuery(AnyDropDbContext dbContext)
    {
        return dbContext.Topics
            .OrderByDescending(x => x.IsPinned)
            .ThenBy(x => x.PinnedAt.HasValue ? 0 : 1)
            .ThenBy(x => x.PinnedAt)
            .ThenBy(x => x.SortOrder)
            .ThenByDescending(x => x.LastMessageAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(x => x.CreatedAt);
    }

    private static string ValidateName(string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
        {
            throw new ArgumentException("主题名称不能为空，且不超过 100 个字符", nameof(name));
        }

        return normalized;
    }
}
