using AnyDrop.App.Platform;
using AnyDrop.Shared;
using Microsoft.JSInterop;

namespace AnyDrop.App.Services;

/// <summary>
/// MAUI 端 <see cref="IThemeService"/> 实现（S3-06）：JS 互操作 <c>window.AnyDropTheme</c>
/// 之外，把偏好镜像到原生 <see cref="IPreferenceStore"/>——原生侧读偏好不经过
/// WebView localStorage，读时以 Preferences 为准并同步回 JS 端（与 S2-05 前行为一致）。
/// 平台差异收在 <see cref="IPreferenceStore"/> 里，本类不含条件编译。
/// </summary>
public sealed class JsThemeService(IJSRuntime js, IPreferenceStore preferences) : IThemeService
{
    private const string ThemeKey = "anydrop_theme";

    public async Task<bool> GetIsDarkAsync(CancellationToken ct = default)
    {
        var isDark = string.Equals(preferences.Get(ThemeKey, "light"), "dark", StringComparison.OrdinalIgnoreCase);
        // 同步 JS 端 CSS 类，确保 WebView 也应用正确主题
        await js.InvokeVoidAsync("AnyDropTheme.set", ct, isDark).ConfigureAwait(false);
        return isDark;
    }

    public Task SetIsDarkAsync(bool isDark, CancellationToken ct = default)
    {
        preferences.Set(ThemeKey, isDark ? "dark" : "light");
        return js.InvokeVoidAsync("AnyDropTheme.set", ct, isDark).AsTask();
    }
}
