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
            new(Guid.NewGuid(), ShareContentType.Text, "hello", null, null, null, null, null, DateTimeOffset.UtcNow, null, null)
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
        shareServiceMock.Verify(x => x.SendTextAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendTextAsync_ValidRequest_ReturnsOkEnvelope()
    {
        var shareServiceMock = new Mock<IShareService>();
        var dto = new ShareItemDto(Guid.NewGuid(), ShareContentType.Text, "hello", null, null, null, null, null, DateTimeOffset.UtcNow, null, null);

        shareServiceMock
            .Setup(x => x.SendTextAsync("hello", null, false, It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task CleanupAsync_InvalidMonths_ReturnsBadRequest()
    {
        var shareServiceMock = new Mock<IShareService>();

        var result = await ShareItemEndpoints.CleanupOldMessagesAsync(2, shareServiceMock.Object, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequest<ApiEnvelope<object>>>();
        var bad = (BadRequest<ApiEnvelope<object>>)result.Result;
        bad.Value!.Success.Should().BeFalse();
        shareServiceMock.Verify(x => x.CleanupOldMessagesAsync(It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public async Task CleanupAsync_ValidMonths_ReturnsOkWithDeletedCount(int months)
    {
        var shareServiceMock = new Mock<IShareService>();
        shareServiceMock.Setup(x => x.CleanupOldMessagesAsync(months, null, It.IsAny<CancellationToken>())).ReturnsAsync(5);

        var result = await ShareItemEndpoints.CleanupOldMessagesAsync(months, shareServiceMock.Object, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ApiEnvelope<CleanupResult>>>();
        var ok = (Ok<ApiEnvelope<CleanupResult>>)result.Result;
        ok.Value!.Success.Should().BeTrue();
        ok.Value.Data!.DeletedCount.Should().Be(5);
    }

    [Fact]
    public async Task BatchDeleteAsync_EmptyIds_ReturnsBadRequest()
    {
        var shareServiceMock = new Mock<IShareService>();
        var request = new BatchDeleteRequest([]);

        var result = await ShareItemEndpoints.BatchDeleteAsync(request, shareServiceMock.Object, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequest<ApiEnvelope<object>>>();
        var bad = (BadRequest<ApiEnvelope<object>>)result.Result;
        bad.Value!.Success.Should().BeFalse();
        shareServiceMock.Verify(x => x.DeleteShareItemsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BatchDeleteAsync_TooManyIds_ReturnsBadRequest()
    {
        var shareServiceMock = new Mock<IShareService>();
        var ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();
        var request = new BatchDeleteRequest(ids);

        var result = await ShareItemEndpoints.BatchDeleteAsync(request, shareServiceMock.Object, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequest<ApiEnvelope<object>>>();
        shareServiceMock.Verify(x => x.DeleteShareItemsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BatchDeleteAsync_ValidIds_ReturnsOkWithActualDeletedCount()
    {
        var shareServiceMock = new Mock<IShareService>();
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        // 服务实际删除 2 条（1 条 ID 不存在）
        shareServiceMock.Setup(x => x.DeleteShareItemsAsync(ids, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        var request = new BatchDeleteRequest(ids);

        var result = await ShareItemEndpoints.BatchDeleteAsync(request, shareServiceMock.Object, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ApiEnvelope<object>>>();
        var ok = (Ok<ApiEnvelope<object>>)result.Result;
        ok.Value!.Success.Should().BeTrue();
        // 确认返回的是服务层的实际删除数（2），而非请求 ID 数（3）
        var data = ok.Value.Data.Should().NotBeNull().And.Subject;
        var dataJson = System.Text.Json.JsonSerializer.Serialize(data);
        dataJson.Should().Contain("\"deleted\":2");
        shareServiceMock.Verify(x => x.DeleteShareItemsAsync(ids, It.IsAny<CancellationToken>()), Times.Once);
    }
}
