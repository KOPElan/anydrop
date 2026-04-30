using System.Net;
using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.App.Services;
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

    private static ShareItemDto MakeTextItem(Guid topicId, string text) =>
        new(Guid.NewGuid(), topicId, ShareContentType.Text, text, null, null, null, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task SearchAsync_ReturnsMatchingItems()
    {
        var topicId = Guid.NewGuid();
        var items = new List<ShareItemDto> { MakeTextItem(topicId, "hello world") };
        var apiResponse = new ApiResponse<IReadOnlyList<ShareItemDto>>(true, items, null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        var result = await sut.SearchAsync(topicId, "hello");

        result.Should().HaveCount(1);
        result[0].TextContent.Should().Be("hello world");
    }

    [Fact]
    public async Task SearchAsync_WhenEmptyResult_ReturnsEmptyList()
    {
        var apiResponse = new ApiResponse<IReadOnlyList<ShareItemDto>>(true, [], null);
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
        var apiResponse = new ApiResponse<IReadOnlyList<ShareItemDto>>(true, items, null);
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
        var apiResponse = new ApiResponse<ActiveDatesResponse>(true, new ActiveDatesResponse(dates), null);
        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var sut = CreateSut(http);

        var result = await sut.GetActiveDatesAsync(topicId, DateTime.Today.Year, DateTime.Today.Month);

        result.Should().HaveCount(1);
    }
}
