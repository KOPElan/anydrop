using AnyDrop.Tests.E2E.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

/// <summary>
/// 跨端实时推送：一端发送，另一端在**不刷新页面**的情况下收到。
///
/// 这是应用的核心功能，`.github/copilot-instructions.md` 也要求至少有一个覆盖
/// 「发送端 → 服务端 → 接收端」链路的用例。
///
/// 此前唯一的 ShareFlowTests 只用了一个页面、断言消息出现在同一个页面上，
/// 实际上并没有验证跨端推送——本用例补上这个缺口。
/// </summary>
[Collection(E2ECollection.Name)]
public class RealtimePushTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task MessageSentOnDeviceA_AppearsOnDeviceB_WithoutReload()
    {
        await using var contextA = await fixture.Browser.NewContextAsync();
        await using var contextB = await fixture.Browser.NewContextAsync();
        var pageA = await contextA.NewPageAsync();
        var pageB = await contextB.NewPageAsync();

        await AuthTestHelpers.EnsureAuthenticatedAsync(pageA, fixture.BaseUrl);
        await AuthTestHelpers.EnsureAuthenticatedAsync(pageB, fixture.BaseUrl);

        // 设备 A 建立主题并发出第一条消息
        var topic = await AuthTestHelpers.CreateTopicAsync(pageA, "推送");
        var firstMessage = AuthTestHelpers.NewMessage("first");

        await pageA.ClickAsync($"button:has-text('{topic}')");
        await AuthTestHelpers.SendMessageAsync(pageA, firstMessage);
        await WaitForMessageAsync(pageA, firstMessage);

        // 设备 B 对齐初始状态：这一步允许刷新，只用于让 B 打开同一个主题
        await pageB.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30_000
        });
        await AuthTestHelpers.WaitForAppReadyAsync(pageB);

        var topicButtonOnB = pageB.Locator($"button:has-text('{topic}')").First;
        await topicButtonOnB.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20_000
        });
        await topicButtonOnB.ClickAsync();
        await WaitForMessageAsync(pageB, firstMessage);

        // 从这里开始统计 B 的导航次数，确保「不刷新」是可验证的事实而非假设
        var navigations = 0;
        pageB.FrameNavigated += (_, _) => Interlocked.Increment(ref navigations);

        // 关键断言：A 再发一条，B 必须在不刷新的情况下收到
        var pushedMessage = AuthTestHelpers.NewMessage("pushed");
        await AuthTestHelpers.SendMessageAsync(pageA, pushedMessage);
        await WaitForMessageAsync(pageB, pushedMessage, timeoutMs: 20_000);

        navigations.Should().Be(0, "跨端推送不应依赖页面刷新");
    }

    private static Task WaitForMessageAsync(IPage page, string text, int timeoutMs = 15_000)
        => page.GetByText(text, new PageGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = timeoutMs
            });
}
