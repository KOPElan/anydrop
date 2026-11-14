using AnyDrop.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Text.Json;

namespace AnyDrop.Components.Pages;

public partial class Settings
{
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required IStringLocalizer<SharedStrings> L { get; set; }
    [Inject] public required ILogger<Settings> Logger { get; set; }

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
    private int _thumbnailGenerationHour = 2;
    private bool _scheduledThumbnailGenerationEnabled;
    private bool _isRunningThumbnails;
    private string? _message;
    private string? _error;

    // 手动清理相关
    private bool _showCleanupConfirmModal;
    private int _pendingCleanupMonths;
    private bool _isCleaningUp;

    // 存储健康（S1-01 缺失引用修复入口）
    private const string DeleteMessageAction = "delete-message";
    private const string DismissAction = "dismiss";
    private List<MissingFileReportItem>? _missingFiles;
    private bool _isResolvingMissingFile;

    // S2-10：存储健康“首次发现”时间按浏览器时区显示；旧实现直接 ToString()
    //（UTC 原样输出，与本地时间差 8 小时）。
    private TimeZoneInfo _displayTimeZone = TimeZoneInfo.Local;

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
        // S2-10：JS 互操作失败（电路断开、JS 出错）时不再把异常抛给 Blazor 导致页面崩溃——
        // 逐项捕获，失败的项保留默认值并显示加载失败提示。
        try
        {
            var profile = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.getJson", "/api/v1/auth/me");
            if (profile.ok && profile.body?.data.ValueKind == JsonValueKind.Object &&
                profile.body.data.TryGetProperty("nickname", out var nickname))
            {
                _nickname = nickname.GetString() ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            Logger.LogWarning(ex, "Failed to load profile in settings.");
            _error = L["Settings_LoadFailed"];
        }

        try
        {
            var browserTimeZoneId = await JSRuntime.InvokeAsync<string>("AnyDropInterop.getBrowserTimeZone");
            try { _displayTimeZone = TimeZoneInfo.FindSystemTimeZoneById(browserTimeZoneId); }
            catch { _displayTimeZone = TimeZoneInfo.Local; }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            Logger.LogDebug(ex, "Failed to get browser timezone in settings, falling back to server local time.");
        }

        try
        {
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
                if (data.TryGetProperty("thumbnailGenerationHour", out var thumbHour))
                {
                    _thumbnailGenerationHour = thumbHour.GetInt32();
                }
                if (data.TryGetProperty("scheduledThumbnailGenerationEnabled", out var scheduledThumbnails))
                {
                    _scheduledThumbnailGenerationEnabled = scheduledThumbnails.GetBoolean();
                }
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            Logger.LogWarning(ex, "Failed to load security settings.");
            _error = L["Settings_LoadFailed"];
        }

        // 从 localStorage 读取主题设置
        try { _isDarkMode = await JSRuntime.InvokeAsync<bool>("AnyDropTheme.get"); }
        catch { _isDarkMode = false; }

        await LoadMissingFilesAsync();
    }

