using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace AnyDrop.Components.Layout;

public partial class TopicSidebar : IAsyncDisposable
{
    [Inject] public required ITopicService TopicService { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required IJSRuntime JS { get; set; }
    [Inject] public required ILogger<TopicSidebar> Logger { get; set; }
    [Inject] public required AuthenticationStateProvider AuthenticationStateProvider { get; set; }
    [Inject] public required ITopicStateService TopicStateService { get; set; }

    // 由 MainLayout 通过 CascadingValue 提供，触发布局层 Modal（避免 backdrop-filter 限制）
    [CascadingParameter(Name = "OpenCreateTopicModal")] public Action? OpenCreateTopicModal { get; set; }

    private readonly List<TopicDto> _topics = [];
    private HubConnection? _hubConnection;
    private IDisposable? _topicsUpdatedSubscription;
    private DotNetObjectReference<TopicSidebar>? _dotNetRef;
    private Guid? _selectedTopicId;

    // 排序错误提示
    private string? _error;

    // 已归档主题下拉状态
    private bool _showArchivedDropdown;
    private readonly List<TopicDto> _archivedTopics = [];
    private string _nickname = "用户";

    // 未读通知：记录收到新消息但未查看的主题 ID
    private readonly HashSet<Guid> _unreadTopicIds = [];

    protected override async Task OnInitializedAsync()
    {
        TopicStateService.SelectedTopicChanged += HandleSelectedTopicChanged;
        TopicStateService.TopicsChanged += HandleTopicsChanged;

        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        _nickname = state.User.FindFirst("nickname")?.Value ?? state.User.Identity?.Name ?? "用户";
        _selectedTopicId = TopicStateService.SelectedTopicId;
        await LoadTopicsAsync();
        // InitializeHubAsync 已移至 OnAfterRenderAsync，避免预渲染阶段执行导致协商响应 HTML 错误
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Hub 只在交互模式（Blazor 电路已建立）下启动，避免预渲染时连接失败
            await InitializeHubAsync();
        }

        // 每次渲染后重新初始化 SortableJS，确保拖拽排序在任何状态变化后仍能正确工作。
        // initSortable 内部会先 destroy 旧实例再 create 新实例，避免重复绑定。
        if (_topics.Count == 0) return;

        _dotNetRef ??= DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("initSortable", "topic-list", _dotNetRef);
        }
        catch (JSDisconnectedException)
        {
            Logger.LogDebug("JS interop disconnected during initSortable — component is being disposed.");
        }
        catch (TaskCanceledException)
        {
            Logger.LogDebug("initSortable was cancelled — component is being disposed.");
        }
        catch (ObjectDisposedException ex)
        {
            Logger.LogDebug(ex, "JS runtime disposed during initSortable.");
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogDebug(ex, "JS interop not available during initSortable.");
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "JavaScript error during initSortable.");
        }
    }

    private async Task SelectTopicAsync(Guid topicId)
    {
        _selectedTopicId = topicId;
        _unreadTopicIds.Remove(topicId);
        await TopicStateService.SetSelectedTopicAsync(topicId);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnSortEnd(string[] orderedIdStrings)
    {
        if (orderedIdStrings.Length == 0)
        {
            return;
        }

        // 解析字符串 ID 为 Guid，过滤无效值
        var orderedIds = orderedIdStrings
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToArray();

        if (orderedIds.Length == 0) return;

        _error = null;
        var snapshot = _topics.ToList();

        var byId = _topics.ToDictionary(t => t.Id);
        var orderedSet = orderedIds.ToHashSet();
        var reordered = orderedIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
        reordered.AddRange(_topics.Where(t => !orderedSet.Contains(t.Id)));
        _topics.Clear();
        _topics.AddRange(reordered);
        await InvokeAsync(StateHasChanged);

        try
        {
            var items = orderedIds
                .Select((topicId, index) => new TopicOrderItem(topicId, index))
                .ToList();
            await TopicService.ReorderTopicsAsync(new ReorderTopicsRequest(items));
            await TopicStateService.NotifyTopicsChangedAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to reorder topics");
            _topics.Clear();
            _topics.AddRange(snapshot);
            _error = "排序更新失败，已回滚";
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadTopicsAsync()
    {
        _topics.Clear();
        _topics.AddRange(await TopicService.GetAllTopicsAsync());

        if (_selectedTopicId.HasValue && !_topics.Any(t => t.Id == _selectedTopicId.Value))
        {
            _selectedTopicId = null;
            await TopicStateService.SetSelectedTopicAsync(null);
        }

        // 若尚未选中任何主题，优先选中内置默认主题，否则选第一个
        if (!_selectedTopicId.HasValue && _topics.Count > 0)
        {
            var defaultTopic = _topics.FirstOrDefault(t => t.IsBuiltIn) ?? _topics[0];
            _selectedTopicId = defaultTopic.Id;
            await TopicStateService.SetSelectedTopicAsync(_selectedTopicId);
        }
    }

    private async Task ToggleArchivedDropdownAsync()
    {
        _showArchivedDropdown = !_showArchivedDropdown;
        if (_showArchivedDropdown)
        {
            await LoadArchivedTopicsAsync();
        }
    }

    private async Task InitializeHubAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/share"))
            .WithAutomaticReconnect()
            .Build();

        _topicsUpdatedSubscription = _hubConnection.On<IReadOnlyList<TopicDto>>("TopicsUpdated", async topics =>
        {
            // 对比旧列表，检测非活动主题是否有新消息（LastMessageAt 更新）
            var previousLastMessageAt = _topics.ToDictionary(t => t.Id, t => t.LastMessageAt);

            _topics.Clear();
            _topics.AddRange(topics);

            foreach (var topic in _topics)
            {
                if (topic.Id == _selectedTopicId) continue;
                if (!topic.LastMessageAt.HasValue) continue;

                var hadPrevious = previousLastMessageAt.TryGetValue(topic.Id, out var prev);
                // 若是新主题或消息时间更新，则标记为未读
                if (!hadPrevious || prev is null || topic.LastMessageAt > prev)
                {
                    _unreadTopicIds.Add(topic.Id);
                }
            }

            // 若已归档下拉列表正在显示，也同步刷新已归档主题列表
            if (_showArchivedDropdown)
            {
                try
                {
                    await LoadArchivedTopicsAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to refresh archived topics list on TopicsUpdated.");
                }
            }

            await InvokeAsync(StateHasChanged);
        });

        try
        {
            await _hubConnection.StartAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to start topic sidebar hub connection.");
        }
    }

    private void OpenSettings() => NavigationManager.NavigateTo("/settings");

    private Task HandleSelectedTopicChanged()
    {
        return InvokeAsync(() =>
        {
            _selectedTopicId = TopicStateService.SelectedTopicId;
            if (_selectedTopicId.HasValue)
                _unreadTopicIds.Remove(_selectedTopicId.Value);
            StateHasChanged();
        });
    }

    private Task HandleTopicsChanged()
    {
        return InvokeAsync(async () =>
        {
            await LoadTopicsAsync();
            if (_showArchivedDropdown)
            {
                await LoadArchivedTopicsAsync();
            }

            StateHasChanged();
        });
    }

    private async Task LoadArchivedTopicsAsync()
    {
        _archivedTopics.Clear();
        _archivedTopics.AddRange(await TopicService.GetArchivedTopicsAsync());
    }

    private async Task LogoutAsync()
    {
        try
        {
            await JS.InvokeAsync<object>("authInterop.postJson", "/api/v1/auth/logout", new { });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Logout request failed; navigating to login page anyway.");
        }

        NavigationManager.NavigateTo("/login", forceLoad: true);
    }

    public async ValueTask DisposeAsync()
    {
        TopicStateService.SelectedTopicChanged -= HandleSelectedTopicChanged;
        TopicStateService.TopicsChanged -= HandleTopicsChanged;
        _topicsUpdatedSubscription?.Dispose();

        if (_topics.Count > 0)
        {
            try
            {
                await JS.InvokeVoidAsync("destroySortable", "topic-list");
            }
            catch
            {
                // Ignore disposal errors.
            }
        }

        _dotNetRef?.Dispose();

        if (_hubConnection is not null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
        }
    }
}
