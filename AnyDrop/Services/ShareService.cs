using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

public sealed class ShareService(
    AnyDropDbContext dbContext,
    IHubContext<ShareHub> hubContext,
    ITopicService topicService) : IShareService
{
    public async Task<ShareItemDto> SendTextAsync(string content, Guid? topicId = null, CancellationToken ct = default)
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

        var now = DateTimeOffset.UtcNow;
        var item = new ShareItem
        {
            ContentType = ShareContentType.Text,
            Content = normalizedContent,
            CreatedAt = now,
            TopicId = topicId
        };

        if (topic is not null)
        {
            topic.LastMessageAt = now;
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
}
