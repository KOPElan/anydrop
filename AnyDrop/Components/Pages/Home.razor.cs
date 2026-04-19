using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace AnyDrop.Components.Pages;

public partial class Home : IAsyncDisposable
{
    private const long DefaultMaxFileSizeBytes = 100L * 1024 * 1024;

    [Inject] public required IShareService ShareService { get; set; }
    [Inject] public required ITopicService TopicService { get; set; }
    [Inject] public required IConfiguration Configuration { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required ILogger<Home> Logger { get; set; }
    [Inject] public required IJSRuntime JS { get; set; }
    [CascadingParameter] public Guid? SelectedTopicId { get; set; }

    private readonly List<ShareItemDto> _messages = [];
    // O(1) 消息去重：避免 SignalR 推送与主动刷新产生重复条目
    private readonly HashSet<Guid> _messageIds = [];
    private HubConnection? _hubConnection;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private string _inputText = string.Empty;
    private string? _validationError;
    private bool _isSending;
    private bool _isDragging;
    private Guid? _selectedTopicId;
    private string? _selectedTopicName;
    private bool _selectedTopicPinned;

    private ElementReference _chatSectionRef;
    private ElementReference _messageListRef;
    private DotNetObjectReference<Home>? _dotNetRef;
    private bool _shouldScrollToBottom;

    protected override async Task OnInitializedAsync()
    {
        await LoadSelectedTopicMessagesAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (SelectedTopicId != _selectedTopicId)
        {
            _selectedTopicId = SelectedTopicId;
            await LoadSelectedTopicMessagesAsync();
            await LoadSelectedTopicMetaAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // 设置聊天区域拖放处理（JS 端负责消抖，仅在状态变化时回调 .NET）
            _dotNetRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("AnyDropInterop.setupDropZone", _chatSectionRef, _dotNetRef);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to initialize drop zone JS interop.");
            }

            // 启动 SignalR 连接
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/share"))
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ShareItemDto>("ReceiveShareItem", dto =>
            {
                return InvokeAsync(async () =>
                {
                    // 只处理当前主题的消息，且 O(1) 去重，避免与主动刷新产生重复
                    if (_selectedTopicId.HasValue
                        && dto.TopicId == _selectedTopicId
                        && _messageIds.Add(dto.Id))
                    {
                        _messages.Add(dto); // 追加到末尾，保持时间升序（最新在底）
                        _shouldScrollToBottom = true;
                        StateHasChanged();
                    }
                });
            });

            try
            {
                await _hubConnection.StartAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to start ShareHub connection. Falling back to polling.");
                _pollingCts = new CancellationTokenSource();
                _pollingTask = StartPollingAsync(_pollingCts.Token);
            }
        }

        if (_shouldScrollToBottom)
        {
            _shouldScrollToBottom = false;
            try
            {
                await JS.InvokeVoidAsync("AnyDropInterop.scrollToBottom", _messageListRef);
            }
            catch (JSDisconnectedException)
            {
                Logger.LogDebug("JS interop disconnected during scrollToBottom — component is being disposed.");
            }
            catch (TaskCanceledException)
            {
                Logger.LogDebug("scrollToBottom was cancelled — component is being disposed.");
            }
        }
    }

    private async Task SendAsync()
    {
        _validationError = null;

        var trimmedText = _inputText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            _validationError = "消息内容不能为空。";
            return;
        }

        if (!_selectedTopicId.HasValue)
        {
            _validationError = "请先选择主题。";
            return;
        }

        if (trimmedText.Length > 10_000)
        {
            _validationError = "消息不能超过 10,000 个字符。";
            return;
        }

