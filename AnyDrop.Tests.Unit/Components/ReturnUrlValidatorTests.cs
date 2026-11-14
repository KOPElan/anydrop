using AnyDrop.Components.Pages;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Components;

/// <summary>S0-01（SEC-01）：登录后 returnUrl 只允许站内相对路径。</summary>
public class ReturnUrlValidatorTests
{
    [Theory]
    [InlineData("https://evil.example/login")]
    [InlineData("http://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("///evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("\\evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/search?q=https://evil.example/")]
    [InlineData("%2F%2Fevil.example/")]
    [InlineData("%2f%2fevil.example%2f")]
    [InlineData("/%2Fevil.example/")]
    [InlineData("/foo\n/bar")]
    [InlineData("relative/path")]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeReturnUrl_UnsafeOrNonLocal_ReturnsRoot(string? returnUrl)
    {
        Login.SanitizeReturnUrl(returnUrl).Should().Be("/");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/topics")]
    [InlineData("/topics/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("/?topicId=3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("/search?q=hello")]
    public void SanitizeReturnUrl_SafeLocalPath_IsPreserved(string returnUrl)
    {
        Login.SanitizeReturnUrl(returnUrl).Should().Be(returnUrl);
    }

    [Fact]
    public void SanitizeReturnUrl_Null_ReturnsRoot()
    {
        Login.SanitizeReturnUrl(null).Should().Be("/");
    }
}
