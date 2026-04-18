using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace AnyDrop.Components.Pages;

/// <summary>
/// Home page that renders chat-style real-time sharing UI.
/// </summary>
public partial class Home : IAsyncDisposable
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IShareService ShareService { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    private readonly List<ShareItemDto> messages = [];
    private HubConnection? hubConnection;
    private IJSObjectReference? module;
    private string draftText = string.Empty;
    private bool isSending;
    private string? errorMessage;

    /// <summary>
    /// Loads recent data and initializes SignalR subscription.
    /// </summary>
    /// <returns>An asynchronous task.</returns>
    protected override async Task OnInitializedAsync()
    {
        var recentItems = await ShareService.GetRecentAsync();
        messages.AddRange(recentItems);

        hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/share"))
            .WithAutomaticReconnect()
            .Build();

        hubConnection.On<ShareItemDto>("ReceiveShareItem", message =>
        {
            return InvokeAsync(async () =>
            {
                messages.Add(message);
                StateHasChanged();
                await ScrollToBottomAsync();
            });
        });

        await hubConnection.StartAsync();
    }

    /// <summary>
    /// Initializes JS module on first render and keeps timeline pinned to bottom.
    /// </summary>
    /// <param name="firstRender">Indicates whether this is the first render pass.</param>
    /// <returns>An asynchronous task.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        module = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./Components/Pages/Home.razor.js");
        await ScrollToBottomAsync();
    }

    /// <summary>
    /// Sends text after validation.
    /// </summary>
    /// <returns>An asynchronous task.</returns>
    private async Task SendAsync()
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(draftText))
        {
            errorMessage = "请输入非空文本内容。";
            return;
        }

        if (draftText.Length > ShareValidationRules.MaxTextLength)
        {
            errorMessage = $"文本长度不能超过 {ShareValidationRules.MaxTextLength} 字符。";
            return;
        }

        isSending = true;

        try
        {
            await ShareService.SendTextAsync(draftText);
            draftText = string.Empty;
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            isSending = false;
        }
    }

    /// <summary>
    /// Cleans up the SignalR client connection.
    /// </summary>
    /// <returns>An asynchronous task.</returns>
    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync();
        }

        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }

    /// <summary>
    /// Scrolls the timeline container to the latest message.
    /// </summary>
    /// <returns>An asynchronous task.</returns>
    private async Task ScrollToBottomAsync()
    {
        if (module is null)
        {
            return;
        }

        await module.InvokeVoidAsync("scrollTimelineToBottom", "chatTimeline");
    }

    /// <summary>
    /// Returns stable message bubble classes based on message identity bytes.
    /// </summary>
    /// <param name="message">The message DTO.</param>
    /// <returns>CSS class names for message bubble styling and alignment.</returns>
    private static string GetMessageBubbleClasses(ShareItemDto message)
    {
        var firstByte = message.Id.ToByteArray()[0];
        return firstByte % 2 is 0 ? "chat-item chat-item--left" : "chat-item chat-item--right";
    }
}
