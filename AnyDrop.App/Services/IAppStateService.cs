using AnyDrop.App.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace AnyDrop.App.Services;

/// <summary>Scoped 应用状态，用于跨组件共享 UI 状态。</summary>
public interface IAppStateService
{
    Guid? CurrentTopicId { get; set; }
    IList<TopicDto> Topics { get; }
    IList<ShareItemDto> Messages { get; }
    bool HasMoreMessages { get; set; }
    string? MessageCursor { get; set; }
    HubConnectionState SignalRState { get; set; }
    event Action? OnChange;
    void NotifyStateChanged();
}
