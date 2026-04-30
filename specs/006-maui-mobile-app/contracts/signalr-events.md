# SignalR Events Contract: AnyDrop 移动端 App

**Feature**: `006-maui-mobile-app`  
**Phase**: 1 — Design & Contracts  
**Date**: 2026-04-29  
**Hub URL**: `{BaseUrl}/hubs/share`  
**Auth**: JWT Bearer 通过 `AccessTokenProvider` 注入（连接握手时传递）

---

## 连接配置

```csharp
_connection = new HubConnectionBuilder()
    .WithUrl($"{baseUrl}/hubs/share", options =>
    {
        options.AccessTokenProvider = async () => await _tokenStorage.GetTokenAsync();
    })
    .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
    .Build();
```

**指数退避策略**（`ExponentialBackoffRetryPolicy`）:

| 重试次数 | 等待时间 |
|---------|---------|
| 第 0 次 | 0 ms（立即重试） |
| 第 1 次 | 2,000 ms |
| 第 2 次 | 10,000 ms |
| 第 3+ 次 | 30,000 ms（上限） |

---

## 服务端 → 客户端事件

### `TopicsUpdated`

服务端广播主题列表变更（发送新消息、创建/删除/更新主题后触发）。

**签名**: `Clients.All.SendAsync("TopicsUpdated", topics)`

**Payload**: `IReadOnlyList<TopicDto>`（完整主题列表，含 `MessageCount` 和 `LastMessagePreview`）

```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "默认",
    "icon": "📋",
    "sortOrder": 0,
    "isPinned": false,
    "isArchived": false,
    "messageCount": 43,
    "lastMessagePreview": "Hello from mobile!",
    "lastMessageAt": "2026-04-29T10:00:00Z"
  }
]
```

**客户端处理逻辑**:
```csharp
// ISignalRService.cs
public interface ISignalRService
{
    event Action<IReadOnlyList<TopicDto>>? TopicsUpdated;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    HubConnectionState State { get; }
    event Action<HubConnectionState>? StateChanged;
}

// SignalRService.cs
_connection.On<IReadOnlyList<TopicDto>>("TopicsUpdated", topics =>
{
    TopicsUpdated?.Invoke(topics);
});
```

**Blazor 订阅示例**（`MessageList.razor`）:
```csharp
protected override async Task OnInitializedAsync()
{
    _signalRService.TopicsUpdated += OnTopicsUpdated;
}

private void OnTopicsUpdated(IReadOnlyList<TopicDto> topics)
{
    // 更新 AppStateService.Topics
    // 若当前主题有新消息（MessageCount 增加），重新加载消息列表尾部
    await InvokeAsync(StateHasChanged);
}

async ValueTask IAsyncDisposable.DisposeAsync()
{
    _signalRService.TopicsUpdated -= OnTopicsUpdated;
}
```

---

## 连接生命周期管理

### 连接状态

```
Disconnected ──[StartAsync]──→ Connecting ──→ Connected
Connected ──[网络断开]──→ Reconnecting ──[成功]──→ Connected
                                        ──[超时]──→ Disconnected
Connected ──[StopAsync]──→ Disconnected
```

### 状态变更通知

`ISignalRService.StateChanged` 事件向 UI 层广播当前连接状态：

| 状态 | UI 表现 |
|------|--------|
| `Connected` | 无指示（正常） |
| `Reconnecting` | 顶部/底部显示"正在重新连接…" 黄色横幅（非阻断） |
| `Disconnected` | 显示"已断开连接，点击重试"红色横幅 |

### 连接生命周期与用户状态

| 应用事件 | SignalR 动作 |
|---------|------------|
| 用户登录成功 | `SignalRService.StartAsync()` |
| 用户退出登录 | `SignalRService.StopAsync()` |
| App 进入前台 | 检查 `State == Disconnected` → 重新 `StartAsync()` |
| BaseUrl 变更 | `StopAsync()` → 重建 `HubConnectionManager` → `StartAsync()` |
| Token 过期（401） | `StopAsync()`（Token 清除后连接无效） |

---

## 客户端 → 服务端

> 当前版本服务端 `ShareHub` 不定义客户端调用方法（仅服务端广播）。
> 所有写操作通过 REST API 完成，SignalR 仅用于实时通知接收。

---

## 消息去重与时序处理

由于 REST API 发送消息和 SignalR 推送的时序不确定，客户端需处理以下场景：

1. **自己发送的消息**：`POST /share-items/text` 返回后，将消息追加至本地列表；随后收到 `TopicsUpdated` 时，通过 `MessageCount` 判断是否需要加载新消息（避免重复添加）。

2. **其他端发送的消息**：收到 `TopicsUpdated` 时，若当前主题的 `MessageCount` 大于本地已加载数量，调用 `GET /topics/{id}/messages`（不带 `before` 参数）获取最新一批消息，追加至列表底部。

3. **消息 ID 去重**：渲染前过滤已在本地列表中存在的消息 ID，防止因网络重试导致重复显示。

---

## 错误处理

| 场景 | 处理策略 |
|------|---------|
| 连接失败（网络不可达） | `WithAutomaticReconnect` 自动重试；UI 显示连接状态横幅 |
| Token 过期导致连接拒绝 | `Closed` 事件触发 → `AppEventBus.RaiseAuthExpired()` → 清除 Token → 导航至 `/login` |
| 服务端重启（连接中断） | 自动重连策略处理；重连成功后重新订阅事件 |
| Hub URL 无效（404/403） | `Closed` 事件触发，日志记录，显示连接错误提示 |
