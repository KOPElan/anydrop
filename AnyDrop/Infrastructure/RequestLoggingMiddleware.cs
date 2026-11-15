using System.Diagnostics;

namespace AnyDrop.Infrastructure;

/// <summary>
/// 轻量访问日志：记录方法、路径、状态码与耗时，超过阈值时降级为警告。
///
/// 自托管场景下，用户排查问题时最需要的就是「哪个请求慢、哪个请求失败了」。
///
/// 默认日志级别为 <c>Warning</c>（见 appsettings.json 的 <c>AnyDrop.RequestLog</c>），
/// 因此默认只输出慢请求；把它调成 <c>Information</c> 即可得到完整访问日志。
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
{
    private const string CategoryName = "AnyDrop.RequestLog";

    /// <summary>超过该耗时即视为慢请求并记警告。</summary>
    private static readonly TimeSpan SlowRequestThreshold = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger = loggerFactory.CreateLogger(CategoryName);

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context.Request.Path))
        {
            await next(context);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);

            if (elapsed >= SlowRequestThreshold)
            {
                _logger.LogWarning(
                    "慢请求 {Method} {Path} -> {StatusCode}，耗时 {ElapsedMilliseconds:F0} ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    elapsed.TotalMilliseconds);
            }
            else
            {
                _logger.LogInformation(
                    "{Method} {Path} -> {StatusCode}（{ElapsedMilliseconds:F0} ms）",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    elapsed.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// 跳过静态资源与健康探针：前者会把日志淹没，后者每 30 秒被容器编排调用一次。
    /// </summary>
    private static bool ShouldSkip(PathString path)
    {
        var value = path.Value ?? string.Empty;

        if (value.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/images", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 带扩展名的路径基本都是静态资源
        return Path.HasExtension(value);
    }
}
