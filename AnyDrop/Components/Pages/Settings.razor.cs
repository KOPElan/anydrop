using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Localization;
using Microsoft.JSInterop;
using System.Text.Json;

namespace AnyDrop.Components.Pages;

public partial class Settings
{
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }

    private string _nickname = string.Empty;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _autoFetchLinkPreview = true;
    private int _burnAfterReadingMinutes = 10;
    private string _timeZoneId = "UTC";
    private string _language = "zh-CN";
    private string? _message;
    private string? _error;

    // 所有可用系统时区列表，在 OnAfterRenderAsync 中加载
    private IReadOnlyList<TimeZoneInfo> _systemTimeZones = [];

    /// <summary>返回首页。</summary>
    private void GoBack() => NavigationManager.NavigateTo("/");

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _systemTimeZones = [.. TimeZoneInfo.GetSystemTimeZones()];
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
        if (security.ok && security.body?.data.ValueKind == JsonValueKind.Object)
        {
            var data = security.body.data;
            if (data.TryGetProperty("autoFetchLinkPreview", out var autoFetch))
            {
                _autoFetchLinkPreview = autoFetch.GetBoolean();
            }
            if (data.TryGetProperty("burnAfterReadingMinutes", out var burn))
            {
                _burnAfterReadingMinutes = burn.GetInt32();
            }
            if (data.TryGetProperty("timeZoneId", out var tz))
            {
                _timeZoneId = tz.GetString() ?? "UTC";
            }
            if (data.TryGetProperty("language", out var lang))
            {
                _language = lang.GetString() ?? "zh-CN";
            }
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
        var payload = new
        {
            autoFetchLinkPreview = _autoFetchLinkPreview,
            timeZoneId = _timeZoneId,
            burnAfterReadingMinutes = _burnAfterReadingMinutes,
            language = _language
        };
        var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.putJson", "/api/v1/settings/security", payload);
        if (!result.ok)
        {
            _error = result.body?.error ?? "保存设置失败。";
            return;
        }

        // 设置语言 Cookie 并强制刷新页面，以使新语言立即生效
        await JSRuntime.InvokeAsync<JsApiResult>("authInterop.postJson", "/api/v1/settings/set-culture",
            new { culture = _language });

        _message = "设置已保存。";
        // 使用 forceLoad 重新加载以应用新语言
        NavigationManager.NavigateTo("/settings", forceLoad: true);
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
