using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace AnyDrop.Components.Pages;

public partial class Settings
{
    [Inject] public required IJSRuntime JSRuntime { get; set; }

    private string _nickname = string.Empty;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _autoFetchLinkPreview = true;
    private string? _message;
    private string? _error;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await LoadAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadAsync()
    {
        var profile = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.getJson", "/api/v1/auth/me");
        if (profile.ok && profile.body?.data.ValueKind == JsonValueKind.Object &&
            profile.body.data.TryGetProperty("nickname", out var nickname))
        {
            _nickname = nickname.GetString() ?? string.Empty;
        }

        var security = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.getJson", "/api/v1/settings/security");
        if (security.ok && security.body?.data.ValueKind == JsonValueKind.Object &&
            security.body.data.TryGetProperty("autoFetchLinkPreview", out var autoFetch))
        {
            _autoFetchLinkPreview = autoFetch.GetBoolean();
        }
    }

    private async Task SaveNicknameAsync()
    {
        ResetMessages();
        var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.putJson", "/api/v1/settings/profile", new { nickname = _nickname });
        if (!result.ok)
        {
            _error = result.body?.error ?? "保存昵称失败。";
            return;
        }

        _message = "昵称已更新。";
    }

    private async Task SavePasswordAsync()
    {
        ResetMessages();
        var payload = new
        {
            currentPassword = _currentPassword,
            newPassword = _newPassword,
            confirmPassword = _confirmPassword
        };
        var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.putJson", "/api/v1/settings/password", payload);
        if (!result.ok)
        {
            _error = result.body?.error ?? "更新密码失败。";
            return;
        }

        _currentPassword = string.Empty;
        _newPassword = string.Empty;
        _confirmPassword = string.Empty;
        _message = "密码已更新。";
    }

    private async Task SaveSecurityAsync()
    {
        ResetMessages();
        var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.putJson", "/api/v1/settings/security",
            new { autoFetchLinkPreview = _autoFetchLinkPreview });
        if (!result.ok)
        {
            _error = result.body?.error ?? "保存安全设置失败。";
            return;
        }

        _message = "安全设置已更新。";
    }

    private void ResetMessages()
    {
        _message = null;
        _error = null;
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
        public JsonElement data { get; set; }
        public string? error { get; set; }
    }
}
