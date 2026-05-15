using AnyDrop.Models;
using Microsoft.AspNetCore.SignalR;

namespace AnyDrop.Hubs;

public sealed class ShareHub : Hub
{
    public async Task SendTopicsUpdatedAsync(IReadOnlyList<TopicDto> topics)
    {
        await Clients.All.SendAsync("TopicsUpdated", topics);
    }

    public async Task NotifyUploadStarted(UploadPendingSignal signal)
    {
        await Clients.Others.SendAsync("ReceiveUploadPending", signal);
    }

    public async Task NotifyUploadSettled(string tempId, Guid topicId)
    {
        await Clients.Others.SendAsync("RemoveUploadPending", tempId, topicId);
    }
}
