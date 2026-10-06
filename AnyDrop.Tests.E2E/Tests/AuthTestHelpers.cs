using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

internal static class AuthTestHelpers
{
    private const string DefaultNickname = "Admin";
    private const string DefaultPassword = "Password1!";

    /// <summary>
    /// 在全新数据库上完成初始化并登录，把会话 Cookie 注入浏览器上下文。
    ///
    /// 每一步都显式校验结果：此前完全忽略响应，一旦安装状态不符合预期
    /// （例如数据库里已有另一个密码），失败会推迟到某个无关的 UI 断言上，
    /// 极难定位。
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

        // 201 = 本次创建成功；409 = 初始化已完成（fixture 保证是全新库，出现即说明状态异常）
        if (setupResponse.Status is not 201)
        {
            throw new InvalidOperationException(
                $"Setup failed with status {setupResponse.Status}: {await setupResponse.TextAsync()}. " +
                "E2E 运行应使用全新数据库，请确认 E2ETestFixture 的临时数据目录已生效。");
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

        await page.GotoAsync(baseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
