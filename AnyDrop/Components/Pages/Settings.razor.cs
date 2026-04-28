using AnyDrop.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using System.Text.Json;

namespace AnyDrop.Components.Pages;

public partial class Settings
{
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required IStringLocalizer<SharedStrings> L { get; set; }

    private string _nickname = string.Empty;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _autoFetchLinkPreview = true;
    private int _burnAfterReadingMinutes = 10;
    private bool _isDarkMode;
    private string _language = "zh-CN";
    private bool _autoCleanupEnabled;
    private int _autoCleanupMonths = 1;
    private string? _message;
    private string? _error;

    // 手动清理相关
    private bool _showCleanupConfirmModal;
    private int _pendingCleanupMonths;
    private bool _isCleaningUp;

    /// <summary>返回首页。</summary>
    private void GoBack() => NavigationManager.NavigateTo("/");

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
            if (data.TryGetProperty("language", out var lang))
            {
                _language = lang.GetString() ?? "zh-CN";
            }
            if (data.TryGetProperty("autoCleanupEnabled", out var autoCleanup))
            {
                _autoCleanupEnabled = autoCleanup.GetBoolean();
            }
            if (data.TryGetProperty("autoCleanupMonths", out var months))
            {
                _autoCleanupMonths = months.GetInt32();
            }
        }

        // 从 localStorage 读取主题设置
        try { _isDarkMode = await JSRuntime.InvokeAsync<bool>("AnyDropTheme.get"); }
        catch { _isDarkMode = false; }
    }

    private async Task SaveNicknameAsync()
    {
        ResetMessages();
        var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.putJson", "/api/v1/settings/profile", new { nickname = _nickname });
        if (!result.ok)
        {
            _error = result.body?.error ?? L["Settings_SaveNicknameFailed"];
            return;
        }

        _message = L["Settings_NicknameSaved"];
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
            _error = result.body?.error ?? L["Settings_UpdatePasswordFailed"];
            return;
        }

        _currentPassword = string.Empty;
        _newPassword = string.Empty;
        _confirmPassword = string.Empty;
        _message = L["Settings_PasswordUpdated"];
    }

    private async Task SaveSecurityAsync()
    {
        ResetMessages();
        var payload = new
        {
            autoFetchLinkPreview = _autoFetchLinkPreview,
            burnAfterReadingMinutes = _burnAfterReadingMinutes,
            language = _language,
            autoCleanupEnabled = _autoCleanupEnabled,
            autoCleanupMonths = _autoCleanupMonths
        };
        var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.putJson", "/api/v1/settings/security", payload);
        if (!result.ok)
        {
            _error = result.body?.error ?? L["Settings_SaveSecurityFailed"];
            return;
        }

        // 设置语言 Cookie，失败时记录提示但不阻断流程
        var cultureResult = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.postJson", "/api/v1/settings/set-culture",
            new { culture = _language });
        if (!cultureResult.ok)
        {
            _error = cultureResult.body?.error ?? L["Settings_SaveSecurityFailed"];
            return;
        }

        // 强制刷新以应用新语言
        NavigationManager.NavigateTo("/settings", forceLoad: true);
    }

    /// <summary>显示手动清理二次确认对话框。</summary>
    private void RequestCleanup(int months)
    {
        _pendingCleanupMonths = months;
        _showCleanupConfirmModal = true;
        ResetMessages();
    }

    /// <summary>取消手动清理。</summary>
    private void CancelCleanupConfirm()
    {
        _showCleanupConfirmModal = false;
    }

    /// <summary>执行手动清理：调用 API 删除旧消息及文件资源。</summary>
    private async Task ConfirmCleanupAsync()
    {
        _isCleaningUp = true;
        _showCleanupConfirmModal = false;
        ResetMessages();

        try
        {
            var result = await JSRuntime.InvokeAsync<JsApiResult>(
                "authInterop.deleteJson",
                $"/api/v1/share-items/cleanup?months={_pendingCleanupMonths}");

            if (!result.ok)
            {
                _error = result.body?.error ?? L["Settings_CleanupFailed"];
                return;
            }

            // 从 data.deletedCount 读取实际删除数量
            var deleted = 0;
            if (result.body?.data.ValueKind == JsonValueKind.Object &&
                result.body.data.TryGetProperty("deletedCount", out var deletedCountProp))
            {
                deleted = deletedCountProp.GetInt32();
            }

            _message = deleted == 0
                ? (string)L["Settings_CleanupNone"]
                : (string)L["Settings_CleanupSuccess", deleted];
        }
        finally
        {
            _isCleaningUp = false;
        }
    }

    /// <summary>切换暗色/亮色主题，立即应用并持久化到 localStorage。</summary>
    private async Task ToggleThemeAsync()
    {
        _isDarkMode = !_isDarkMode;
        await JSRuntime.InvokeVoidAsync("AnyDropTheme.set", _isDarkMode);
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
