using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Services;

public sealed class ShareService(AnyDropDbContext dbContext, IHubContext<ShareHub> hubContext) : IShareService
{
    public async Task<ShareItemDto> SendTextAsync(string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required", nameof(content));
        }

        if (content.Length > 10_000)
        {
            throw new ArgumentException("Content length must be less than or equal to 10000 characters", nameof(content));
        }

        var item = new ShareItem
        {
            ContentType = ShareContentType.Text,
            Content = content.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ShareItems.Add(item);
        await dbContext.SaveChangesAsync(ct);

        var dto = item.ToDto();
        await hubContext.Clients.All.SendAsync("ReceiveShareItem", dto, ct);
        return dto;
    }

    public async Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default)
    {
        var normalizedCount = count <= 0 ? 50 : count;
        var safeCount = Math.Clamp(normalizedCount, 1, 200);

        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
        {
            var inMemoryItems = await dbContext.ShareItems
                .AsNoTracking()
                .ToListAsync(ct);

            return inMemoryItems
                .OrderByDescending(x => x.CreatedAt)
                .Take(safeCount)
                .Select(x => x.ToDto())
                .ToList();
        }

        return await dbContext.ShareItems
            .FromSqlInterpolated($"SELECT * FROM ShareItems ORDER BY CreatedAt DESC LIMIT {safeCount}")
            .AsNoTracking()
            .Select(x => x.ToDto())
            .ToListAsync(ct);
    }
}
