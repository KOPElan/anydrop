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
    /// <summary>新消息添加时触发，订阅方可据此滚动到底部。</summary>
    event Action? MessageAdded;
    void NotifyStateChanged();
    void NotifyMessageAdded();
}
