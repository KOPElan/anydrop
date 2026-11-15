using AnyDrop.App.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.App;

/// <summary>S2-04：手动重连退避序列（1s → 30s 封顶）与抖动范围。</summary>
public class SignalRReconnectTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    [InlineData(6, 30)]
    [InlineData(100, 30)]
    public void BackoffForAttempt_FollowsExponentialSequenceCappedAt30s(int attempt, int expectedSeconds)
    {
        SignalRService.BackoffForAttempt(attempt).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void WithJitter_StaysWithin25PercentOfDelay()
    {
        var delay = TimeSpan.FromSeconds(30);

        for (var i = 0; i < 200; i++)
        {
            var jittered = SignalRService.WithJitter(delay);
            jittered.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(22.5));
            jittered.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(37.5));
        }
    }

    [Fact]
    public void WithJitter_NeverBelow100ms()
    {
        SignalRService.WithJitter(TimeSpan.FromMilliseconds(10))
            .Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
    }
}
