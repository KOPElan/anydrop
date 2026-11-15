using AnyDrop.App.Services;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.App;

/// <summary>S3-06：客户端按错误码查本地化文案，码未知时回退服务端原文。</summary>
public class ApiErrorMessagesTests
{
    private static string? FakeLookup(string key) => key switch
    {
        "Error_invalid_credentials" => "Incorrect account or password.",
        "Error_upload_failed" => "Upload failed.",
        _ => null
    };

    [Fact]
    public void Localize_KnownCode_ReturnsLocalizedText()
    {
        ApiErrorMessages.Localize("invalid_credentials", FakeLookup, "账号或密码错误。")
            .Should().Be("Incorrect account or password.");
    }

    [Fact]
    public void Localize_UnknownCode_ReturnsFallback()
    {
        ApiErrorMessages.Localize("some_future_code", FakeLookup, "服务端原文。")
            .Should().Be("服务端原文。");
    }

    [Fact]
    public void Localize_UnknownCodeAndNoFallback_ReturnsNull()
    {
        ApiErrorMessages.Localize("some_future_code", FakeLookup, null).Should().BeNull();
    }

    [Fact]
    public void Localize_NullCode_ReturnsFallback()
    {
        ApiErrorMessages.Localize(null, FakeLookup, "服务端原文。").Should().Be("服务端原文。");
    }

    [Fact]
    public void Localize_KnownCodeWithoutFallback_ReturnsLocalizedText()
    {
        ApiErrorMessages.Localize("upload_failed", FakeLookup, null).Should().Be("Upload failed.");
    }
}
