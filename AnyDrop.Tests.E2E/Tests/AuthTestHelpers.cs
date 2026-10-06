using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

internal static class AuthTestHelpers
{
    private const string DefaultNickname = "Admin";
    private const string DefaultPassword = "Password1!";

    /// <summary>
    /// 完成初始化并登录，把会话 Cookie 注入浏览器上下文。
    /// </summary>
    public static async Task EnsureAuthenticatedAsync(IPage page, string baseUrl)
    {
        var setupResponse = await page.APIRequest.PostAsync(
            $"{baseUrl}/api/v1/auth/setup",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    nickname = DefaultNickname,
                    password = DefaultPassword,
                    confirmPassword = DefaultPassword
                }
            });

        // 201 = 本次创建成功；409 = 用户已存在。
        // 关键：所有 E2E 用例共享同一个 fixture（因而共享同一个数据库），
        // 只有第一个调用者能拿到 201，其余都会拿到 409——两者都必须接受。
        // （此前只接受 201，导致除第一个用例外的所有用例都会在这里抛异常。
        //   由于 E2E 当时未接入 CI，这个破坏一直没被发现。）
        if (setupResponse.Status is not (201 or 409))
        {
            throw new InvalidOperationException(
                $"Setup failed with status {setupResponse.Status}: {await setupResponse.TextAsync()}");
        }

        var loginResponse = await page.APIRequest.PostAsync(
            $"{baseUrl}/api/v1/auth/login",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    password = DefaultPassword,
                    returnUrl = "/"
                }
            });

        if (!loginResponse.Headers.TryGetValue("set-cookie", out var setCookie))
        {
            throw new InvalidOperationException(
                $"Login did not return a session cookie (status {loginResponse.Status}): {await loginResponse.TextAsync()}");
        }

        var cookiePair = setCookie.Split(';', 2)[0].Split('=', 2);
        if (cookiePair.Length != 2)
        {
            throw new InvalidOperationException($"Unexpected Set-Cookie header: {setCookie}");
        }

        await page.Context.AddCookiesAsync([
            new Cookie
            {
                Name = cookiePair[0],
                Value = cookiePair[1],
                Url = baseUrl
            }
        ]);

        await page.GotoAsync(baseUrl, new PageGotoOptions
        {
            // 用 DOMContentLoaded 而不是默认的 load：随后我们会显式等待页面可交互，
            // 没必要等所有子资源加载完成——那样反而可能被某个慢资源拖到超时。
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30_000
        });
        await WaitForAppReadyAsync(page);
    }

    /// <summary>
    /// 等待页面外壳渲染完成。
    ///
    /// 用 <c>aside.sidebar</c> 的 **Attached** 状态，而不是「新建主题按钮可见」：
    /// 移动端布局（375px）下侧边栏是 <c>display:none</c>，用可见性判断会让移动端用例直接失败。
    /// 需要操作具体控件的调用方各自再等待目标元素。
    /// </summary>
    public static async Task WaitForAppReadyAsync(IPage page)
    {
        await page.Locator("aside.sidebar").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000
        });
    }

    /// <summary>打开「新建主题」弹窗创建主题，返回主题名。</summary>
    public static async Task<string> CreateTopicAsync(IPage page, string prefix = "主题")
    {
        var topic = $"{prefix}-{Guid.NewGuid():N}";
        var newTopicButton = page.Locator("button[aria-label='新建主题']").First;
        var modalInput = page.Locator(".modal-content input[placeholder='输入主题名称（最多100字）']");

        // Blazor 线路刚建立时首次点击可能丢失（页面已渲染但尚未可交互），
        // 因此「打开弹窗」这一步允许重试一次，避免依赖固定 sleep。
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await newTopicButton.ClickAsync();
            try
            {
                await modalInput.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 6_000
                });
                break;
            }
            catch (TimeoutException) when (attempt == 1)
            {
                // 再试一次
            }
        }

        await modalInput.FillAsync(topic);
        await page.ClickAsync(".modal-content button:has-text('创建')");
        await page.WaitForSelectorAsync(
            $"button[data-id] >> text={topic}",
            new PageWaitForSelectorOptions { Timeout = 15_000 });

        return topic;
    }

    /// <summary>在当前主题中发送一条文本消息。</summary>
    public static async Task SendMessageAsync(IPage page, string message)
    {
        // 输入框只在选中主题后可见，这里显式等待，避免用固定 sleep 猜时机
        var input = page.Locator("textarea").First;
        await input.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000
        });

        await input.FillAsync(message);
        await page.ClickAsync("button:has(span:has-text('arrow_upward'))");
    }

    /// <summary>生成不会与既有数据冲突的消息文本。</summary>
    public static string NewMessage(string prefix = "msg") => $"{prefix}-{Guid.NewGuid():N}";
}
