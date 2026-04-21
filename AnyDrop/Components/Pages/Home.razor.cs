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
    private bool _selectedTopicArchived;
    private bool _selectedTopicIsBuiltIn;
    private int _selectedTopicMessageCount;

    // 主题设置 Modal 状态
    private bool _showTopicSettingsModal;
    private string _topicSettingsName = string.Empty;
    private string _topicSettingsIcon = "chat_bubble";
    private string? _topicSettingsError;
    private ElementReference _topicSettingsInputRef;

    // 可选图标列表
    private readonly string[] _availableIcons =
    [
        "chat_bubble", "bookmark", "work", "home", "favorite",
        "school", "sports_esports", "code", "music_note", "restaurant",
        "flight", "local_cafe", "shopping_cart", "cake", "pets",
        "directions_car", "movie", "explore", "star", "lightbulb",
        "beach_access", "fitness_center", "palette", "public", "emoji_events"
    ];

    // 图片大图预览 Modal
    private string? _previewImageUrl;

    // 阅后即焚模式
    private bool _burnAfterReading;

    // 从搜索页返回时，待高亮的消息 ID
    private Guid? _pendingHighlightId;
    private bool _shouldHighlight;

    private ElementReference _chatSectionRef;
    private ElementReference _messageListRef;
    private DotNetObjectReference<Home>? _dotNetRef;
    // 强制滚到底（初次加载、主题切换、手动发消息）
    private bool _shouldScrollToBottom;
    // 条件滚到底（收到新消息时，仅当用户处于底部附近才滚动）
    private bool _shouldScrollIfNearBottom;

    protected override async Task OnInitializedAsync()
    {
        // 读取 highlight 查询参数（从搜索页跳转过来时定位消息）
        var uri = new Uri(NavigationManager.Uri);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        if (query.TryGetValue("highlight", out var h) && Guid.TryParse(h, out var gid))
        {
            _pendingHighlightId = gid;
        }

        await LoadSelectedTopicMessagesAsync();

        // 若有待高亮消息，不滚到底部，改为滚到目标消息
        if (_pendingHighlightId.HasValue)
        {
            _shouldScrollToBottom = false;
            _shouldHighlight = true;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (SelectedTopicId != _selectedTopicId)
        {
            _selectedTopicId = SelectedTopicId;
            await LoadSelectedTopicMessagesAsync();
            await LoadSelectedTopicMetaAsync();

            // 若有待高亮消息（从搜索页跳转而来），撤销自动滚到底的请求，保留高亮定位
            if (_shouldHighlight)
            {
                _shouldScrollToBottom = false;
            }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // ── 优先处理滚动/高亮（在 hub 初始化前执行，消除初次打开时先显示顶部再跳底的闪烁）──

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
            catch (ObjectDisposedException ex)
            {
                Logger.LogDebug(ex, "JS runtime disposed during scrollToBottom.");
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogDebug(ex, "JS interop not available during scrollToBottom.");
            }
        }

        if (_shouldScrollIfNearBottom)
        {
            _shouldScrollIfNearBottom = false;
            try
            {
                await JS.InvokeVoidAsync("AnyDropInterop.scrollToBottomIfNearBottom", _messageListRef);
            }
            catch (JSDisconnectedException)
            {
                Logger.LogDebug("JS interop disconnected during scrollToBottomIfNearBottom — component is being disposed.");
            }
            catch (TaskCanceledException)
            {
                Logger.LogDebug("scrollToBottomIfNearBottom was cancelled — component is being disposed.");
            }
            catch (ObjectDisposedException ex)
            {
                Logger.LogDebug(ex, "JS runtime disposed during scrollToBottomIfNearBottom.");
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogDebug(ex, "JS interop not available during scrollToBottomIfNearBottom.");
            }
        }

        // 从搜索页跳转回来时，滚动并高亮目标消息
        if (_shouldHighlight && _pendingHighlightId.HasValue)
        {
            _shouldHighlight = false;
            var targetId = _pendingHighlightId.Value;
            _pendingHighlightId = null;
            try
            {
                await JS.InvokeVoidAsync("AnyDropInterop.scrollToMessage", targetId.ToString());
            }
            catch (JSDisconnectedException)
            {
                Logger.LogDebug("JS interop disconnected during scrollToMessage.");
            }
            catch (TaskCanceledException)
            {
                Logger.LogDebug("scrollToMessage was cancelled.");
            }
            catch (ObjectDisposedException ex)
            {
                Logger.LogDebug(ex, "JS runtime disposed during scrollToMessage.");
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogDebug(ex, "JS interop not available during scrollToMessage.");
            }
        }

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
                return InvokeAsync(() =>
                {
                    // 只处理当前主题的消息
                    if (!_selectedTopicId.HasValue || dto.TopicId != _selectedTopicId)
                        return;

                    // 若消息已存在（如链接元数据后台更新），就地替换气泡内容
                    var existingIndex = _messages.FindIndex(m => m.Id == dto.Id);
                    if (existingIndex >= 0)
                    {
                        _messages[existingIndex] = dto;
                        StateHasChanged();
                        return;
                    }

                    // 新消息：O(1) 去重后追加到末尾，保持时间升序
                    // 仅当用户处于底部附近时才触发自动滚动（避免打断用户翻阅历史记录）
                    if (_messageIds.Add(dto.Id))
                    {
                        _messages.Add(dto);
                        _shouldScrollIfNearBottom = true;
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
            await ShareService.SendTextAsync(trimmedText, _selectedTopicId, burnAfterReading: _burnAfterReading);
            _inputText = string.Empty;
            // 发送后主动刷新一次，保障在 SignalR 降级（轮询）场景下也能立即显示
            await LoadSelectedTopicMessagesAsync();
        }
        finally
        {
            _isSending = false;
        }
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.CtrlKey && string.Equals(e.Key, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            await SendAsync();
        }
    }

    /// <summary>打开主题设置 Modal。</summary>
    private void OpenTopicSettingsModal()
    {
        _topicSettingsName = _selectedTopicName ?? string.Empty;
        _topicSettingsIcon = "chat_bubble"; // Default icon, could be loaded from topic metadata
        _topicSettingsError = null;
        _showTopicSettingsModal = true;
    }

    /// <summary>选择图标。</summary>
    private void SelectIcon(string icon)
    {
        _topicSettingsIcon = icon;
    }

    /// <summary>保存主题图标。</summary>
    private async Task SaveTopicIconAsync()
    {
        // TODO: Implement icon saving to Topic model when Icon field is added
        // For now, just show a success message
        _topicSettingsError = null;
        await Task.CompletedTask;
        // 图标保存功能需要扩展 Topic 模型和数据库架构
    }

    /// <summary>切换当前主题的置顶状态。</summary>
    private async Task TogglePinCurrentTopicAsync()
    {
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        var pinning = !_selectedTopicPinned;

        try
        {
            await TopicService.PinTopicAsync(_selectedTopicId.Value, pinning);
            _selectedTopicPinned = pinning;
            await LoadSelectedTopicMetaAsync();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to pin/unpin topic {TopicId}", _selectedTopicId);
            _topicSettingsError = "操作失败，请重试。";
        }
    }

    /// <summary>关闭主题设置 Modal。</summary>
    private void CloseTopicSettingsModal()
    {
        _showTopicSettingsModal = false;
        _topicSettingsError = null;
    }

    /// <summary>保存主题名称更改。</summary>
    private async Task SaveTopicNameAsync()
    {
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        var name = _topicSettingsName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _topicSettingsError = "主题名称不能为空。";
            return;
        }

        _topicSettingsError = null;
        try
        {
            await TopicService.UpdateTopicAsync(_selectedTopicId.Value, new UpdateTopicRequest(name));
            await LoadSelectedTopicMetaAsync();
            _topicSettingsName = _selectedTopicName ?? name;
            _showTopicSettingsModal = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to rename topic {TopicId}", _selectedTopicId);
            _topicSettingsError = "保存失败，请重试。";
        }
    }

    /// <summary>归档或取消归档当前主题。</summary>
    private async Task ArchiveCurrentTopicAsync()
    {
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        var archiving = !_selectedTopicArchived;

        try
        {
            await TopicService.ArchiveTopicAsync(_selectedTopicId.Value, archiving);
            CloseTopicSettingsModal();

            // 归档后主题从普通列表消失，清空聊天区
            if (archiving)
            {
                _selectedTopicId = null;
                _selectedTopicName = null;
                _selectedTopicPinned = false;
                _selectedTopicArchived = false;
                _selectedTopicIsBuiltIn = false;
                _selectedTopicMessageCount = 0;
                _messages.Clear();
                _messageIds.Clear();
            }
            else
            {
                // 取消归档：刷新元数据，主题重新出现在普通列表
                await LoadSelectedTopicMetaAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to archive topic {TopicId}", _selectedTopicId);
            _topicSettingsError = "操作失败，请重试。";
        }
    }

    /// <summary>删除当前主题（仅限无内容时）。</summary>
    private async Task DeleteCurrentTopicAsync()
    {
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        try
        {
            await TopicService.DeleteTopicAsync(_selectedTopicId.Value);
            CloseTopicSettingsModal();

            _selectedTopicId = null;
            _selectedTopicName = null;
            _messages.Clear();
            _messageIds.Clear();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete topic {TopicId}", _selectedTopicId);
            _topicSettingsError = "删除失败，请重试。";
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
            try
            {
                var maxFileSize = Configuration.GetValue<long?>("Storage:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;
                await using var stream = file.OpenReadStream(maxAllowedSize: maxFileSize);
                await ShareService.SendFileAsync(stream, file.Name, file.ContentType, _selectedTopicId.Value,
                    burnAfterReading: _burnAfterReading);
            }
            catch (IOException ex) when (ex.Message.Contains("exceeded", StringComparison.OrdinalIgnoreCase))
            {
                _validationError = $"文件\"{file.Name}\"超过最大允许大小，已跳过。";
                StateHasChanged();
            }
            catch (Exception)
            {
                _validationError = $"文件\"{file.Name}\"上传失败，请重试。";
                StateHasChanged();
            }
        }

        // 主动刷新，确保在 SignalR 降级场景下也能立即呈现
        await LoadSelectedTopicMessagesAsync();
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

    /// <summary>
    /// 由 JS 拖放事件回调：将拖入的文件通过 IJSStreamReference 流式发送，不触发文件对话框。
    /// </summary>
    [JSInvokable]
    public async Task ReceiveDroppedFile(string fileName, string mimeType, long fileSize, IJSStreamReference streamRef)
    {
        if (!_selectedTopicId.HasValue)
        {
            _validationError = "请先选择主题。";
            await InvokeAsync(StateHasChanged);
            return;
        }

        _validationError = null;
        _isSending = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var maxFileSize = Configuration.GetValue<long?>("Storage:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;
            var safeMimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;

            await using var stream = await streamRef.OpenReadStreamAsync(maxAllowedSize: maxFileSize);
            await ShareService.SendFileAsync(stream, fileName, safeMimeType, _selectedTopicId.Value,
                knownFileSize: fileSize, burnAfterReading: _burnAfterReading);

            await LoadSelectedTopicMessagesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to receive dropped file {FileName}", fileName);
            _validationError = "文件上传失败，请重试。";
        }
        finally
        {
            _isSending = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>切换阅后即焚模式。</summary>
    private void ToggleBurnAfterReading()
    {
        _burnAfterReading = !_burnAfterReading;
    }

    /// <summary>打开图片大图预览 Modal。</summary>
    private void OpenImagePreview(string url)
    {
        _previewImageUrl = url;
    }

    /// <summary>关闭图片大图预览 Modal。</summary>
    private void CloseImagePreview()
    {
        _previewImageUrl = null;
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
        catch (ObjectDisposedException ex)
        {
            Logger.LogDebug(ex, "JS runtime disposed during cleanupDropZone.");
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogDebug(ex, "JS interop not available during cleanupDropZone.");
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
        _selectedTopicArchived = false;
        _selectedTopicIsBuiltIn = false;
        _selectedTopicMessageCount = 0;
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        // 先从普通列表中查找；找不到再从归档列表查找。
        // 两次调用已分别走各自的 DB 查询过滤，分布在不同场景的结果集较小。
        var allTopics = await TopicService.GetAllTopicsAsync(ct);
        var topic = allTopics.FirstOrDefault(t => t.Id == _selectedTopicId.Value);

        if (topic is null)
        {
            var archivedTopics = await TopicService.GetArchivedTopicsAsync(ct);
            topic = archivedTopics.FirstOrDefault(t => t.Id == _selectedTopicId.Value);
        }

        _selectedTopicName = topic?.Name;
        _selectedTopicPinned = topic?.IsPinned == true;
        _selectedTopicArchived = topic?.IsArchived == true;
        _selectedTopicIsBuiltIn = topic?.IsBuiltIn == true;
        _selectedTopicMessageCount = topic?.MessageCount ?? 0;
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

    /// <summary>将消息时间格式化为「日期 + 时间」字符串。</summary>
    private static string FormatMessageTime(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        return local.ToString("yyyy/MM/dd HH:mm");
    }

    /// <summary>将阅后即焚到期时间格式化为倒计时或已到期标记。</summary>
    private static string FormatExpiry(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return "即将删除";
        }

        return remaining.TotalMinutes >= 1
            ? $"{(int)remaining.TotalMinutes}分钟后删除"
            : $"{(int)remaining.TotalSeconds}秒后删除";
    }
}

