using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Components.Pages;

public partial class Home : IAsyncDisposable
{
    private const long DefaultMaxFileSizeBytes = 100L * 1024 * 1024;

    [Inject] public required IShareService ShareService { get; set; }
    [Inject] public required ITopicService TopicService { get; set; }
    [Inject] public required IConfiguration Configuration { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required ILogger<Home> Logger { get; set; }
    [CascadingParameter] public Guid? SelectedTopicId { get; set; }

    private readonly List<ShareItemDto> _messages = [];
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
        if (!firstRender)
        {
            return;
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/share"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<ShareItemDto>("ReceiveShareItem", dto =>
        {
            return InvokeAsync(async () =>
            {
                if (_selectedTopicId.HasValue && dto.TopicId == _selectedTopicId)
                {
                    _messages.Insert(0, dto);
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

    private async Task SendAsync()
    {
        _validationError = null;

        var trimmedText = _inputText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            _validationError = "Message content is required.";
            return;
        }

        if (!_selectedTopicId.HasValue)
        {
            _validationError = "请先选择主题。";
            return;
        }

        if (trimmedText.Length > 10_000)
        {
            _validationError = "Message cannot exceed 10,000 characters.";
            return;
        }

        _isSending = true;
        try
        {
            await ShareService.SendTextAsync(trimmedText, _selectedTopicId);
            _inputText = string.Empty;
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

        await LoadSelectedTopicMessagesAsync();
    }

    private void OnDragEnter(DragEventArgs _)
    {
        _isDragging = true;
    }

    private void OnDragLeave(DragEventArgs _)
    {
        _isDragging = false;
    }

    public async ValueTask DisposeAsync()
    {
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

        _messages.AddRange(response.Messages);
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
}
