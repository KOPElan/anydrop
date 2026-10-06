using System.Diagnostics;
using Microsoft.Playwright;

namespace AnyDrop.Tests.E2E.Infrastructure;

/// <summary>
/// 为整批 E2E 用例启动一个 AnyDrop 实例。
///
/// 关键点：该实例必须使用**独立的临时数据目录**与**显式配置的 JWT 密钥**。
///
/// 此前 fixture 只设置 ASPNETCORE_ENVIRONMENT=Development，既没有提供密钥
/// （appsettings.Development.json 中该值为空，服务端会拒绝启动），也没有重定向存储路径，
/// 于是测试会直接读写仓库里的真实开发者数据库 data/anydrop.db，并在其中创建用户、主题与消息。
/// 结果是测试既不可重复（第二次运行会因「已完成初始化」而失败），又会污染本地数据。
/// </summary>
public sealed class E2ETestFixture : IAsyncLifetime
{
    /// <summary>至少 32 字符，满足服务端启动校验。</summary>
    private const string TestJwtSecret = "e2e-only-secret-0123456789abcdef0123456789abcdef";

    private Process? _appProcess;
    private string? _tempDataRoot;

    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; } = "http://127.0.0.1:5002";

    public async Task InitializeAsync()
    {
        var repoRoot = ResolveRepoRoot();

        _tempDataRoot = Path.Combine(Path.GetTempPath(), $"anydrop-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataRoot);

        var startInfo = new ProcessStartInfo("dotnet", "run --no-launch-profile --project AnyDrop/AnyDrop.csproj")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        // 环境变量在默认配置顺序中优先于 appsettings.* 与 user secrets，
        // 因此可以确保测试实例既不读取开发者本机的 user secrets，也不触碰仓库内的真实数据。
        startInfo.Environment["Auth__JwtSecret"] = TestJwtSecret;
        startInfo.Environment["Storage__DatabasePath"] = Path.Combine(_tempDataRoot, "anydrop.db");
        startInfo.Environment["Storage__BasePath"] = Path.Combine(_tempDataRoot, "files");

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

        // 必须先停掉应用再删临时目录：进程持有 SQLite 文件句柄。
        // 清理失败不影响测试结论，因此只忽略。
        if (_tempDataRoot is not null && Directory.Exists(_tempDataRoot))
        {
            try
            {
                Directory.Delete(_tempDataRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task WaitForApplicationReadyAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        // 首次运行需要编译，给足时间
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(120);

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (_appProcess is { HasExited: true })
            {
                throw new InvalidOperationException("AnyDrop process exited before startup.");
            }

            try
            {
                // 探测 /health 而不是 setup-status：前者同时验证数据库可查询与存储可写
                var response = await client.GetAsync($"{BaseUrl}/health");
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

        throw new TimeoutException("AnyDrop web app did not start within 120 seconds.");
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
