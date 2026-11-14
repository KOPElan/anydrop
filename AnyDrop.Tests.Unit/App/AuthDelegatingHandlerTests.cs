using System.Net;
using System.Net.Http.Json;
using AnyDrop.App.Infrastructure;
using AnyDrop.App.Services;
using AnyDrop.Shared;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

/// <summary>
/// S2-01 / S2-03：401 处理器分流——登录端点不触发过期、401 先刷新重试一次、
/// 刷新失败才清 token 跳登录、并发 401 只跳一次。
/// </summary>
public class AuthDelegatingHandlerTests
{
    private readonly Mock<ISecureTokenStorage> _tokenStorageMock = new();
    private readonly Mock<ITokenRefreshService> _refreshMock = new();
    private readonly AppEventBus _eventBus = new();

    public AuthDelegatingHandlerTests()
    {
        _refreshMock.Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    private HttpClient CreateHttpClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Action<HttpRequestMessage>? capture = null)
    {
        var innerHandler = new Mock<HttpMessageHandler>();
        innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                capture?.Invoke(req);
                return responder(req);
            });

        var handler = new AuthDelegatingHandler(_tokenStorageMock.Object, _eventBus, _refreshMock.Object)
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

        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK), req => capturedRequest = req);

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

        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK), req => capturedRequest = req);

        await client.GetAsync("/test");

        capturedRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_WhenUnauthorizedAndRefreshFails_ClearsTokenAndRaisesEvent()
    {
        _tokenStorageMock.Setup(t => t.GetTokenAsync()).ReturnsAsync("expired-token");
        _tokenStorageMock.Setup(t => t.GetRefreshTokenAsync()).ReturnsAsync((string?)null);
        bool eventRaised = false;
        _eventBus.AuthExpired += () => eventRaised = true;

        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await client.GetAsync("/api/resource");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _refreshMock.Verify(r => r.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        eventRaised.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WhenUnauthorizedButRefreshTokenStillPresent_DoesNotRaiseAuthExpired()
    {
        // 刷新因网络故障失败时 token 仍在：不跳登录，等下次请求再试。
        _tokenStorageMock.Setup(t => t.GetTokenAsync()).ReturnsAsync("expired-token");
        _tokenStorageMock.Setup(t => t.GetRefreshTokenAsync()).ReturnsAsync("still-there");
        bool eventRaised = false;
        _eventBus.AuthExpired += () => eventRaised = true;

        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await client.GetAsync("/api/resource");

        eventRaised.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WhenUnauthorizedAndRefreshSucceeds_RetriesOnceWithNewToken()
    {
        _tokenStorageMock.SetupSequence(t => t.GetTokenAsync())
            .ReturnsAsync("expired-token")
            .ReturnsAsync("fresh-token");
        _refreshMock.Setup(r => r.RefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var seenTokens = new List<string?>();

        var callCount = 0;
        var client = CreateHttpClient(req =>
        {
            callCount++;
            seenTokens.Add(req.Headers.Authorization?.Parameter);
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        var response = await client.GetAsync("/api/resource");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2);
        seenTokens.Should().Equal(["expired-token", "fresh-token"]);
    }

    [Fact]
    public async Task SendAsync_WhenLoginEndpoint401_DoesNotClearTokenOrRaiseEvent()
    {
        // S2-03：登录端点输错密码的 401 不清已有有效 token。
        _tokenStorageMock.Setup(t => t.GetTokenAsync()).ReturnsAsync("valid-token");
        bool eventRaised = false;
        _eventBus.AuthExpired += () => eventRaised = true;

        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest("wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _tokenStorageMock.Verify(t => t.ClearTokenAsync(), Times.Never);
        _refreshMock.Verify(r => r.RefreshAsync(It.IsAny<CancellationToken>()), Times.Never);
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WhenOkResponse_DoesNotClearToken()
    {
        _tokenStorageMock.Setup(t => t.GetTokenAsync()).ReturnsAsync("valid-token");

        var client = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await client.GetAsync("/api/resource");

        _tokenStorageMock.Verify(t => t.ClearTokenAsync(), Times.Never);
    }

    [Fact]
    public void IsAuthEndpoint_MatchesLoginSetupRefreshOnly()
    {
        AuthDelegatingHandler.IsAuthEndpoint(new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/v1/auth/login"))
            .Should().BeTrue();
        AuthDelegatingHandler.IsAuthEndpoint(new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/v1/auth/setup"))
            .Should().BeTrue();
        AuthDelegatingHandler.IsAuthEndpoint(new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/v1/auth/refresh"))
            .Should().BeTrue();
        AuthDelegatingHandler.IsAuthEndpoint(new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/v1/topics"))
            .Should().BeFalse();
        AuthDelegatingHandler.IsAuthEndpoint(new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/v1/auth/me"))
            .Should().BeFalse();
    }
}
