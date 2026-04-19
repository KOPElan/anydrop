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
    IFileStorageService fileStorageService) : IShareService
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
            ContentType = IsLink(normalizedContent) ? ShareContentType.Link : ShareContentType.Text,
            Content = normalizedContent,
            CreatedAt = now,
            TopicId = topicId
        };

        if (topic is not null)
        {
            topic.LastMessageAt = now;
            topic.LastMessagePreview = BuildPreview(normalizedContent);
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

    public async Task<ShareItemDto> SendFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid? topicId = null,
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

        var now = DateTimeOffset.UtcNow;
        var item = new ShareItem
        {
            ContentType = contentType,
            Content = storagePath,
            FileName = fileName,
            FileSize = fileStream.CanSeek ? fileStream.Length : null,
            MimeType = mimeType,
            CreatedAt = now,
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
