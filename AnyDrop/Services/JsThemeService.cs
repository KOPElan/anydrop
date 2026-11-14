using AnyDrop.Shared;
using Microsoft.JSInterop;

namespace AnyDrop.Services;

/// <summary>Web 端 <see cref="IThemeService"/> 实现：直通 JS 互操作 <c>window.AnyDropTheme</c>（S3-06）。</summary>
public sealed class JsThemeService(IJSRuntime js) : IThemeService
{
    public Task<bool> GetIsDarkAsync(CancellationToken ct = default) =>
        js.InvokeAsync<bool>("AnyDropTheme.get", ct).AsTask();

    public Task SetIsDarkAsync(bool isDark, CancellationToken ct = default) =>
        js.InvokeVoidAsync("AnyDropTheme.set", ct, isDark).AsTask();
}
