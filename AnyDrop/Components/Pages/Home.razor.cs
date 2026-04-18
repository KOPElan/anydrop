using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace AnyDrop.Components.Pages;

public partial class Home : IAsyncDisposable
{
    [Inject] public required IShareService ShareService { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required IJSRuntime JsRuntime { get; set; }
    [Inject] public required ILogger<Home> Logger { get; set; }

    private readonly List<ShareItemDto> _messages = [];
    private HubConnection? _hubConnection;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private string _inputText = string.Empty;
    private string? _validationError;
    private bool _isSending;

    protected override async Task OnInitializedAsync()
    {
        var recentItems = await ShareService.GetRecentAsync();
        _messages.AddRange(recentItems);
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
            _messages.Add(dto);
            return InvokeAsync(StateHasChanged);
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

        if (trimmedText.Length > 10_000)
        {
            _validationError = "Message cannot exceed 10,000 characters.";
            return;
        }

        _isSending = true;
        try
        {
            await ShareService.SendTextAsync(trimmedText);
            _inputText = string.Empty;
            await JsRuntime.InvokeVoidAsync("scrollTo", 0, int.MaxValue);
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
            var latest = await ShareService.GetRecentAsync(ct: ct);
            _messages.Clear();
            _messages.AddRange(latest.OrderByDescending(x => x.CreatedAt));
            await InvokeAsync(StateHasChanged);
        }
    }
}
