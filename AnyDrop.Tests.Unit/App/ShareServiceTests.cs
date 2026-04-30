using System.Net;
using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.App.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

public class ShareServiceTests
{
    private static (ShareService sut, Mock<HttpMessageHandler> handlerMock) CreateSut(HttpResponseMessage response)
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

        return (new ShareService(factoryMock.Object), handlerMock);
    }

    private static ShareItemDto MakeTextItem(Guid topicId, string text) =>
        new(Guid.NewGuid(), topicId, ShareContentType.Text, text, null, null, null, null, null, null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetMessagesAsync_ReturnsItems()
    {
        var topicId = Guid.NewGuid();
        var items = new List<ShareItemDto> { MakeTextItem(topicId, "hello") };
        var data = new TopicMessagesResponse(items, false, null);
        var apiResponse = new ApiResponse<TopicMessagesResponse>(true, data, null);

        var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) };
        var (sut, _) = CreateSut(http);

        var result = await sut.GetMessagesAsync(topicId);

        result.Items.Should().HaveCount(1);
        result.Items[0].TextContent.Should().Be("hello");
    }

    [Fact]
    public async Task SendTextAsync_ReturnsCreatedItem()
    {
        var topicId = Guid.NewGuid();
        var item = MakeTextItem(topicId, "new text");
        var apiResponse = new ApiResponse<ShareItemDto>(true, item, null);

        var http = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(apiResponse) };
        var (sut, _) = CreateSut(http);

        var result = await sut.SendTextAsync(new CreateTextShareItemRequest(topicId, "new text"));

        result.TextContent.Should().Be("new text");
        result.TopicId.Should().Be(topicId);
    }

    [Fact]
    public async Task GetMessagesAsync_WithBeforeCursor_IncludesParam()
    {
        var topicId = Guid.NewGuid();
        var data = new TopicMessagesResponse([], false, null);
        var apiResponse = new ApiResponse<TopicMessagesResponse>(true, data, null);

        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(apiResponse) });

        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("api")).Returns(client);
        var sut = new ShareService(factoryMock.Object);

        await sut.GetMessagesAsync(topicId, before: "cursor-123");

        capturedRequest!.RequestUri!.Query.Should().Contain("before=cursor-123");
    }
}
