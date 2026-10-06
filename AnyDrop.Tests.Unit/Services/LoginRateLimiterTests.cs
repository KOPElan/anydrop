using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AnyDrop.Tests.Unit.Services;

public class LoginRateLimiterTests
{
    private static LoginRateLimiter CreateSut(int maxFailures, int cooldownSeconds, TimeProvider? timeProvider = null)
        => new(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AuthOptions
            {
                LoginMaxFailures = maxFailures,
                LoginCooldownSeconds = cooldownSeconds
            }),
            timeProvider ?? TimeProvider.System);

    [Fact]
    public void RegisterFailure_AfterThreshold_ShouldLock()
    {
        var sut = CreateSut(maxFailures: 2, cooldownSeconds: 60);

        sut.RegisterFailure("k");
        sut.RegisterFailure("k");

        var locked = sut.IsLocked("k", out var retry);
        locked.Should().BeTrue();
        retry.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Reset_ShouldClearLock()
    {
        var sut = CreateSut(maxFailures: 1, cooldownSeconds: 60);
        sut.RegisterFailure("k");
        sut.Reset("k");

        sut.IsLocked("k", out _).Should().BeFalse();
    }

    [Fact]
    public void RegisterFailure_AfterCooldownExpired_ShouldResetFailureWindow()
    {
        // 用假时钟推进时间。此前这里使用真实的 Thread.Sleep(1200)，
        // 既拖慢测试套件，又在负载高时不稳定。
        var clock = new FakeTimeProvider();
        var sut = CreateSut(maxFailures: 2, cooldownSeconds: 60, clock);

        sut.RegisterFailure("k");
        sut.RegisterFailure("k");
        sut.IsLocked("k", out _).Should().BeTrue();

        clock.AdvanceSeconds(61);

        // 冷却期已过，锁应解除
        sut.IsLocked("k", out _).Should().BeFalse();

        // 失败窗口已重置：再失败一次不应立刻锁定
        sut.RegisterFailure("k");
        sut.IsLocked("k", out _).Should().BeFalse();
    }

    [Fact]
    public void IsLocked_BeforeCooldownElapses_ShouldStayLocked()
    {
        var clock = new FakeTimeProvider();
        var sut = CreateSut(maxFailures: 1, cooldownSeconds: 60, clock);

        sut.RegisterFailure("k");
        clock.AdvanceSeconds(30);

        sut.IsLocked("k", out var retry).Should().BeTrue();
        retry.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }
}
