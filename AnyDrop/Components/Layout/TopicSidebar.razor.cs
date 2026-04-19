using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
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

    [Parameter] public EventCallback<Guid?> OnTopicSelected { get; set; }

    private readonly List<TopicDto> _topics = [];
    private HubConnection? _hubConnection;
    private IDisposable? _topicsUpdatedSubscription;
    private DotNetObjectReference<TopicSidebar>? _dotNetRef;
    private Guid? _selectedTopicId;

    // 排序错误提示
    private string? _error;

    // Modal 状态
    private bool _showCreateModal;
    private string _newTopicName = string.Empty;
    private string? _modalError;
    private ElementReference _modalInputRef;

    protected override async Task OnInitializedAsync()
    {
        await LoadTopicsAsync();
        await InitializeHubAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
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

    private void OpenCreateModal()
    {
        _newTopicName = string.Empty;
        _modalError = null;
        _showCreateModal = true;
    }

    private void CloseCreateModal()
    {
        _showCreateModal = false;
        _newTopicName = string.Empty;
        _modalError = null;
    }

    private async Task CreateTopicAsync()
    {
        _modalError = null;

        var normalizedName = _newTopicName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 100)
        {
            _modalError = "主题名称不能为空，且不超过 100 个字符";
            return;
        }

        try
        {
            var topic = await TopicService.CreateTopicAsync(new CreateTopicRequest(normalizedName));
            _newTopicName = string.Empty;
            _showCreateModal = false;
            await LoadTopicsAsync();
            _selectedTopicId = topic.Id;
            await OnTopicSelected.InvokeAsync(_selectedTopicId);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create topic");
            _modalError = "创建主题失败";
        }
    }

    private async Task SelectTopicAsync(Guid topicId)
    {
        _selectedTopicId = topicId;
        await OnTopicSelected.InvokeAsync(topicId);
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
        }

        // 若尚未选中任何主题，优先选中内置默认主题，否则选第一个
        if (!_selectedTopicId.HasValue && _topics.Count > 0)
        {
            var defaultTopic = _topics.FirstOrDefault(t => t.IsBuiltIn) ?? _topics[0];
            _selectedTopicId = defaultTopic.Id;
            await OnTopicSelected.InvokeAsync(_selectedTopicId);
        }
    }

    private async Task InitializeHubAsync()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/share"))
            .WithAutomaticReconnect()
            .Build();

        _topicsUpdatedSubscription = _hubConnection.On<IReadOnlyList<TopicDto>>("TopicsUpdated", topics =>
        {
            _topics.Clear();
            _topics.AddRange(topics);
            return InvokeAsync(StateHasChanged);
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

    public async ValueTask DisposeAsync()
    {
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

