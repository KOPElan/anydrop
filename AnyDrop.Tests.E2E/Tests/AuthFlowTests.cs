using AnyDrop.Tests.E2E.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

[Collection(E2ECollection.Name)]
public class AuthFlowTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task UnauthenticatedUser_ShouldRedirectToLogin()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        (page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase)
            || page.Url.Contains("/setup", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public async Task SetupLoginLogout_ProtectedApi_ShouldReturn401AfterLogout()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await AuthTestHelpers.EnsureAuthenticatedAsync(page, fixture.BaseUrl);

        var beforeLogout = await page.EvaluateAsync<int>("""async () => (await fetch('/api/v1/settings/security', { credentials: 'include' })).status""");
        beforeLogout.Should().Be(200);

        await page.ClickAsync("button:has-text('登出')");
        await page.WaitForURLAsync("**/login*");

        var afterLogout = await page.EvaluateAsync<int>("""async () => (await fetch('/api/v1/settings/security', { credentials: 'include' })).status""");
        afterLogout.Should().Be(401);
    }
}
