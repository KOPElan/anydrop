using AnyDrop.Models;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Models;

/// <summary>
/// S3-02：限流阈值与 JWT 有效期的配置上下限校验——
/// 误配出「等于关闭保护」的值时应在启动时失败，而不是静默运行。
/// </summary>
public class RateLimitOptionsTests
{
    [Fact]
    public void Validate_Defaults_Passes()
    {
        var options = new RateLimitOptions();

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10001)]
    public void Validate_UploadPerMinuteOutOfRange_Throws(int value)
    {
        var options = new RateLimitOptions { UploadPerMinute = value };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*RateLimit:UploadPerMinute*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void Validate_SearchPerMinuteOutOfRange_Throws(int value)
    {
        var options = new RateLimitOptions { SearchPerMinute = value };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Boundaries_Pass()
    {
        var options = new RateLimitOptions
        {
            UploadPerMinute = 1,
            SearchPerMinute = 10000,
            BatchDeletePerMinute = 1,
            ThumbnailPerMinute = 10000
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }
}
