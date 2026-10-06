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
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await AuthTestHelpers.EnsureAuthenticatedAsync(page, fixture.BaseUrl);

        var topicA = await AuthTestHelpers.CreateTopicAsync(page, "主题A");
        var topicB = await AuthTestHelpers.CreateTopicAsync(page, "主题B");

        // 在主题 B 中发一条消息，B 应因「最新消息」排到 A 之前
        await page.ClickAsync($"button:has-text('{topicB}')");
        await AuthTestHelpers.SendMessageAsync(page, AuthTestHelpers.NewMessage());

        // 等待侧边栏顺序真正更新，而不是固定 sleep 1.5 秒。
        // 这样既更快，也在失败时给出明确的「条件未满足」而不是「顺序恰好不对」。
        await page.WaitForFunctionAsync(
            """
            ([a, b]) => {
                const buttons = Array.from(document.querySelectorAll('#topic-list > button'));
                const ia = buttons.findIndex(x => x.innerText.includes(a));
                const ib = buttons.findIndex(x => x.innerText.includes(b));
                return ia >= 0 && ib >= 0 && ib < ia;
            }
            """,
            new[] { topicA, topicB },
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        var texts = await page.Locator("#topic-list > button").AllInnerTextsAsync();
        var ordered = texts.ToList();
        var indexA = ordered.FindIndex(t => t.Contains(topicA, StringComparison.Ordinal));
        var indexB = ordered.FindIndex(t => t.Contains(topicB, StringComparison.Ordinal));

        indexA.Should().BeGreaterThanOrEqualTo(0);
        indexB.Should().BeGreaterThanOrEqualTo(0);
        indexB.Should().BeLessThan(indexA);
    }
}
