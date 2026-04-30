using AnyDrop.App.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace AnyDrop.App.Services;

/// <summary>Scoped 应用状态实现。</summary>
public sealed class AppStateService : IAppStateService
{
    public Guid? CurrentTopicId { get; set; }
    public IList<TopicDto> Topics { get; } = new List<TopicDto>();
    public IList<ShareItemDto> Messages { get; } = new List<ShareItemDto>();
    public bool HasMoreMessages { get; set; }
    public string? MessageCursor { get; set; }
    public HubConnectionState SignalRState { get; set; } = HubConnectionState.Disconnected;

    public event Action? OnChange;

    public void NotifyStateChanged() => OnChange?.Invoke();
}
