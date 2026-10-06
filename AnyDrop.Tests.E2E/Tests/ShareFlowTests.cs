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
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await AuthTestHelpers.EnsureAuthenticatedAsync(page, fixture.BaseUrl);

        var topic = await AuthTestHelpers.CreateTopicAsync(page, "测试主题");
        await page.ClickAsync($"button:has-text('{topic}')");

        var message = AuthTestHelpers.NewMessage("hello");
        await AuthTestHelpers.SendMessageAsync(page, message);

        // 等待消息真正出现，而不是固定 sleep 一段时间
        await page.GetByText(message, new PageGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15_000
            });
    }
}
