using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.SignalR;

namespace AnyDrop.Hubs;

/// <summary>
/// SignalR hub for real-time share item synchronization.
/// </summary>
public sealed class ShareHub(IShareService shareService) : Hub
{
    /// <summary>
    /// Sends text via service to persist and broadcast.
    /// </summary>
    /// <param name="content">Text message payload.</param>
    /// <returns>The created share item.</returns>
    public Task<ShareItemDto> SendTextAsync(string content)
    {
        return shareService.SendTextAsync(content);
    }

    /// <summary>
    /// Gets recent messages via service.
    /// </summary>
    /// <param name="count">Maximum count of records.</param>
    /// <returns>The recent messages.</returns>
    public Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50)
    {
        return shareService.GetRecentAsync(count);
    }
}
