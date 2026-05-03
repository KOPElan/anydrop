using System.Net;
using AnyDrop.App.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

public class ServerConfigServiceTests
{
    private ServerConfigService CreateSut(HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        return new ServerConfigService(factoryMock.Object);
    }

    [Fact]
    public async Task SetBaseUrlAsync_ThenGetBaseUrl_ReturnsNormalizedUrl()
    {
        var sut = CreateSut();

        await sut.SetBaseUrlAsync("http://localhost:5002");

        sut.GetBaseUrl().Should().Be("http://localhost:5002");
    }

    [Fact]
    public async Task SetBaseUrlAsync_UrlWithTrailingSlash_TrimsSlash()
    {
        var sut = CreateSut();

        await sut.SetBaseUrlAsync("http://localhost:5002/");

        sut.GetBaseUrl().Should().Be("http://localhost:5002");
    }

    [Fact]
    public async Task SetBaseUrlAsync_UrlWithoutScheme_AddsHttpScheme()
    {
        var sut = CreateSut();

        await sut.SetBaseUrlAsync("localhost:5002");

        sut.GetBaseUrl().Should().Be("http://localhost:5002");
    }

    [Fact]
    public async Task ValidateUrlAsync_WhenServerResponds_ReturnsTrue()
    {
        var sut = CreateSut(HttpStatusCode.OK);

        var result = await sut.ValidateUrlAsync("http://localhost:5002");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateUrlAsync_WhenServerUnreachable_ReturnsFalse()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var client = new HttpClient(handlerMock.Object) { Timeout = TimeSpan.FromMilliseconds(100) };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var sut = new ServerConfigService(factoryMock.Object);

        var result = await sut.ValidateUrlAsync("http://unreachable-host:9999");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetHubUrl_ReturnsHubPath()
    {
        var sut = CreateSut();
        await sut.SetBaseUrlAsync("http://localhost:5002");

        sut.GetHubUrl().Should().Be("http://localhost:5002/hubs/share");
    }

    [Fact]
    public void HasBaseUrl_WhenNoUrl_ReturnsFalse()
    {
        var sut = CreateSut();

        sut.HasBaseUrl().Should().BeFalse();
    }

    [Fact]
    public async Task HasBaseUrl_WhenUrlSet_ReturnsTrue()
    {
        var sut = CreateSut();
        await sut.SetBaseUrlAsync("http://localhost:5002");

        sut.HasBaseUrl().Should().BeTrue();
    }
}
