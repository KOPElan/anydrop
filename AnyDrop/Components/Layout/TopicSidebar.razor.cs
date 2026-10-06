using AnyDrop.Models;
using AnyDrop.Resources;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
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
    [Inject] public required IUserService UserService { get; set; }
    [Inject] public required ITokenService TokenService { get; set; }
    [Inject] public required IStringLocalizer<SharedStrings> L { get; set; }

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
    private string _nickname = string.Empty;

    // 未读通知：记录收到新消息但未查看的主题 ID
    private readonly HashSet<Guid> _unreadTopicIds = [];

    protected override async Task OnInitializedAsync()
    {
        TopicStateService.SelectedTopicChanged += HandleSelectedTopicChanged;
        TopicStateService.TopicsChanged += HandleTopicsChanged;

        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        _nickname = state.User.FindFirst("nickname")?.Value ?? state.User.Identity?.Name ?? L["Sidebar_DefaultUser"];
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

        var byId = _topics.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
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
            _error = L["Sidebar_ReorderFailedRollback"];
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadTopicsAsync()
    {
        // 先取数据再整体替换：不在 Clear 与 AddRange 之间 await，
        // 否则并发的推送可能插入到中间，导致列表出现重复主题。
        var loaded = await TopicService.GetAllTopicsAsync();
        ReplaceTopics(loaded);

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

    /// <summary>
    /// 用给定列表整体替换当前主题列表，并按 Id 去重。
    ///
    /// 去重不是可有可无的防御：主题按钮使用 <c>@key="topic.Id"</c>，
    /// 一旦出现重复 Id，Blazor 会在 diff 时抛「same key value」并**终止整条线路**，
    /// 页面从此不再响应任何交互。
    /// </summary>
    private void ReplaceTopics(IEnumerable<TopicDto> topics)
    {
        var deduplicated = topics
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .ToList();

        _topics.Clear();
        _topics.AddRange(deduplicated);
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
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/share"), options =>
            {
                options.AccessTokenProvider = ResolveHubAccessTokenAsync;
            })
            .WithAutomaticReconnect()
            .Build();

        _topicsUpdatedSubscription = _hubConnection.On<IReadOnlyList<TopicDto>>("TopicsUpdated", async topics =>
        {
            // 必须通过 InvokeAsync 回到渲染器的调度线程再改状态。
            // 直接在 SignalR 回调线程上修改 _topics，会与调度线程上的
            // LoadTopicsAsync / HandleTopicsChanged 并发，使列表出现重复主题——
            // 表现为 @key 重复，Blazor 会直接抛异常终止整条线路，页面随即失去响应。
            await InvokeAsync(async () =>
            {
                // 对比旧列表，检测非活动主题是否有新消息（LastMessageAt 更新）
                var previousLastMessageAt = _topics
                    .GroupBy(t => t.Id)
                    .ToDictionary(g => g.Key, g => g.First().LastMessageAt);

                ReplaceTopics(topics);

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

                StateHasChanged();
            });
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
        // 同样先取数据再替换，避免 Clear 与 AddRange 之间被并发写入
        var loaded = await TopicService.GetArchivedTopicsAsync();
        _archivedTopics.Clear();
        _archivedTopics.AddRange(loaded.GroupBy(t => t.Id).Select(g => g.First()));
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

    /// <summary>
    /// 为服务器端 HubConnection 提供 JWT，避免协商阶段因无浏览器 Cookie 身份导致连接失败。
    /// </summary>
    private async Task<string?> ResolveHubAccessTokenAsync()
    {
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var principal = state.User;
            if (principal.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var subject = principal.FindFirst("sub")?.Value
                          ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(subject, out var userId))
            {
                return null;
            }

            var user = await UserService.GetByIdAsync(userId);
            if (user is null)
            {
                return null;
            }

            var (accessToken, _) = TokenService.GenerateToken(user);
            return accessToken;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to resolve ShareHub access token in TopicSidebar.");
            return null;
        }
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
