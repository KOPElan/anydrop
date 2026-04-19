using AnyDrop.Tests.E2E.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

[Collection(E2ECollection.Name)]
public class ShareFlowTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task ShareText_SecondBrowserContext_ShouldReceiveMessage()
    {
        await using var contextA = await fixture.Browser.NewContextAsync();
        await using var contextB = await fixture.Browser.NewContextAsync();
        var pageA = await contextA.NewPageAsync();
        var pageB = await contextB.NewPageAsync();

        await pageA.GotoAsync(fixture.BaseUrl);
        await pageB.GotoAsync(fixture.BaseUrl);
        await pageA.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await pageB.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await pageA.WaitForTimeoutAsync(1000);
        await pageB.WaitForTimeoutAsync(1000);

        var topic = $"测试主题-{Guid.NewGuid():N}".Substring(0, 12);
        await pageA.FillAsync("input[placeholder='输入主题名称（最多100字）']", topic);
        await pageA.ClickAsync("button:has-text('+ 新建主题')");
        await pageA.WaitForSelectorAsync($"button[data-id] >> text={topic}");
        await pageB.WaitForSelectorAsync($"button[data-id] >> text={topic}");
        await pageB.ClickAsync($"button:has-text('{topic}')");

        var message = $"hello-{Guid.NewGuid():N}";
        await pageA.FillAsync("textarea", message);
        await pageA.ClickAsync("button:has-text('Send')");

        var remoteMessage = pageB.GetByText(message, new PageGetByTextOptions { Exact = true });
        await remoteMessage.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000,
            State = WaitForSelectorState.Visible
        });

        (await remoteMessage.IsVisibleAsync()).Should().BeTrue();
    }
}
