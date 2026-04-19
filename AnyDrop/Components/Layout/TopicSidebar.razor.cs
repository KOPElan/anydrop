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
    private string _newTopicName = string.Empty;
    private string? _error;
    private bool _sortableInitialized;

    protected override async Task OnInitializedAsync()
    {
        await LoadTopicsAsync();
        await InitializeHubAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_topics.Count == 0 || _sortableInitialized)
        {
            return;
        }

        _dotNetRef ??= DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("initSortable", "topic-list", _dotNetRef);
        _sortableInitialized = true;
    }

    private async Task CreateTopicAsync()
    {
        _error = null;

        var normalizedName = _newTopicName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 100)
        {
            _error = "主题名称不能为空，且不超过 100 个字符";
            return;
        }

        try
        {
            var topic = await TopicService.CreateTopicAsync(new CreateTopicRequest(normalizedName));
            _newTopicName = string.Empty;
            await LoadTopicsAsync();
            _selectedTopicId = topic.Id;
            await OnTopicSelected.InvokeAsync(_selectedTopicId);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create topic");
            _error = "创建主题失败";
        }
    }

    private async Task SelectTopicAsync(Guid topicId)
    {
        _selectedTopicId = topicId;
        await OnTopicSelected.InvokeAsync(topicId);
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnSortEnd(Guid[] orderedIds)
    {
        if (orderedIds.Length == 0)
        {
            return;
        }

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
            await OnTopicSelected.InvokeAsync(null);
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

        if (_sortableInitialized)
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
