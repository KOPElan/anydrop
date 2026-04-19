using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Tests;

internal static class AuthTestHelpers
{
    private const string DefaultNickname = "Admin";
    private const string DefaultPassword = "Password1!";

    public static async Task EnsureAuthenticatedAsync(IPage page, string baseUrl)
    {
        await page.APIRequest.PostAsync(
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

        if (loginResponse.Headers.TryGetValue("set-cookie", out var setCookie))
        {
            var cookiePair = setCookie.Split(';', 2)[0].Split('=', 2);
            if (cookiePair.Length == 2)
            {
                await page.Context.AddCookiesAsync([
                    new Cookie
                    {
                        Name = cookiePair[0],
                        Value = cookiePair[1],
                        Url = baseUrl
                    }
                ]);
            }
        }

        await page.GotoAsync(baseUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
