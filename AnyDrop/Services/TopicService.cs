using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AnyDrop.Services;

public sealed class TopicService(AnyDropDbContext dbContext, IHubContext<ShareHub> hubContext) : ITopicService
{
    public async Task<IReadOnlyList<TopicDto>> GetAllTopicsAsync(CancellationToken ct = default)
    {
        var topics = await BuildOrderedTopicsQuery()
            .AsNoTracking()
            .ToListAsync(ct);

        var counts = await dbContext.ShareItems
            .AsNoTracking()
            .Where(x => x.TopicId != null)
            .GroupBy(x => x.TopicId)
            .Select(g => new { TopicId = g.Key!.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        return topics
            .Select(t => new TopicDto(
                t.Id,
                t.Name,
                t.SortOrder,
                t.CreatedAt,
                t.LastMessageAt,
                counts.GetValueOrDefault(t.Id, 0),
                t.IsBuiltIn,
                t.LastMessagePreview))
            .ToList();
    }

    public async Task<TopicDto> CreateTopicAsync(CreateTopicRequest request, CancellationToken ct = default)
    {
        var name = ValidateName(request.Name);
        var topic = new Topic
        {
            Name = name,
            SortOrder = int.MaxValue,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Topics.Add(topic);
        await dbContext.SaveChangesAsync(ct);

        await BroadcastTopicsUpdatedAsync(ct);

        return new TopicDto(topic.Id, topic.Name, topic.SortOrder, topic.CreatedAt, topic.LastMessageAt, 0, topic.IsBuiltIn, topic.LastMessagePreview);
    }

    public async Task<TopicDto?> UpdateTopicAsync(Guid topicId, UpdateTopicRequest request, CancellationToken ct = default)
    {
        var topic = await dbContext.Topics.FirstOrDefaultAsync(t => t.Id == topicId, ct);
        if (topic is null)
        {
            return null;
        }

        topic.Name = ValidateName(request.Name);
        await dbContext.SaveChangesAsync(ct);
        await BroadcastTopicsUpdatedAsync(ct);

        var messageCount = await dbContext.ShareItems.CountAsync(s => s.TopicId == topic.Id, ct);
        return new TopicDto(topic.Id, topic.Name, topic.SortOrder, topic.CreatedAt, topic.LastMessageAt, messageCount, topic.IsBuiltIn, topic.LastMessagePreview);
    }

    public async Task<bool> DeleteTopicAsync(Guid topicId, CancellationToken ct = default)
    {
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

        var messages = await dbContext.ShareItems.Where(s => s.TopicId == topicId).ToListAsync(ct);
        foreach (var message in messages)
        {
            message.TopicId = null;
        }

        dbContext.Topics.Remove(topic);
        await dbContext.SaveChangesAsync(ct);
        await BroadcastTopicsUpdatedAsync(ct);

        return true;
    }

    public async Task ReorderTopicsAsync(ReorderTopicsRequest request, CancellationToken ct = default)
    {
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

            await dbContext.SaveChangesAsync(ct);
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

    public async Task<TopicMessagesResponse?> GetTopicMessagesAsync(Guid topicId, int limit, DateTimeOffset? before, CancellationToken ct = default)
    {
        var exists = await dbContext.Topics.AnyAsync(t => t.Id == topicId, ct);
        if (!exists)
        {
            return null;
        }

        var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);
        var query = dbContext.ShareItems
            .AsNoTracking()
            .Where(x => x.TopicId == topicId);

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

    private IQueryable<Topic> BuildOrderedTopicsQuery()
    {
        return dbContext.Topics
            .OrderBy(x => x.SortOrder)
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
