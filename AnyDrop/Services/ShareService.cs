using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

/// <summary>
/// Handles share item persistence and real-time broadcasting.
/// </summary>
public sealed class ShareService(AnyDropDbContext dbContext, IHubContext<ShareHub> hubContext) : IShareService
{
    /// <summary>
    /// Persists and broadcasts a text message.
    /// </summary>
    /// <param name="content">Text message content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created share item DTO.</returns>
    public async Task<ShareItemDto> SendTextAsync(string content, CancellationToken ct = default)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var normalizedContent = content.Trim();

        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            throw new ArgumentException("Text content cannot be empty.", nameof(content));
        }

        if (normalizedContent.Length > ShareValidationRules.MaxTextLength)
        {
            throw new ArgumentException($"Text content cannot exceed {ShareValidationRules.MaxTextLength} characters.", nameof(content));
        }

        var item = new ShareItem
        {
            ContentType = ShareContentType.Text,
            Content = normalizedContent,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.ShareItems.Add(item);
        await dbContext.SaveChangesAsync(ct);

        var dto = item.ToDto();
        await hubContext.Clients.All.SendAsync("ReceiveShareItem", dto, ct);

        return dto;
    }

    /// <summary>
    /// Loads recent items and returns them in chronological order.
    /// </summary>
    /// <param name="count">Maximum number of records.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recent share item list.</returns>
    public async Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default)
    {
        if (count < 1 || count > ShareValidationRules.MaxRecentCountLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"Count must be between 1 and {ShareValidationRules.MaxRecentCountLimit}.");
        }

        var items = await dbContext.ShareItems
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .Take(count)
            .OrderBy(item => item.CreatedAt)
            .Select(item => item.ToDto())
            .ToListAsync(ct);

        return items;
    }
}
