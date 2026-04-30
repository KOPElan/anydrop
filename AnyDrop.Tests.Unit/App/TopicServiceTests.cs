using System.Net;
using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.App.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

public class TopicServiceTests
{
    private static (TopicService sut, Mock<HttpMessageHandler> handlerMock) CreateSut(HttpResponseMessage response)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("api")).Returns(client);

        return (new TopicService(factoryMock.Object), handlerMock);
    }

    private static TopicDto MakeTopic(bool isPinned = false, bool isArchived = false, int sortOrder = 0) =>
        new(Guid.NewGuid(), "Test Topic", "📁", isPinned, isArchived, sortOrder, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    // ── GetTopicsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTopicsAsync_ReturnsTopicList()
    {
        var topics = new List<TopicDto>
        {
            MakeTopic(isPinned: true, sortOrder: 0),
            MakeTopic(isPinned: false, sortOrder: 1),
        };
        var apiResponse = new ApiResponse<IReadOnlyList<TopicDto>>(true, topics, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var (sut, _) = CreateSut(http);

        var result = await sut.GetTopicsAsync();

        result.Should().HaveCount(2);
        result[0].IsPinned.Should().BeTrue();
        result[1].IsPinned.Should().BeFalse();
    }

    [Fact]
    public async Task GetTopicsAsync_WhenDataIsNull_ReturnsEmptyList()
    {
        var apiResponse = new ApiResponse<IReadOnlyList<TopicDto>>(true, null, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var (sut, _) = CreateSut(http);

        var result = await sut.GetTopicsAsync();

        result.Should().BeEmpty();
    }

    // ── GetArchivedTopicsAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetArchivedTopicsAsync_ReturnsArchivedTopics()
    {
        var archived = new List<TopicDto> { MakeTopic(isArchived: true) };
        var apiResponse = new ApiResponse<IReadOnlyList<TopicDto>>(true, archived, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var (sut, _) = CreateSut(http);

        var result = await sut.GetArchivedTopicsAsync();

        result.Should().HaveCount(1);
        result[0].IsArchived.Should().BeTrue();
    }

    // ── CreateTopicAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CreateTopicAsync_Returns201CreatedTopic()
    {
        var created = MakeTopic();
        var apiResponse = new ApiResponse<TopicDto>(true, created, null);
        var http = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(apiResponse) };
        var (sut, _) = CreateSut(http);

        var result = await sut.CreateTopicAsync(new CreateTopicRequest("Test Topic", "📁"));

        result.Should().NotBeNull();
        result.Name.Should().Be("Test Topic");
    }

    [Fact]
    public async Task CreateTopicAsync_WhenServerError_ThrowsHttpRequestException()
    {
        var http = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var (sut, _) = CreateSut(http);

        await sut.Invoking(s => s.CreateTopicAsync(new CreateTopicRequest("Bad")))
            .Should().ThrowAsync<HttpRequestException>();
    }

    // ── UpdateTopicAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateTopicAsync_Succeeds()
    {
        var http = new HttpResponseMessage(HttpStatusCode.OK);
        var (sut, _) = CreateSut(http);

        await sut.Invoking(s => s.UpdateTopicAsync(Guid.NewGuid(), new UpdateTopicRequest("Renamed")))
            .Should().NotThrowAsync();
    }

    // ── UpdateTopicIconAsync ────────────────────────────────────────────

    [Fact]
    public async Task UpdateTopicIconAsync_Succeeds()
    {
        var http = new HttpResponseMessage(HttpStatusCode.OK);
        var (sut, _) = CreateSut(http);

        await sut.Invoking(s => s.UpdateTopicIconAsync(Guid.NewGuid(), new UpdateTopicIconRequest("🎯")))
            .Should().NotThrowAsync();
    }

    // ── PinTopicAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task PinTopicAsync_WithIsPinnedTrue_Succeeds()
    {
        var http = new HttpResponseMessage(HttpStatusCode.OK);
        HttpRequestMessage? captured = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(http);

        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("api")).Returns(client);
        var sut = new TopicService(factoryMock.Object);

        await sut.PinTopicAsync(Guid.NewGuid(), new PinTopicRequest(true));

        captured!.RequestUri!.ToString().Should().Contain("/pin");
    }

    [Fact]
    public async Task PinTopicAsync_WithIsPinnedFalse_Succeeds()
    {
        var http = new HttpResponseMessage(HttpStatusCode.OK);
        var (sut, _) = CreateSut(http);

        await sut.Invoking(s => s.PinTopicAsync(Guid.NewGuid(), new PinTopicRequest(false)))
            .Should().NotThrowAsync();
    }

    // ── ArchiveTopicAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ArchiveTopicAsync_Succeeds()
    {
        var http = new HttpResponseMessage(HttpStatusCode.OK);
        HttpRequestMessage? captured = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(http);

        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("api")).Returns(client);
        var sut = new TopicService(factoryMock.Object);

        await sut.ArchiveTopicAsync(Guid.NewGuid(), new ArchiveTopicRequest(true));

        captured!.RequestUri!.ToString().Should().Contain("/archive");
    }

    // ── ReorderTopicsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ReorderTopicsAsync_Succeeds()
    {
        var http = new HttpResponseMessage(HttpStatusCode.OK);
        var (sut, _) = CreateSut(http);

        var request = new ReorderTopicsRequest(new[]
        {
            new TopicOrderItem(Guid.NewGuid(), 0),
            new TopicOrderItem(Guid.NewGuid(), 1),
        });

        await sut.Invoking(s => s.ReorderTopicsAsync(request))
            .Should().NotThrowAsync();
    }

    // ── DeleteTopicAsync ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTopicAsync_Succeeds()
    {
        var http = new HttpResponseMessage(HttpStatusCode.NoContent);
        var (sut, _) = CreateSut(http);

        await sut.Invoking(s => s.DeleteTopicAsync(Guid.NewGuid()))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteTopicAsync_WhenNotFound_ThrowsHttpRequestException()
    {
        var http = new HttpResponseMessage(HttpStatusCode.NotFound);
        var (sut, _) = CreateSut(http);

        await sut.Invoking(s => s.DeleteTopicAsync(Guid.NewGuid()))
            .Should().ThrowAsync<HttpRequestException>();
    }
}
