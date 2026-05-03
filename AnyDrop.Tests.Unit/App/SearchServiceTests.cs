using System.Net;
using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.App.Services;
using AnyDrop.Shared;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

public class SearchServiceTests
{
    private static SearchService CreateSut(HttpResponseMessage response)
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

        return new SearchService(factoryMock.Object);
    }

    // ShareItemDto: (Guid Id, ShareContentType ContentType, string Content, string? FileName,
    //   long? FileSize, string? MimeType, string? LinkTitle, string? LinkDescription,
    //   DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, Guid? TopicId)
    private static ShareItemDto MakeTextItem(Guid topicId, string text) =>
        new(Guid.NewGuid(), ShareContentType.Text, text, null, null, null, null, null, DateTimeOffset.UtcNow, null, topicId);

    [Fact]
    public async Task SearchAsync_ReturnsMatchingItems()
    {
        var topicId = Guid.NewGuid();
        var items = new List<ShareItemDto> { MakeTextItem(topicId, "hello world") };
        var messagesResponse = new TopicMessagesResponse(items, false, null);
        var apiResponse = new ApiEnvelope<TopicMessagesResponse>(true, messagesResponse, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        var result = await sut.SearchAsync(topicId, "hello");

        result.Should().HaveCount(1);
        result[0].Content.Should().Be("hello world");
    }

    [Fact]
    public async Task SearchAsync_WhenEmptyResult_ReturnsEmptyList()
    {
        var messagesResponse = new TopicMessagesResponse([], false, null);
        var apiResponse = new ApiEnvelope<TopicMessagesResponse>(true, messagesResponse, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        var result = await sut.SearchAsync(Guid.NewGuid(), "no-match");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByDateAsync_ReturnsItemsForDate()
    {
        var topicId = Guid.NewGuid();
        var items = new List<ShareItemDto> { MakeTextItem(topicId, "today msg") };
        var apiResponse = new ApiEnvelope<IReadOnlyList<ShareItemDto>>(true, items, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        var result = await sut.GetByDateAsync(topicId, DateOnly.FromDateTime(DateTime.Today));

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetActiveDatesAsync_ReturnsDates()
    {
        var topicId = Guid.NewGuid();
        var dates = new List<DateOnly> { DateOnly.FromDateTime(DateTime.Today) };
        // 服务端直接返回 IReadOnlyList<DateOnly>，不包装在对象中
        var apiResponse = new ApiEnvelope<IReadOnlyList<DateOnly>>(true, dates, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        var result = await sut.GetActiveDatesAsync(topicId, DateTime.Today.Year, DateTime.Today.Month);

        result.Should().HaveCount(1);
    }
}

