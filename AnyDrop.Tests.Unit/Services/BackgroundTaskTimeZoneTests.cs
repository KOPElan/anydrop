using AnyDrop.Data;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// S1-07（D6）回归组：后台任务时区 + 对账首轮失败自愈。
/// </summary>
public class BackgroundTaskTimeZoneTests
{
    private static BackgroundTaskTimeZone Create(string? timeZoneId, ILogger<BackgroundTaskTimeZone>? logger = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BackgroundTasks:TimeZoneId"] = timeZoneId
            })
            .Build();
        return new BackgroundTaskTimeZone(config, logger ?? NullLogger<BackgroundTaskTimeZone>.Instance);
    }

    [Fact]
    public void Unset_ShouldUseLocalTimeZone()
    {
        var sut = Create(null);

        sut.TimeZone.Should().Be(TimeZoneInfo.Local);
        sut.Source.Should().Contain("未配置");
    }

    [Fact]
    public void AsiaShanghai_ShouldResolveToConfiguredZone()
    {
        var sut = Create("Asia/Shanghai");

        sut.TimeZone.Id.Should().Be("Asia/Shanghai");
        sut.Source.Should().Contain("Asia/Shanghai");
    }

    [Fact]
    public void InvalidId_ShouldLogErrorAndFallBackToLocal_NeverSilentlyUtc()
    {
        var logger = new ListLogger<BackgroundTaskTimeZone>();
        var sut = Create("Not/AZone", logger);

        sut.TimeZone.Should().Be(TimeZoneInfo.Local);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
        sut.Source.Should().Contain("无效");
    }

    [Fact]
    public void GetNextRunUtc_AsiaShanghai_AlignsToLocalHour()
    {
        var sut = Create("Asia/Shanghai");

        // 2026-01-01 18:00 +08（= 10:00Z），当天 02:00 已过 → 次日 02:00 +08，
        // 即 2026-01-02 02:00+08 = 2026-01-01 18:00Z。
        var afterTwo = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        sut.GetNextRunUtc(2, afterTwo)
            .Should().Be(new DateTimeOffset(2026, 1, 1, 18, 0, 0, TimeSpan.Zero));

        // 2026-01-01 01:00 +08（= 17:00Z 前一天），当天 02:00 未到 → 当天 02:00 +08
        var beforeTwo = new DateTimeOffset(2025, 12, 31, 17, 0, 0, TimeSpan.Zero);
        sut.GetNextRunUtc(2, beforeTwo)
            .Should().Be(new DateTimeOffset(2025, 12, 31, 18, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void TodayLocal_AsiaShanghai_UsesLocalDateNotUtcDate()
    {
        var sut = Create("Asia/Shanghai");

        // 2026-01-01 20:00Z 在 UTC 还是 1 月 1 日，在 +08 已是 1 月 2 日凌晨 4 点
        sut.TodayLocal(new DateTimeOffset(2026, 1, 1, 20, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2026, 1, 2));
    }

    /// <summary>
    /// 对账首轮失败不再终止宿主：首轮抛错后服务继续运行，下一锚定时刻正常执行。
    /// 验收对应计划 S1-07「人为让首轮对账抛错，服务不退出且下一周期正常执行」。
    /// </summary>
    [Fact]
    public async Task OrphanCleanup_FirstRunThrows_ServiceSurvivesAndRunsNextAnchor()
    {
        var start = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        var basePath = Path.Combine(Path.GetTempPath(), $"anydrop-s107-{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);

        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BackgroundTasks:TimeZoneId"] = "UTC",
                    ["Storage:BasePath"] = basePath,
                    ["Storage:OrphanCleanupEnabled"] = "false"
                })
                .Build();

            var options = new DbContextOptionsBuilder<AnyDropDbContext>()
                .UseInMemoryDatabase($"anydrop-s107-{Guid.NewGuid():N}")
                .Options;
            var factory = new TestDbContextFactory(options);

            var attempts = 0;
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<OrphanFileReconciler>(_ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("模拟首轮对账失败");
                }

                return new OrphanFileReconciler(
                    factory, config, clock, NullLogger<OrphanFileReconciler>.Instance);
            });
            var provider = services.BuildServiceProvider();

            var taskTimeZone = new BackgroundTaskTimeZone(config, NullLogger<BackgroundTaskTimeZone>.Instance);
            var sut = new OrphanedFileCleanupService(
                provider, config, clock, taskTimeZone,
                NullLogger<OrphanedFileCleanupService>.Instance);

            using var cts = new CancellationTokenSource();
            await sut.StartAsync(cts.Token);

            // 推进 1 分钟：首轮触发并抛错，必须被捕获、服务不退出
            clock.Advance(TimeSpan.FromMinutes(1));
            await WaitUntilAsync(() => attempts >= 1, TimeSpan.FromSeconds(10));

            // 逐步推进到下一锚点（UTC 凌晨 3 点）：每次 1 小时，中间留真实时间。
            // 不能一次推进 15 小时——首轮抛错后，后台循环要先算出锚点并注册
            // 定时器；若在定时器注册前就把假时间推过锚点，锚点会被算到下一天，
            // 第二轮就不会在测试内触发。
            for (var i = 0; i < 16; i++)
            {
                clock.Advance(TimeSpan.FromHours(1));
                await Task.Delay(100);
            }

            await WaitUntilAsync(() => attempts >= 2, TimeSpan.FromSeconds(10));

            // 停止不应抛出（若 ExecuteAsync 已故障退出，StopAsync 会把异常抛出来）
            await sut.StopAsync(CancellationToken.None);

            attempts.Should().Be(2);
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }

    /// <summary>
    /// 等待后台循环的异步续体跑到指定状态。定时器回调在调用线程同步触发，
    /// 但回调后 Task 的续体走线程池；在并行跑全量测试时固定等待不可靠，
    /// 故轮询等待（真实时间，带上限）。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    /// <summary>捕获日志条目的最小 ILogger，断言用。</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
