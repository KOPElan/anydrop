using AnyDrop.Api;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;

namespace AnyDrop.Tests.Unit.Api;

public class ShareItemEndpointsTests
{
    [Fact]
    public async Task GetRecentAsync_UsesDefaultCountWhenMissing_ReturnsOkEnvelope()
    {
        var shareServiceMock = new Mock<IShareService>();
        var items = new List<ShareItemDto>
        {
            new(Guid.NewGuid(), ShareContentType.Text, "hello", null, null, null, null, null, DateTimeOffset.UtcNow, null)
        };

        shareServiceMock
            .Setup(x => x.GetRecentAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var result = await ShareItemEndpoints.GetRecentAsync(null, shareServiceMock.Object, CancellationToken.None);

        result.Should().BeOfType<Ok<ApiEnvelope<IReadOnlyList<ShareItemDto>>>>();
        result.Value.Should().NotBeNull();
        result.Value.Success.Should().BeTrue();
        result.Value.Data.Should().ContainSingle(x => x.Content == "hello");
        shareServiceMock.Verify(x => x.GetRecentAsync(50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTextAsync_EmptyContent_ReturnsBadRequestEnvelope()
    {
        var shareServiceMock = new Mock<IShareService>();

        var result = await ShareItemEndpoints.SendTextAsync(
            new CreateTextShareItemRequest("  ", null),
            shareServiceMock.Object,
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequest<ApiEnvelope<ShareItemDto>>>();
        var badRequest = (BadRequest<ApiEnvelope<ShareItemDto>>)result.Result;
        badRequest.Value.Should().NotBeNull();
        badRequest.Value.Success.Should().BeFalse();
        badRequest.Value.Error.Should().Be("Content is required.");
        shareServiceMock.Verify(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendTextAsync_ValidRequest_ReturnsOkEnvelope()
    {
        var shareServiceMock = new Mock<IShareService>();
        var dto = new ShareItemDto(Guid.NewGuid(), ShareContentType.Text, "hello", null, null, null, null, null, DateTimeOffset.UtcNow, null);

        shareServiceMock
            .Setup(x => x.SendTextAsync("hello", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await ShareItemEndpoints.SendTextAsync(
            new CreateTextShareItemRequest("hello", null),
            shareServiceMock.Object,
            CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ApiEnvelope<ShareItemDto>>>();
        var ok = (Ok<ApiEnvelope<ShareItemDto>>)result.Result;
        ok.Value.Should().NotBeNull();
        ok.Value.Success.Should().BeTrue();
        ok.Value.Data.Should().NotBeNull();
        ok.Value.Data!.Content.Should().Be("hello");
    }
}
