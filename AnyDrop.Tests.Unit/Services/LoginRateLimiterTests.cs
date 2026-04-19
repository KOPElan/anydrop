using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AnyDrop.Tests.Unit.Services;

public class LoginRateLimiterTests
{
    [Fact]
    public void RegisterFailure_AfterThreshold_ShouldLock()
    {
        var sut = new LoginRateLimiter(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AuthOptions { LoginMaxFailures = 2, LoginCooldownSeconds = 60 }));

        sut.RegisterFailure("k");
        sut.RegisterFailure("k");

        var locked = sut.IsLocked("k", out var retry);
        locked.Should().BeTrue();
        retry.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Reset_ShouldClearLock()
    {
        var sut = new LoginRateLimiter(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new AuthOptions { LoginMaxFailures = 1, LoginCooldownSeconds = 60 }));
        sut.RegisterFailure("k");
        sut.Reset("k");

        var locked = sut.IsLocked("k", out _);
        locked.Should().BeFalse();
    }
}