    /// <summary>“首次发现”时间按浏览器时区换算后显示。</summary>
    private string FormatDetectedTime(DateTimeOffset time)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(time.UtcDateTime, _displayTimeZone);
        return local.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>加载未解决的缺失引用记录。</summary>
    private async Task LoadMissingFilesAsync()
    {
        try
        {
            var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.getJson", "/api/v1/storage/missing-files");
            if (!result.ok || result.body?.data.ValueKind != JsonValueKind.Array)
            {
                _missingFiles = [];
                return;
            }

            var list = new List<MissingFileReportItem>();
            foreach (var element in result.body.data.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                list.Add(new MissingFileReportItem(
                    element.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid) ? guid : Guid.Empty,
                    element.TryGetProperty("shareItemId", out var itemId) && itemId.TryGetGuid(out var itemGuid) ? itemGuid : Guid.Empty,
                    element.TryGetProperty("storagePath", out var path) ? path.GetString() ?? string.Empty : string.Empty,
                    element.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "content" : "content",
                    element.TryGetProperty("fileName", out var fileName) ? fileName.GetString() : null,
                    element.TryGetProperty("topicName", out var topicName) ? topicName.GetString() : null,
                    element.TryGetProperty("firstDetectedAt", out var first) && first.TryGetDateTimeOffset(out var firstDetected)
                        ? firstDetected
                        : DateTimeOffset.MinValue));
            }

            _missingFiles = list;
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            Logger.LogWarning(ex, "Failed to load missing-file reports.");
            _missingFiles = [];
            _error = L["Settings_LoadFailed"];
        }
    }

    /// <summary>处理一条缺失引用记录：delete-message 删除对应消息，dismiss 标记已处理。</summary>
    private async Task ResolveMissingFileAsync(Guid reportId, string action)
    {
        ResetMessages();
        _isResolvingMissingFile = true;
        try
        {
            var result = await JSRuntime.InvokeAsync<JsApiResult>(
                "authInterop.postJson",
                $"/api/v1/storage/missing-files/{reportId}/resolve",
                new { action });
            if (!result.ok)
            {
                _error = result.body?.error ?? L["Settings_StorageHealthFailed"];
                return;
            }

            _message = L["Settings_StorageHealthResolved"];
            await LoadMissingFilesAsync();
        }
        finally
        {
            _isResolvingMissingFile = false;
        }
    }

    private async Task SaveNicknameAsync()
    {
        ResetMessages();
        try
        {
            var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.putJson", "/api/v1/settings/profile", new { nickname = _nickname });
            if (!result.ok)
            {
                _error = result.body?.error ?? L["Settings_SaveNicknameFailed"];
                return;
            }

            _message = L["Settings_NicknameSaved"];
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            Logger.LogWarning(ex, "Save nickname failed via JS interop.");
            _error = L["Settings_SaveNicknameFailed"];
        }
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
        try
        {
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
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            Logger.LogWarning(ex, "Change password failed via JS interop.");
            _error = L["Settings_UpdatePasswordFailed"];
        }
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
            autoCleanupMonths = _autoCleanupMonths,
            thumbnailGenerationHour = _thumbnailGenerationHour,
            scheduledThumbnailGenerationEnabled = _scheduledThumbnailGenerationEnabled
        };
        try
        {
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
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            Logger.LogWarning(ex, "Save security settings failed via JS interop.");
            _error = L["Settings_SaveSecurityFailed"];
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
        try
        {
            await JSRuntime.InvokeVoidAsync("AnyDropTheme.set", _isDarkMode);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            // S2-10：JS 调用失败时回滚开关，保持界面与实际主题一致
            Logger.LogWarning(ex, "Failed to apply theme via JS interop.");
            _isDarkMode = !_isDarkMode;
        }
    }

    /// <summary>立即触发缩略图批处理任务。</summary>
    private async Task RunThumbnailsNowAsync()
    {
        ResetMessages();
        _isRunningThumbnails = true;
        try
        {
            var result = await JSRuntime.InvokeAsync<JsApiResult>(
                "authInterop.postJson",
                "/api/v1/settings/thumbnails/run",
                new { });
            if (result.ok || result.status == 202)
            {
                _message = L["Settings_ThumbnailRunQueued"];
            }
            else
            {
                _error = result.body?.error ?? L["Settings_ThumbnailRunFailed"];
            }
        }
        finally
        {
            _isRunningThumbnails = false;
        }
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

    /// <summary>缺失引用记录（设置页存储健康区展示用）。</summary>
    private sealed record MissingFileReportItem(
        Guid Id,
        Guid ShareItemId,
        string StoragePath,
        string Kind,
        string? FileName,
        string? TopicName,
        DateTimeOffset FirstDetectedAt);

    private sealed class JsApiEnvelope
    {
        public bool success { get; set; }
        public JsonElement data { get; set; }
        public string? error { get; set; }
    }
}
