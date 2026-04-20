using AnyDrop.Tests.E2E.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

[Collection(E2ECollection.Name)]
public class LayoutTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task Sidebar_ShouldBeVisibleOnDesktopAndHiddenOnMobile_WithoutHorizontalScroll()
    {
        await using var desktopContext = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 }
        });
        var desktopPage = await desktopContext.NewPageAsync();
        await AuthTestHelpers.EnsureAuthenticatedAsync(desktopPage, fixture.BaseUrl);
        (await desktopPage.IsVisibleAsync("aside.sidebar")).Should().BeTrue();

        await using var mobileContext = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 375, Height = 667 }
        });
        var mobilePage = await mobileContext.NewPageAsync();
        await AuthTestHelpers.EnsureAuthenticatedAsync(mobilePage, fixture.BaseUrl);
        await mobilePage.WaitForFunctionAsync(
            "() => getComputedStyle(document.querySelector('aside.sidebar')).display === 'none'");
        (await mobilePage.IsVisibleAsync("aside.sidebar")).Should().BeFalse();

        var hasNoHorizontalOverflow = await mobilePage.EvaluateAsync<bool>(
            "document.body.scrollWidth <= window.innerWidth");
        hasNoHorizontalOverflow.Should().BeTrue();
    }
}
