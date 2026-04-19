using AnyDrop.Tests.E2E.Infrastructure;
using FluentAssertions;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

[Collection(E2ECollection.Name)]
public class TopicSidebarTests(E2ETestFixture fixture)
{
    [Fact]
    public async Task CreateTopic_SendMessage_OtherWindowShouldReceiveRealtimeOrderUpdate()
    {
        await using var contextA = await fixture.Browser.NewContextAsync();
        await using var contextB = await fixture.Browser.NewContextAsync();
        var pageA = await contextA.NewPageAsync();
        var pageB = await contextB.NewPageAsync();

        await pageA.GotoAsync(fixture.BaseUrl);
        await pageB.GotoAsync(fixture.BaseUrl);
        await pageA.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await pageB.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var topicA = $"主题A-{Guid.NewGuid():N}";
        var topicB = $"主题B-{Guid.NewGuid():N}";

        await pageA.FillAsync("input[placeholder='输入主题名称（最多100字）']", topicA);
        await pageA.ClickAsync("button:has-text('+ 新建主题')");
        await pageA.WaitForSelectorAsync($"button[data-id] >> text={topicA}");

        await pageA.FillAsync("input[placeholder='输入主题名称（最多100字）']", topicB);
        await pageA.ClickAsync("button:has-text('+ 新建主题')");
        await pageA.WaitForSelectorAsync($"button[data-id] >> text={topicB}");

        await pageA.ClickAsync($"button:has-text('{topicB}')");
        var message = $"msg-{Guid.NewGuid():N}";
        await pageA.FillAsync("textarea", message);
        await pageA.ClickAsync("button:has-text('Send')");

        await pageB.WaitForTimeoutAsync(1500);
        var topicTexts = await pageB.Locator("#topic-list > button").AllInnerTextsAsync();
        var indexA = topicTexts.Select((text, index) => new { text, index }).FirstOrDefault(x => x.text.Contains(topicA, StringComparison.Ordinal))?.index ?? -1;
        var indexB = topicTexts.Select((text, index) => new { text, index }).FirstOrDefault(x => x.text.Contains(topicB, StringComparison.Ordinal))?.index ?? -1;
        indexA.Should().BeGreaterThanOrEqualTo(0);
        indexB.Should().BeGreaterThanOrEqualTo(0);
        indexB.Should().BeLessThan(indexA);
    }
}
