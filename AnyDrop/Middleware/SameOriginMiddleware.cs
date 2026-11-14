using AnyDrop.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AnyDrop.Middleware;

/// <summary>
/// S3-02：表单类 API 接口的 Origin 校验（CSRF 纵深防御）。
///
/// Cookie 认证 + <c>SameSite=Lax</c> 只能挡跨站 POST 的 Cookie 携带，
/// 但挡不住同站脚本之外的跨站请求伪造组合。浏览器对跨站 POST 一定会带
/// <c>Origin</c> 头；本中间件只对 <c>/api/v1</c> 下的改动请求
/// （POST/PUT/PATCH/DELETE）校验它：
///   * 无 Origin 头 → 放行。MAUI、curl 等非浏览器客户端不发 Origin，
///     不能因此拒绝它们。
///   * Origin 的 host 与请求的 Host 一致 → 放行。
///   * Host 不一致但与 X-Forwarded-Host 一致 → 放行。ForwardedHeaders
///     未配置 XForwardedHost 时，应用看到的 Host 仍是内网 host，
///     而浏览器 Origin 是公网 host；反代若透传了 X-Forwarded-Host，
///     以它为准，不误杀。
///   * 另可配 Security:AllowedOrigins（逗号分隔的 host 列表）放行
///     已知的前端域名。
///   * 其余情况 → 403，不进后续管道。
///
/// 配置 <c>Security:RequireSameOrigin</c>（默认 true）可关闭本检查。
/// </summary>
public sealed class SameOriginMiddleware(RequestDelegate next, ILogger<SameOriginMiddleware> logger, bool enabled, IReadOnlySet<string> allowedOrigins)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (enabled && IsMutatingApiRequest(context.Request))
        {
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(origin) && !IsSameOrigin(context.Request, origin))
            {
                logger.LogWarning(
                    "拒绝跨站 API 请求：Origin {Origin} 与 Host {Host} 不一致（{Method} {Path}）。",
                    origin, context.Request.Host, context.Request.Method, context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    ApiEnvelope<object>.Fail("请求来源不正确。"), context.RequestAborted);
                return;
            }
        }

        await next(context);
    }

    private static bool IsMutatingApiRequest(HttpRequest request)
        => request.Path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)
            && request.Method is "POST" or "PUT" or "PATCH" or "DELETE";

    private bool IsSameOrigin(HttpRequest request, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        var originHost = originUri.Host;
        if (string.Equals(originHost, request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var forwardedHost = request.Headers["X-Forwarded-Host"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedHost)
            && string.Equals(originHost, forwardedHost.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return allowedOrigins.Contains(originHost, StringComparer.OrdinalIgnoreCase);
    }
}

public static class SameOriginMiddlewareExtensions
{
    /// <summary>注册 Origin 校验中间件（须放在 UseForwardedHeaders 之后）。</summary>
    public static IApplicationBuilder UseSameOriginCheck(
        this IApplicationBuilder app, bool enabled, IReadOnlySet<string> allowedOrigins)
        => app.UseMiddleware<SameOriginMiddleware>(enabled, allowedOrigins);
}
