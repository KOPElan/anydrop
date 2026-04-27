using AnyDrop.Models;
using AnyDrop.Resources;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace AnyDrop.Components.Pages;

public partial class Home : IAsyncDisposable
{
    private const long DefaultMaxFileSizeBytes = 100L * 1024 * 1024;
    private const int HubInitialDelayMs = 300;
    private const int HubRetryDelayMs = 500;
    private const int MaxHubConnectionAttempts = 2;

    [Inject] public required IShareService ShareService { get; set; }
    [Inject] public required ITopicService TopicService { get; set; }
    [Inject] public required IConfiguration Configuration { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required ILogger<Home> Logger { get; set; }
    [Inject] public required IJSRuntime JS { get; set; }
    [Inject] public required ITopicStateService TopicStateService { get; set; }
    [Inject] public required IStringLocalizer<SharedStrings> L { get; set; }
    [CascadingParameter] public Guid? SelectedTopicId { get; set; }
    [CascadingParameter(Name = "ToggleMobileSidebar")] public Action? ToggleMobileSidebar { get; set; }

    private readonly List<ShareItemDto> _messages = [];
    // O(1) 消息去重：避免 SignalR 推送与主动刷新产生重复条目
    private readonly HashSet<Guid> _messageIds = [];
    // 待上传占位条目列表
    private readonly List<PendingUpload> _pendingUploads = [];
    private HubConnection? _hubConnection;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private string _inputText = string.Empty;
    private string? _validationError;
    private bool _isSending;
    private bool _isDragging;
    private Guid? _selectedTopicId;
    private string? _selectedTopicName;
    private string _selectedTopicIcon = "chat_bubble";
    private bool _selectedTopicPinned;
    private bool _selectedTopicArchived;
    private bool _selectedTopicIsBuiltIn;
    private int _selectedTopicMessageCount;

    // 浏览器时区（IANA），在首次渲染后从 JS 获取
    private string _browserTimeZoneId = "UTC";
    private TimeZoneInfo _displayTimeZone = TimeZoneInfo.Utc;

    // 删除确认 Modal 状态
    private bool _showDeleteConfirmModal;

    // 主题信息 Modal 状态（仅名称和图标）
    private bool _showTopicSettingsModal;
    // _topicSettingsName / _topicSettingsIcon 作为 InitialName/InitialIcon 传入子组件，子组件内部管理编辑状态
    private string _topicSettingsName = string.Empty;
    private string _topicSettingsIcon = "chat_bubble";
    private string? _topicSettingsError;

    // 顶部操作下拉菜单状态
    private bool _showTopicMenu;

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
            // 获取浏览器时区，用于正确显示消息时间
            try
            {
                _browserTimeZoneId = await JS.InvokeAsync<string>("AnyDropInterop.getBrowserTimeZone");
                try { _displayTimeZone = TimeZoneInfo.FindSystemTimeZoneById(_browserTimeZoneId); }
                catch { _displayTimeZone = TimeZoneInfo.Utc; }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to get browser timezone, falling back to UTC.");
            }

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

                    // 若当前消息是由本端上传完成的占位条目，移除占位并将正式消息加入列表（原位无闪烁过渡）
                    var completedPending = _pendingUploads.Find(p => p.IsCompleted && p.CompletedDto?.Id == dto.Id);
                    if (completedPending is not null)
                    {
                        _pendingUploads.Remove(completedPending);
                    }

                    // 新消息：O(1) 去重后追加到末尾，保持时间升序
                    // 仅当用户处于底部附近时才触发自动滚动（避免打断用户翻阅历史记录）
                    if (_messageIds.Add(dto.Id))
                    {
                        _messages.Add(dto);
                        _shouldScrollIfNearBottom = true;
                        StateHasChanged();
                    }
                    else if (completedPending is not null)
                    {
                        // ID 已在 _messageIds（上传时预注册），直接加入并刷新
                        _messages.Add(dto);
                        _shouldScrollIfNearBottom = true;
                        StateHasChanged();
                    }
                });
            });

            // 短暂延迟，等待 Blazor 电路稳定后再连接 SignalR Hub，减少导航后的瞬时协商错误
            await Task.Delay(HubInitialDelayMs);
            var hubStarted = false;
            for (var attempt = 0; attempt < MaxHubConnectionAttempts && !hubStarted; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        await Task.Delay(HubRetryDelayMs);
                    }

                    await _hubConnection.StartAsync();
                    hubStarted = true;
                }
                catch (Exception ex) when (attempt < MaxHubConnectionAttempts - 1)
                {
                    Logger.LogDebug(ex, "ShareHub connection attempt {Attempt} failed, retrying...", attempt + 1);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to start ShareHub connection. Falling back to polling.");
                    _pollingCts = new CancellationTokenSource();
                    _pollingTask = StartPollingAsync(_pollingCts.Token);
                }
            }
        }
    }

    private async Task SendAsync()
    {
        _validationError = null;

        var trimmedText = _inputText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            _validationError = L["Home_MessageRequired"];
            return;
        }

        if (!_selectedTopicId.HasValue)
        {
            _validationError = L["Home_SelectTopicFirst"];
            return;
        }

        if (trimmedText.Length > 10_000)
        {
            _validationError = L["Home_MessageTooLong"];
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

    /// <summary>打开主题信息 Modal（仅名称和图标）。</summary>
    private void OpenTopicInfoModal()
    {
        _topicSettingsName = _selectedTopicName ?? string.Empty;
        _topicSettingsIcon = _selectedTopicIcon;
        _topicSettingsError = null;
        _showTopicSettingsModal = true;
        _showTopicMenu = false;
    }

    /// <summary>切换顶部操作下拉菜单。</summary>
    private void ToggleTopicMenu()
    {
        _showTopicMenu = !_showTopicMenu;
    }

    /// <summary>关闭顶部操作下拉菜单。</summary>
    private void CloseTopicMenu()
    {
        _showTopicMenu = false;
    }

    /// <summary>
    /// 子组件 TopicSettingsModal 保存回调：接收编辑后的名称和图标，依次调用 API。
    /// 使用 ValueTuple 参数与 TopicSettingsModal.SaveArgs 兼容。
    /// </summary>
    private async Task HandleTopicInfoSave(TopicSettingsModal.SaveArgs args)
    {
        if (!_selectedTopicId.HasValue) return;
        _topicSettingsError = null;

        var (newName, newIcon) = (args.Name, args.Icon);

        // 保存图标（如有变更）
        if (!string.IsNullOrEmpty(newIcon) && newIcon != _selectedTopicIcon)
        {
            try
            {
                var result = await TopicService.UpdateTopicIconAsync(_selectedTopicId.Value, new UpdateTopicIconRequest(newIcon));
                if (result is not null)
                {
                    _selectedTopicIcon = newIcon;
                    _topicSettingsIcon = newIcon;
                    await TopicStateService.NotifyTopicsChangedAsync();
                }
                else
                {
                    _topicSettingsError = L["Home_SaveIconTopicNotFound"];
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to save icon for topic {TopicId}", _selectedTopicId);
                _topicSettingsError = L["Home_SaveIconFailed"];
                return;
            }
        }

        // 保存名称（如有变更）
        var trimmedName = newName.Trim();
        if (!string.IsNullOrEmpty(trimmedName) && trimmedName != _selectedTopicName)
        {
            try
            {
                await TopicService.UpdateTopicAsync(_selectedTopicId.Value, new UpdateTopicRequest(trimmedName));
                await LoadSelectedTopicMetaAsync();
                _topicSettingsName = _selectedTopicName ?? trimmedName;
                await TopicStateService.NotifyTopicsChangedAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to rename topic {TopicId}", _selectedTopicId);
                _topicSettingsError = L["Home_SaveTopicNameFailed"];
                return;
            }
        }

        _showTopicSettingsModal = false;
    }

    /// <summary>切换当前主题的置顶状态。</summary>
    private async Task TogglePinCurrentTopicAsync()
    {
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        _showTopicMenu = false;
        var pinning = !_selectedTopicPinned;

        try
        {
            await TopicService.PinTopicAsync(_selectedTopicId.Value, pinning);
            _selectedTopicPinned = pinning;
            await LoadSelectedTopicMetaAsync();
            await TopicStateService.NotifyTopicsChangedAsync();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to pin/unpin topic {TopicId}", _selectedTopicId);
            _validationError = L["Home_PinFailed"];
        }
    }

    /// <summary>关闭主题设置 Modal。</summary>
    private void CloseTopicSettingsModal()
    {
        _showTopicSettingsModal = false;
        _topicSettingsError = null;
    }

    /// <summary>归档或取消归档当前主题。</summary>
    private async Task ArchiveCurrentTopicAsync()
    {
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        _showTopicMenu = false;
        var archiving = !_selectedTopicArchived;

        try
        {
            await TopicService.ArchiveTopicAsync(_selectedTopicId.Value, archiving);

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
                await TopicStateService.SetSelectedTopicAsync(null);
            }
            else
            {
                // 取消归档：刷新元数据，主题重新出现在普通列表
                await LoadSelectedTopicMetaAsync();
            }

            await TopicStateService.NotifyTopicsChangedAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to archive topic {TopicId}", _selectedTopicId);
            _validationError = L["Home_ArchiveFailed"];
        }
    }

    /// <summary>显示删除主题二次确认弹窗。</summary>
    private void ConfirmDeleteCurrentTopic()
    {
        _showTopicMenu = false;
        _showDeleteConfirmModal = true;
    }

    /// <summary>取消删除确认。</summary>
    private void CancelDeleteConfirm()
    {
        _showDeleteConfirmModal = false;
    }

    /// <summary>删除当前主题（仅限无内容时，用户确认后执行）。</summary>
    private async Task DeleteCurrentTopicAsync()
    {
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        _showDeleteConfirmModal = false;

        try
        {
            await TopicService.DeleteTopicAsync(_selectedTopicId.Value);

            _selectedTopicId = null;
            _selectedTopicName = null;
            _selectedTopicIcon = "chat_bubble";
            _messages.Clear();
            _messageIds.Clear();
            await TopicStateService.SetSelectedTopicAsync(null);
            await TopicStateService.NotifyTopicsChangedAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete topic {TopicId}", _selectedTopicId);
            _validationError = L["Home_DeleteFailed"];
        }
    }

    private async Task OnFilesSelected(InputFileChangeEventArgs args)
    {
        if (!_selectedTopicId.HasValue)
        {
            _validationError = L["Home_SelectTopicFirst"];
            return;
        }

        _validationError = null;

        var files = args.GetMultipleFiles().ToList();
        var maxFileSize = Configuration.GetValue<long?>("Storage:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;

        // 为每个文件立即创建占位条目并渲染，给用户即时反馈
        var pendingList = new List<(PendingUpload Pending, IBrowserFile File)>();
        foreach (var file in files)
        {
            var contentType = PendingUpload.ResolveContentType(file.ContentType);
            var pending = new PendingUpload(file.Name, file.ContentType, file.Size, contentType);
            _pendingUploads.Add(pending);
            pendingList.Add((pending, file));
        }

        // 显示占位符并请求滚动到底部
        _shouldScrollIfNearBottom = true;
        StateHasChanged();

        // 依次上传文件（占位符保持显示并更新进度）
        foreach (var (pending, file) in pendingList)
        {
            try
            {
                await using var rawStream = file.OpenReadStream(maxAllowedSize: maxFileSize);
                await using var progressStream = new ProgressStream(rawStream, file.Size, percent =>
                {
                    pending.ProgressPercent = percent;
                    _ = InvokeAsync(StateHasChanged);
                });

                var dto = await ShareService.SendFileAsync(progressStream, file.Name, file.ContentType,
                    _selectedTopicId.Value, knownFileSize: file.Size, burnAfterReading: _burnAfterReading);

                // 上传成功：将 ID 加入去重集合（防止 SignalR 重复），并在原气泡上标记完成状态
                // 不直接加入 _messages，等待 SignalR 推送或 polling 刷新时再原位替换，避免闪烁
                _messageIds.Add(dto.Id);
                pending.CompletedDto = dto;
                pending.IsCompleted = true;
                pending.ProgressPercent = 100;
                _shouldScrollIfNearBottom = true;
            }
            catch (IOException ex) when (ex.Message.Contains("exceeded", StringComparison.OrdinalIgnoreCase))
            {
                pending.IsFailed = true;
                _validationError = L["Home_FileTooLargeSkipped", file.Name];
            }
            catch (Exception)
            {
                pending.IsFailed = true;
                _validationError = L["Home_FileUploadFailed", file.Name];
            }

            StateHasChanged();
        }
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
            _validationError = L["Home_SelectTopicFirst"];
            await InvokeAsync(StateHasChanged);
            return;
        }

        _validationError = null;

        // 立即创建占位条目，给用户即时反馈
        var safeMimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;
        var contentType = PendingUpload.ResolveContentType(safeMimeType);
        var pending = new PendingUpload(fileName, safeMimeType, fileSize, contentType);
        _pendingUploads.Add(pending);
        _shouldScrollIfNearBottom = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            var maxFileSize = Configuration.GetValue<long?>("Storage:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;

            await using var rawStream = await streamRef.OpenReadStreamAsync(maxAllowedSize: maxFileSize);
            await using var progressStream = new ProgressStream(rawStream, fileSize, percent =>
            {
                pending.ProgressPercent = percent;
                _ = InvokeAsync(StateHasChanged);
            });

            var dto = await ShareService.SendFileAsync(progressStream, fileName, safeMimeType, _selectedTopicId.Value,
                knownFileSize: fileSize, burnAfterReading: _burnAfterReading);

            // 上传成功：原气泡就地标记完成，等 SignalR 推送时再移除占位并加入正式列表
            _messageIds.Add(dto.Id);
            pending.CompletedDto = dto;
            pending.IsCompleted = true;
            pending.ProgressPercent = 100;
            _shouldScrollIfNearBottom = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to receive dropped file {FileName}", fileName);
            pending.IsFailed = true;
            _validationError = L["Home_DropFileUploadFailed"];
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
                // 使用增量轮询：仅追加新消息，不清空列表，避免强制滚动到底部打断用户翻阅
                await PollNewMessagesAsync(ct);
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    /// <summary>
    /// 增量轮询：查询最新一批消息，仅追加尚未显示的新条目，并在用户处于底部附近时
    /// 触发条件滚动。不清空已有消息列表，不强制滚到底部，避免打断用户翻阅历史。
    /// </summary>
    private async Task PollNewMessagesAsync(CancellationToken ct = default)
    {
        if (!_selectedTopicId.HasValue) return;

        var response = await TopicService.GetTopicMessagesAsync(_selectedTopicId.Value, 50, null, ct);
        if (response is null) return;

        var hasNew = false;
        foreach (var msg in response.Messages.Reverse())
        {
            if (_messageIds.Add(msg.Id))
            {
                _messages.Add(msg);
                hasNew = true;
            }
            else
            {
                // 就地更新已有消息（例如链接元数据后台刷新）
                var idx = _messages.FindIndex(m => m.Id == msg.Id);
                if (idx >= 0) _messages[idx] = msg;
            }
        }

        if (hasNew)
        {
            // 仅当用户处于底部附近时才滚动，不强制跳转
            _shouldScrollIfNearBottom = true;
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
            await TopicStateService.SetSelectedTopicAsync(null);
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

        // 轮询降级场景：移除已由服务端确认的完成态占位条目，避免与正式消息重叠
        _pendingUploads.RemoveAll(p => p.IsCompleted && p.CompletedDto is not null && _messageIds.Contains(p.CompletedDto.Id));

        _shouldScrollToBottom = true;
    }

    private async Task LoadSelectedTopicMetaAsync(CancellationToken ct = default)
    {
        _selectedTopicName = null;
        _selectedTopicIcon = "chat_bubble";
        _selectedTopicPinned = false;
        _selectedTopicArchived = false;
        _selectedTopicIsBuiltIn = false;
        _selectedTopicMessageCount = 0;
        if (!_selectedTopicId.HasValue)
        {
            return;
        }

        // 使用 GetTopicByIdAsync 直接按 ID 查询（含已归档），避免全量加载两次列表
        var topic = await TopicService.GetTopicByIdAsync(_selectedTopicId.Value, ct);

        _selectedTopicName = topic?.Name;
        _selectedTopicIcon = topic?.Icon ?? "chat_bubble";
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

    /// <summary>将消息时间格式化为「日期 + 时间」字符串（使用浏览器时区）。</summary>
    private string FormatMessageTime(DateTimeOffset time)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(time.UtcDateTime, _displayTimeZone);
        return local.ToString("yyyy/MM/dd HH:mm");
    }

    /// <summary>将阅后即焚到期时间格式化为倒计时或已到期标记。</summary>
    private string FormatExpiry(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return L["Home_ExpiringSoon"];
        }

        return remaining.TotalMinutes >= 1
            ? L["Home_DeleteAfterMinutes", (int)remaining.TotalMinutes]
            : L["Home_DeleteAfterSeconds", (int)remaining.TotalSeconds];
    }

    /// <summary>返回待上传条目对应的 Material Symbol 图标名称。</summary>
    private static string GetPendingUploadIcon(ShareContentType contentType) => contentType switch
    {
        ShareContentType.Image => "image",
        ShareContentType.Video => "videocam",
        _ => "attach_file"
    };

    /// <summary>待上传占位条目，跟踪单个文件的上传进度与状态。</summary>
    private sealed class PendingUpload(string fileName, string mimeType, long fileSize, ShareContentType contentType)
    {
        public Guid TempId { get; } = Guid.NewGuid();
        public string FileName { get; } = fileName;
        public string MimeType { get; } = mimeType;
        public long FileSize { get; } = fileSize;
        public ShareContentType ContentType { get; } = contentType;
        public int ProgressPercent { get; set; }
        public bool IsFailed { get; set; }
        /// <summary>上传完成时为 true，此时 <see cref="CompletedDto"/> 包含服务端返回的消息数据。</summary>
        public bool IsCompleted { get; set; }
        /// <summary>上传成功后由服务端返回的消息 DTO，用于在原气泡上直接渲染预览。</summary>
        public ShareItemDto? CompletedDto { get; set; }

        public static ShareContentType ResolveContentType(string mime)
        {
            if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return ShareContentType.Image;
            if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return ShareContentType.Video;
            return ShareContentType.File;
        }
    }

    /// <summary>包装流并在读取时汇报上传进度百分比（每变化 ≥1% 触发一次回调）。</summary>
    private sealed class ProgressStream(Stream inner, long totalBytes, Action<int> onProgress) : Stream
    {
        private long _bytesRead;
        private int _lastPercent = -1;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => totalBytes > 0 ? totalBytes : inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            Report(n);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var n = await inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(false);
            Report(n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = await inner.ReadAsync(buffer, ct).ConfigureAwait(false);
            Report(n);
            return n;
        }

        private void Report(int bytes)
        {
            if (bytes <= 0 || totalBytes <= 0) return;
            _bytesRead += bytes;
            var percent = (int)Math.Min(100L, _bytesRead * 100L / totalBytes);
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                onProgress(percent);
            }
        }

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
