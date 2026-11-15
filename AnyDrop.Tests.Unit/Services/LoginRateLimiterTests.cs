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

    [Fact]
    public void IsLocked_BetweenFailures_MustNotResetFailureCount()
    {
        // 这条用例刻意模拟真实调用顺序：AuthService 在每次登录尝试前都会调用
        // IsLocked，因此失败计数必须能跨过这些调用继续累计。
        //
        // 修复前 IsLocked 会在 LockedUntil 为 null 时移除缓存条目，导致计数每次
        // 都被清零（FailedCount 永远是 1），默认阈值 5 下永远不会锁定——
        // 登录限流在真实使用中完全失效。
        var sut = CreateSut(maxFailures: 5, cooldownSeconds: 60);

        for (var i = 1; i <= 5; i++)
        {
            sut.RegisterFailure("k");
            var locked = sut.IsLocked("k", out _);
            locked.Should().Be(i == 5, $"第 {i} 次失败后的锁定状态（阈值 5）");
        }
    }

    [Fact]
    public void RegisterFailure_RepeatedLockouts_BackOffExponentially()
    {
        var clock = new FakeTimeProvider();
        var sut = CreateSut(maxFailures: 1, cooldownSeconds: 60, clock);

        // 第 1 次锁定：60 秒
        sut.RegisterFailure("k");
        sut.IsLocked("k", out var first).Should().BeTrue();
        first.Should().BeCloseTo(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1));

        clock.AdvanceSeconds(61);

        // 第 2 次锁定：120 秒
        sut.RegisterFailure("k");
        sut.IsLocked("k", out var second).Should().BeTrue();
        second.Should().BeCloseTo(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(1));

        clock.AdvanceSeconds(121);

        // 第 3 次锁定：240 秒
        sut.RegisterFailure("k");
        sut.IsLocked("k", out var third).Should().BeTrue();
        third.Should().BeCloseTo(TimeSpan.FromSeconds(240), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RegisterFailure_BackOff_IsCappedAtFifteenMinutes()
    {
        var clock = new FakeTimeProvider();
        var sut = CreateSut(maxFailures: 1, cooldownSeconds: 60, clock);

        for (var i = 0; i < 10; i++)
        {
            sut.RegisterFailure("k");
            clock.AdvanceSeconds(16 * 60);
        }

        sut.RegisterFailure("k");
        sut.IsLocked("k", out var retry).Should().BeTrue();
        retry.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LockRemainsInEffect_EvenWhenBackOffExceedsLegacyCacheTtl()
    {
        // 退避会把锁定延长到远超 cooldown*2 的时长。缓存条目的过期时间必须覆盖
        // 整个锁定期，否则条目会先于锁定被清除，限流静默失效。
        var clock = new FakeTimeProvider();
        var sut = new LoginRateLimiter(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AuthOptions { LoginMaxFailures = 1, LoginCooldownSeconds = 60 }),
            clock);

        // 逐次触发，把当前锁定推长到 240 秒
        sut.RegisterFailure("k");
        for (var i = 0; i < 2; i++)
        {
            clock.AdvanceSeconds(61 * (1 << i) + 1);
            sut.RegisterFailure("k");
        }

        sut.IsLocked("k", out var retry).Should().BeTrue();
        retry.Should().BeGreaterThan(TimeSpan.FromSeconds(120),
            "退避后的锁定时长已超过旧实现使用的 cooldown*2 缓存 TTL");

        // 推进到锁定即将结束，仍应处于锁定状态（说明缓存条目没有提前过期）
        clock.Advance(retry - TimeSpan.FromSeconds(5));
        sut.IsLocked("k", out _).Should().BeTrue("缓存条目不应提前过期");
    }

    [Fact]
    public void Reset_ClearsBackOffHistory()
    {
        var clock = new FakeTimeProvider();
        var sut = CreateSut(maxFailures: 1, cooldownSeconds: 60, clock);

        sut.RegisterFailure("k");
        sut.Reset("k");

        // 成功登录后重新计数，退避历史应一并清除
        sut.RegisterFailure("k");
        sut.IsLocked("k", out var retry).Should().BeTrue();
        retry.Should().BeCloseTo(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1));
    }
}
