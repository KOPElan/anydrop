using AnyDrop.Tests.E2E.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

[Collection(E2ECollection.Name)]
public class TopicSidebarTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task CreateTopic_SendMessage_CurrentWindowShouldUpdateOrder()
    {
        await using var contextA = await fixture.Browser.NewContextAsync();
        var pageA = await contextA.NewPageAsync();

        await AuthTestHelpers.EnsureAuthenticatedAsync(pageA, fixture.BaseUrl);

        var topicA = $"主题A-{Guid.NewGuid():N}";
        var topicB = $"主题B-{Guid.NewGuid():N}";

        // 通过图标按钮打开 Modal 创建主题 A
        await pageA.ClickAsync("button[aria-label='新建主题']");
        await pageA.FillAsync(".modal-content input[placeholder='输入主题名称（最多100字）']", topicA);
        await pageA.ClickAsync(".modal-content button:has-text('创建')");
        await pageA.WaitForSelectorAsync($"button[data-id] >> text={topicA}");

        // 通过图标按钮打开 Modal 创建主题 B
        await pageA.ClickAsync("button[aria-label='新建主题']");
        await pageA.FillAsync(".modal-content input[placeholder='输入主题名称（最多100字）']", topicB);
        await pageA.ClickAsync(".modal-content button:has-text('创建')");
        await pageA.WaitForSelectorAsync($"button[data-id] >> text={topicB}");

        await pageA.ClickAsync($"button:has-text('{topicB}')");
        var message = $"msg-{Guid.NewGuid():N}";
        await pageA.FillAsync("textarea", message);
        await pageA.ClickAsync("button:has(span:has-text('arrow_upward'))");

        await pageA.WaitForTimeoutAsync(1500);
        var topicTexts = await pageA.Locator("#topic-list > button").AllInnerTextsAsync();
        var indexA = topicTexts.Select((text, index) => new { text, index }).FirstOrDefault(x => x.text.Contains(topicA, StringComparison.Ordinal))?.index ?? -1;
        var indexB = topicTexts.Select((text, index) => new { text, index }).FirstOrDefault(x => x.text.Contains(topicB, StringComparison.Ordinal))?.index ?? -1;
        indexA.Should().BeGreaterThanOrEqualTo(0);
        indexB.Should().BeGreaterThanOrEqualTo(0);
        indexB.Should().BeLessThan(indexA);
    }
}
