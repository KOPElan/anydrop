using System.Text.Json;
using AnyDrop.Shared;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.Api;

/// <summary>S3-06：错误码必须随信封走 JSON（客户端按码查本地化文案）。</summary>
public class ApiEnvelopeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Fail_WithErrorCode_RoundTripsThroughJson()
    {
        var envelope = ApiEnvelope<string>.Fail("账号或密码错误。", ApiErrorCodes.InvalidCredentials);

        var json = JsonSerializer.Serialize(envelope, Json);
        var parsed = JsonSerializer.Deserialize<ApiEnvelope<string>>(json, Json);

        parsed.Should().NotBeNull();
        parsed!.Success.Should().BeFalse();
        parsed.Error.Should().Be("账号或密码错误。");
        parsed.ErrorCode.Should().Be("invalid_credentials");
        // 码是 Error_ 前缀的资源键来源
        ApiErrorCodes.ResourceKey(parsed.ErrorCode).Should().Be("Error_invalid_credentials");
    }

    [Fact]
    public void Fail_WithoutErrorCode_ErrorCodeIsNull()
    {
        var envelope = ApiEnvelope<string>.Fail("旧客户端兼容路径。");

        envelope.ErrorCode.Should().BeNull();
        envelope.Error.Should().Be("旧客户端兼容路径。");
    }

    [Fact]
    public void Ok_ErrorCodeIsNull()
    {
        var envelope = ApiEnvelope<string>.Ok("ok");

        envelope.Success.Should().BeTrue();
        envelope.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void ResourceKey_NullOrEmpty_ReturnsNull()
    {
        ApiErrorCodes.ResourceKey(null).Should().BeNull();
        ApiErrorCodes.ResourceKey("").Should().BeNull();
    }
}
