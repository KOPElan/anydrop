using System.Diagnostics;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Infrastructure;

public sealed class E2ETestFixture : IAsyncLifetime
{
    private Process? _appProcess;
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; } = "http://127.0.0.1:5002";

    public async Task InitializeAsync()
    {
        var repoRoot = ResolveRepoRoot();
        var startInfo = new ProcessStartInfo("dotnet", "run --no-launch-profile --project AnyDrop/AnyDrop.csproj")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        _appProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start AnyDrop application process.");
        await WaitForApplicationReadyAsync();

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        Playwright?.Dispose();

        if (_appProcess is { HasExited: false })
        {
            _appProcess.Kill(true);
            await _appProcess.WaitForExitAsync();
        }
    }

    private async Task WaitForApplicationReadyAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(45);

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (_appProcess is { HasExited: true })
            {
                throw new InvalidOperationException("AnyDrop process exited before startup.");
            }

            try
            {
                var response = await client.GetAsync(BaseUrl);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Swallow and retry until timeout.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("AnyDrop web app did not start within 45 seconds.");
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AnyDrop.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing AnyDrop.slnx.");
    }
}
