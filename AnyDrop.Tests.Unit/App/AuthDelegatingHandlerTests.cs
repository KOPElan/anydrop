using System.Net;
using AnyDrop.App.Infrastructure;
using AnyDrop.App.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

public class AuthDelegatingHandlerTests
{
    private readonly Mock<ISecureTokenStorage> _tokenStorageMock = new();
    private readonly AppEventBus _eventBus = new();

    private HttpClient CreateHttpClient(HttpStatusCode statusCode)
    {
        var innerHandler = new Mock<HttpMessageHandler>();
        innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode));

        var handler = new AuthDelegatingHandler(_tokenStorageMock.Object, _eventBus)
        {
            InnerHandler = innerHandler.Object
        };

        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    [Fact]
    public async Task SendAsync_WhenTokenAvailable_AddsBearerHeader()
    {
        _tokenStorageMock.Setup(t => t.GetTokenAsync()).ReturnsAsync("my-jwt-token");
        HttpRequestMessage? capturedRequest = null;

        var innerHandler = new Mock<HttpMessageHandler>();
        innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var handler = new AuthDelegatingHandler(_tokenStorageMock.Object, _eventBus)
        {
            InnerHandler = innerHandler.Object
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        await client.GetAsync("/test");

        capturedRequest!.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("my-jwt-token");
    }

    [Fact]
    public async Task SendAsync_WhenNoToken_DoesNotAddAuthHeader()
    {
        _tokenStorageMock.Setup(t => t.GetTokenAsync()).ReturnsAsync((string?)null);
        HttpRequestMessage? capturedRequest = null;

        var innerHandler = new Mock<HttpMessageHandler>();
        innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var handler = new AuthDelegatingHandler(_tokenStorageMock.Object, _eventBus)
        {
            InnerHandler = innerHandler.Object
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        await client.GetAsync("/test");

        capturedRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WhenUnauthorizedResponse_ClearsTokenAndRaisesEvent()
    {
        _tokenStorageMock.Setup(t => t.GetTokenAsync()).ReturnsAsync("expired-token");
        bool eventRaised = false;
        _eventBus.AuthExpired += () => eventRaised = true;

        var client = CreateHttpClient(HttpStatusCode.Unauthorized);

        await client.GetAsync("/api/resource");

        _tokenStorageMock.Verify(t => t.ClearTokenAsync(), Times.Once);
        eventRaised.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WhenOkResponse_DoesNotClearToken()
    {
        _tokenStorageMock.Setup(t => t.GetTokenAsync()).ReturnsAsync("valid-token");

        var client = CreateHttpClient(HttpStatusCode.OK);

        await client.GetAsync("/api/resource");

        _tokenStorageMock.Verify(t => t.ClearTokenAsync(), Times.Never);
    }
}