        _isSending = true;
        try
        {
            await ShareService.SendTextAsync(trimmedText, _selectedTopicId);
            _inputText = string.Empty;
            // 发送后主动刷新一次，保障在 SignalR 降级（轮询）场景下也能立即显示
            await LoadSelectedTopicMessagesAsync();
        }
        finally
        {
            _isSending = false;
        }
    }

    private async Task TogglePinAsync()
    {
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        await TopicService.PinTopicAsync(_selectedTopicId.Value, !_selectedTopicPinned);
        await LoadSelectedTopicMetaAsync();
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.CtrlKey && string.Equals(e.Key, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            await SendAsync();
        }
    }

    private async Task OnFilesSelected(InputFileChangeEventArgs args)
    {
        if (!_selectedTopicId.HasValue)
        {
            _validationError = "请先选择主题。";
            return;
        }

        _validationError = null;

        foreach (var file in args.GetMultipleFiles())
        {
            var maxFileSize = Configuration.GetValue<long?>("Storage:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;
            await using var stream = file.OpenReadStream(maxAllowedSize: maxFileSize);
            await ShareService.SendFileAsync(stream, file.Name, file.ContentType, _selectedTopicId.Value);
        }

        // 主动刷新，确保在 SignalR 降级场景下也能立即呈现
        await LoadSelectedTopicMessagesAsync();
    }

    /// <summary>
    /// 文件拖放到聊天区域时触发（由覆盖层 InputFile 处理）。
    /// 关闭拖放覆盖层后转交给 OnFilesSelected 处理。
    /// </summary>
    private async Task OnFilesDropped(InputFileChangeEventArgs args)
    {
        _isDragging = false;
        await OnFilesSelected(args);
    }

    /// <summary>
    /// 由 JS 拖放处理逻辑回调，通知 .NET 端切换拖放覆盖层显示状态。
    /// </summary>
    [JSInvokable]
    public Task SetDragging(bool isDragging)
    {
        _isDragging = isDragging;
        return InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        // 清理 JS 拖放事件监听器，防止内存泄漏
        try
        {
            await JS.InvokeVoidAsync("AnyDropInterop.cleanupDropZone", _chatSectionRef);
        }
        catch (JSDisconnectedException)
        {
            Logger.LogDebug("JS interop disconnected during cleanupDropZone — component is being disposed.");
        }
        catch (TaskCanceledException)
        {
            Logger.LogDebug("cleanupDropZone was cancelled — component is being disposed.");
        }

        _dotNetRef?.Dispose();

        _pollingCts?.Cancel();
        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        if (_hubConnection is null)
        {
            return;
        }

        await _hubConnection.StopAsync();
        await _hubConnection.DisposeAsync();
    }

    private async Task StartPollingAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            if (_selectedTopicId.HasValue)
            {
                await LoadSelectedTopicMessagesAsync(ct);
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task LoadSelectedTopicMessagesAsync(CancellationToken ct = default)
    {
        _messages.Clear();
        _messageIds.Clear();
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        var response = await TopicService.GetTopicMessagesAsync(_selectedTopicId.Value, 50, null, ct);
        if (response is null)
        {
            _selectedTopicId = null;
            return;
        }

        // 服务端返回最新 N 条（降序），逆序后使列表保持时间升序（最旧在顶，最新在底）
        foreach (var msg in response.Messages.Reverse())
        {
            if (_messageIds.Add(msg.Id))
            {
                _messages.Add(msg);
            }
        }

        _shouldScrollToBottom = true;
    }

    private async Task LoadSelectedTopicMetaAsync(CancellationToken ct = default)
    {
        _selectedTopicName = null;
        _selectedTopicPinned = false;
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        var topics = await TopicService.GetAllTopicsAsync(ct);
        var topic = topics.FirstOrDefault(t => t.Id == _selectedTopicId.Value);
        _selectedTopicName = topic?.Name;
        _selectedTopicPinned = topic?.IsPinned == true;
    }

    private static string GetFileUrl(Guid itemId, bool download = false)
        => download
            ? $"/api/v1/share-items/{itemId}/file?download=true"
            : $"/api/v1/share-items/{itemId}/file";

    /// <summary>将字节数格式化为人类可读的大小字符串。</summary>
    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }
}
