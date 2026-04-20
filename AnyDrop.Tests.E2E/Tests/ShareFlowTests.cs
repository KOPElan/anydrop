using AnyDrop.Tests.E2E.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

[Collection(E2ECollection.Name)]
public class ShareFlowTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task ShareText_ShouldDisplayMessageInCurrentPage()
    {
        await using var contextA = await fixture.Browser.NewContextAsync();
        var pageA = await contextA.NewPageAsync();

        await AuthTestHelpers.EnsureAuthenticatedAsync(pageA, fixture.BaseUrl);
        await pageA.WaitForTimeoutAsync(1000);

        var topic = $"测试主题-{Guid.NewGuid():N}".Substring(0, 12);
        await pageA.ClickAsync("button[aria-label='新建主题']");
        await pageA.FillAsync(".modal-content input[placeholder='输入主题名称（最多100字）']", topic);
        await pageA.ClickAsync(".modal-content button:has-text('创建')");
        await pageA.WaitForSelectorAsync($"button[data-id] >> text={topic}");

        var message = $"hello-{Guid.NewGuid():N}";
        await pageA.FillAsync("textarea", message);
        await pageA.ClickAsync("button:has(span:has-text('arrow_upward'))");

        var remoteMessage = pageA.GetByText(message, new PageGetByTextOptions { Exact = true });
        await remoteMessage.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000,
            State = WaitForSelectorState.Visible
        });

        (await remoteMessage.IsVisibleAsync()).Should().BeTrue();
    }
}
