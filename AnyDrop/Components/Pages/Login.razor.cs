using AnyDrop.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;

namespace AnyDrop.Components.Pages;

public partial class Login
{
    [Inject] public required IJSRuntime JSRuntime { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required ILogger<Login> Logger { get; set; }
    [Inject] public required IStringLocalizer<SharedStrings> L { get; set; }

    private readonly LoginFormModel _model = new();
    private bool _submitting;
    private bool _isInteractive;
    private string? _error;
    private string _returnUrl = "/";

    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }

    protected override void OnParametersSet()
    {
        _returnUrl = SanitizeReturnUrl(ReturnUrl);
    }

    /// <summary>
    /// 只接受站内相对路径作为登录后跳转目标（SEC-01）。
    ///
    /// 攻击者可构造 <c>/login?returnUrl=https://evil.example/...</c> 的链接：
    /// 登录成功后若原样跳转，owner 会在刚输完密码、最信任的时刻被带到仿冒站。
    /// 因此只接受以单个 <c>/</c> 开头、不含协议（<c>://</c>，含 URL 编码后的形式）
    /// 与反斜杠的路径，其余一律回退首页。与服务端中间件已有的安全写法对齐。
    /// </summary>
    internal static string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var candidate = returnUrl.Trim();
        if (!IsLocalRelativePath(candidate))
        {
            return "/";
        }

        // 再校验一次解码后的形式：%2F%2Fevil.com、%5C 等编码绕过在解码后现形。
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(candidate);
        }
        catch
        {
            return "/";
        }

        return IsLocalRelativePath(decoded) ? candidate : "/";
    }

    private static bool IsLocalRelativePath(string value)
    {
        if (value.Length == 0 || value[0] != '/')
        {
            return false;
        }

        // //evil.com（协议相对 URL）与 /\evil.com 都不是站内路径
        if (value.Length > 1 && (value[1] == '/' || value[1] == '\\'))
        {
            return false;
        }

        // 路径中任何位置出现协议或反斜杠都拒绝：/search?q=https://... 也不放行
        if (value.Contains('\\') || value.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        // 控制字符（换行等）可能被用来拆分响应头或跳转目标
        return value.All(c => !char.IsControl(c));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _isInteractive = true;
        StateHasChanged();

        // S2-10：已登录检查只是“自动跳过登录页”的体验优化——JS 互操作失败
        // （电路断开、JS 出错）时不要把异常抛给 Blazor 导致页面崩溃，
        // 用户仍可通过表单正常登录。
        try
        {
            var result = await JSRuntime.InvokeAsync<JsApiResult>("authInterop.getJson", "/api/v1/auth/me");
            if (result.ok)
            {
                NavigationManager.NavigateTo(_returnUrl, forceLoad: true);
            }
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException or JSException)
        {
            Logger.LogDebug(ex, "Login status check via JS interop failed; user can still sign in with the form.");
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
                _error = result.body?.error ?? L["Login_InvalidCredentials"];
                return;
            }

            NavigationManager.NavigateTo(_returnUrl, forceLoad: true);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Login request failed.");
            _error = L["Login_FailedRetry"];
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
