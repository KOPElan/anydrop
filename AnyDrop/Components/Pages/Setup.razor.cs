using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using AnyDrop.Services;

namespace AnyDrop.Components.Pages;

public partial class Setup
{
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required ILogger<Setup> Logger { get; set; }
    [Inject] public required IUserService UserService { get; set; }

    private readonly SetupFormModel _model = new();
    private bool _submitting;
    private bool _isInteractive;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var hasUser = await UserService.HasUserAsync();
        if (hasUser)
        {
            NavigationManager.NavigateTo("/login", forceLoad: true);
        }
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return Task.CompletedTask;
        }

        _isInteractive = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task HandleSubmitAsync()
    {
        _submitting = true;
        _error = null;
        try
        {
            var payload = new
            {
                nickname = _model.Nickname,
                password = _model.Password,
                confirmPassword = _model.ConfirmPassword
            };

            var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.postJson", "/api/v1/auth/setup", payload);
            if (!result.ok)
            {
                _error = result.body?.error ?? "初始化失败，请稍后重试。";
                return;
            }

            NavigationManager.NavigateTo("/", forceLoad: true);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Setup request failed.");
            _error = "初始化失败，请稍后重试。";
        }
        finally
        {
            _submitting = false;
        }
    }

    private sealed class SetupFormModel
    {
        [Required, StringLength(50)] public string Nickname { get; set; } = string.Empty;
        [Required, MinLength(6)] public string Password { get; set; } = string.Empty;
        [Required, MinLength(6)] public string ConfirmPassword { get; set; } = string.Empty;
    }

    private sealed class JsApiResult
    {
        public bool ok { get; set; }
        public int status { get; set; }
        public JsApiEnvelope? body { get; set; }
    }

    private sealed class JsApiEnvelope
    {
        public bool success { get; set; }
        public System.Text.Json.JsonElement data { get; set; }
        public string? error { get; set; }
    }
}
