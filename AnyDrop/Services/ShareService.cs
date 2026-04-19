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
    LinkMetadataService linkMetadataService) : IShareService
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

        var isLink = IsLink(normalizedContent);
        var now = DateTimeOffset.UtcNow;
        var item = new ShareItem
        {
            ContentType = isLink ? ShareContentType.Link : ShareContentType.Text,
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

        // 先广播消息（让用户立即看到），再后台补全链接元数据
        var dto = item.ToDto();
        await hubContext.Clients.All.SendAsync("ReceiveShareItem", dto, CancellationToken.None);

        if (topic is not null)
        {
            var topics = await topicService.GetAllTopicsAsync(ct);
            await hubContext.Clients.All.SendAsync("TopicsUpdated", topics, CancellationToken.None);
        }

        // 异步抓取 OGP 元数据并更新，不阻塞本次返回
        if (isLink)
        {
            _ = FetchAndUpdateLinkMetadataAsync(item.Id, normalizedContent);
        }

        return dto;
    }

    /// <summary>
    /// 后台抓取链接元数据，更新数据库并推送更新后的 DTO 给所有客户端。
    /// 此方法独立于请求生命周期运行，失败时静默忽略。
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

            // 使用新的 DbContext 以避免并发冲突（原 Scoped context 可能已释放）
            var item = await dbContext.ShareItems.FindAsync(itemId);
            if (item is null)
            {
                return;
            }

            item.LinkTitle = title;
            item.LinkDescription = description;
            await dbContext.SaveChangesAsync();

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
        var item = new ShareItem
        {
            ContentType = contentType,
            Content = storagePath,
            FileName = fileName,
            FileSize = fileSize,
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
