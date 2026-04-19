using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;

namespace AnyDrop.Hubs;

public sealed class ShareHub : Hub
{
    public async Task SendTopicsUpdatedAsync(IReadOnlyList<TopicDto> topics)
    {
        await Clients.All.SendAsync("TopicsUpdated", topics);
    }
}
