using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Components.Pages;

public partial class Home : IAsyncDisposable
{
    [Inject] public required IShareService ShareService { get; set; }
    [Inject] public required ITopicService TopicService { get; set; }
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
    private Guid? _selectedTopicId;

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
}
