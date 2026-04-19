using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Components.Pages;

public partial class Login
{
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required ILogger<Login> Logger { get; set; }

    private readonly LoginFormModel _model = new();
    private bool _submitting;
    private bool _isInteractive;
    private string? _error;
    private string _returnUrl = "/";

    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }

    protected override void OnParametersSet()
    {
        _returnUrl = string.IsNullOrWhiteSpace(ReturnUrl) ? "/" : ReturnUrl!;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _isInteractive = true;
        StateHasChanged();

        var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.getJson", "/api/v1/auth/me");
        if (result.ok)
        {
            NavigationManager.NavigateTo(_returnUrl, forceLoad: true);
        }
    }

    private async Task HandleSubmitAsync()
    {
        _submitting = true;
        _error = null;
        try
        {
            var payload = new
            {
                password = _model.Password,
                returnUrl = _returnUrl
            };

            var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.postJson", "/api/v1/auth/login", payload);
            if (!result.ok)
            {
                _error = result.body?.error ?? "账号或密码错误。";
                return;
            }

            NavigationManager.NavigateTo(_returnUrl, forceLoad: true);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Login request failed.");
            _error = "登录失败，请稍后重试。";
        }
        finally
        {
            _submitting = false;
        }
    }

    private sealed class LoginFormModel
    {
        [Required] public string Password { get; set; } = string.Empty;
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
        public object? data { get; set; }
        public string? error { get; set; }
    }
}
