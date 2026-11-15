using AnyDrop.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnyDrop.Tests.Unit.Middleware;

/// <summary>
/// S3-02：Origin 校验——只挡带跨站 Origin 的改动 API 请求，
/// 无 Origin 的非浏览器客户端与同站请求一律放行。
/// </summary>
public class SameOriginMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_SameOriginPost_PassesThrough()
    {
        var reachedNext = false;
        var middleware = CreateMiddleware(next: _ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("POST", "/api/v1/topics", origin: "https://anydrop.example.com");
        context.Request.Host = new HostString("anydrop.example.com");

        await middleware.InvokeAsync(context);

        reachedNext.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_CrossOriginPost_Returns403()
    {
        var reachedNext = false;
        var middleware = CreateMiddleware(next: _ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("POST", "/api/v1/topics", origin: "https://evil.example.com");
        context.Request.Host = new HostString("anydrop.example.com");

        await middleware.InvokeAsync(context);

        reachedNext.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_NoOriginHeader_PassesThrough()
    {
        // MAUI、curl 等非浏览器客户端不发 Origin，不能因此拒绝。
        var reachedNext = false;
        var middleware = CreateMiddleware(next: _ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("POST", "/api/v1/uploads/init", origin: null);
        context.Request.Host = new HostString("anydrop.example.com");

        await middleware.InvokeAsync(context);

        reachedNext.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_CrossOriginGet_IsNotChecked()
    {
        // 只校验改动请求：GET 不带副作用，Origin 再跨站也不挡。
        var reachedNext = false;
        var middleware = CreateMiddleware(next: _ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("GET", "/api/v1/topics", origin: "https://evil.example.com");
        context.Request.Host = new HostString("anydrop.example.com");

        await middleware.InvokeAsync(context);

        reachedNext.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ForwardedHostMatchesOrigin_PassesThrough()
    {
        // 反代部署：应用看到的 Host 是内网 host，浏览器 Origin 是公网 host，
        // 反代透传的 X-Forwarded-Host 与 Origin 一致时放行。
        var reachedNext = false;
        var middleware = CreateMiddleware(next: _ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("DELETE", "/api/v1/share-items/batch", origin: "https://anydrop.example.com");
        context.Request.Host = new HostString("localhost:8080");
        context.Request.Headers["X-Forwarded-Host"] = "anydrop.example.com";

        await middleware.InvokeAsync(context);

        reachedNext.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AllowedOrigin_PassesThrough()
    {
        var reachedNext = false;
        var middleware = CreateMiddleware(
            next: _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            allowedOrigins: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "frontend.example.com" });

        var context = CreateContext("POST", "/api/v1/topics", origin: "https://frontend.example.com");
        context.Request.Host = new HostString("anydrop.example.com");

        await middleware.InvokeAsync(context);

        reachedNext.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_Disabled_PassesThroughCrossOrigin()
    {
        var reachedNext = false;
        var middleware = CreateMiddleware(
            next: _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            enabled: false);

        var context = CreateContext("POST", "/api/v1/topics", origin: "https://evil.example.com");
        context.Request.Host = new HostString("anydrop.example.com");

        await middleware.InvokeAsync(context);

        reachedNext.Should().BeTrue();
    }

    private static SameOriginMiddleware CreateMiddleware(
        RequestDelegate next, bool enabled = true, IReadOnlySet<string>? allowedOrigins = null)
        => new(next, NullLogger<SameOriginMiddleware>.Instance, enabled,
            allowedOrigins ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static DefaultHttpContext CreateContext(string method, string path, string? origin)
    {
        // WriteAsJsonAsync（403 分支）需要 RequestServices 提供 JsonOptions 与日志。
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        context.Request.Method = method;
        context.Request.Path = path;
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context;
    }
}
