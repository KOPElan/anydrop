using AnyDrop.Tests.E2E.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

/// <summary>
/// 安全响应头与内容安全策略（CSP）的端到端验证。
///
/// CSP 很容易「看起来正确但实际把页面打坏」——被拦截的资源只会在浏览器控制台留下
/// 一条错误，服务端日志里什么也看不到。因此这里用真实浏览器逐页访问，
/// 断言控制台没有任何错误（含 CSP 违规）。
/// </summary>
[Collection(E2ECollection.Name)]
public class SecurityHeadersTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task Responses_CarryHardeningHeaders()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30_000
        });

        response.Should().NotBeNull();
        var headers = response!.Headers;

        headers.Should().ContainKey("x-content-type-options").WhoseValue.Should().Be("nosniff");
        headers.Should().ContainKey("referrer-policy").WhoseValue.Should().Be("same-origin");
        headers.Should().ContainKey("x-frame-options").WhoseValue.Should().Be("DENY");

        headers.Should().ContainKey("content-security-policy");
        var csp = headers["content-security-policy"];
        csp.Should().Contain("default-src 'self'");
        csp.Should().Contain("script-src 'self'", "脚本来源必须收紧到同源");
        csp.Should().Contain("object-src 'none'");
        csp.Should().Contain("frame-ancestors 'none'");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/settings")]
    public async Task Page_LoadsWithoutConsoleErrors(string path)
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        if (path != "/login")
        {
            await AuthTestHelpers.EnsureAuthenticatedAsync(page, fixture.BaseUrl);
        }

        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            // Playwright .NET 的 IConsoleMessage.Type 是字符串（"error" / "warning" / …）
            if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase)
                && !IsExpectedUnauthorizedProbe(message.Text))
            {
                errors.Add($"[console] {message.Text}");
            }
        };
        page.PageError += (_, error) => errors.Add($"[pageerror] {error}");

        await page.GotoAsync($"{fixture.BaseUrl}{path}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30_000
        });

        // 异步资源（字体、模块脚本、SignalR 连接）的失败会在导航完成后才上报，
        // 因此这里需要一个短暂的静默期。这是「等待某些事情不再发生」，
        // 无法用元素等待替代，故保留一个很短的固定等待。
        await page.WaitForTimeoutAsync(1500);

        errors.Should().BeEmpty($"访问 {path} 时不应出现控制台错误或 CSP 违规");
    }

    /// <summary>
    /// 登录页会主动探测 <c>/api/v1/auth/me</c> 以判断是否已登录，未登录时必然返回 401。
    /// 浏览器会把该响应记为一条控制台错误，但它是设计行为，不是需要修复的问题。
    /// </summary>
    private static bool IsExpectedUnauthorizedProbe(string text)
        => text.Contains("Failed to load resource", StringComparison.Ordinal)
           && text.Contains("401", StringComparison.Ordinal);

    [Fact]
    public async Task Page_DoesNotReferenceThirdPartyOrigins()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var thirdPartyRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
                && !uri.IsLoopback
                && !uri.Host.Equals("127.0.0.1", StringComparison.Ordinal)
                && !uri.Host.Equals("localhost", StringComparison.Ordinal))
            {
                thirdPartyRequests.Add(request.Url);
            }
        };

        await AuthTestHelpers.EnsureAuthenticatedAsync(page, fixture.BaseUrl);
        await page.WaitForTimeoutAsync(1000);

        thirdPartyRequests.Should().BeEmpty(
            "自托管应用不应在页面加载时请求任何第三方地址（此前会加载 Google Fonts 与 jsDelivr）");
    }
}
