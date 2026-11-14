using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AnyDrop.App.Infrastructure;
using AnyDrop.App.Services;
using AnyDrop.Shared;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;

namespace AnyDrop.Tests.Unit.App;

/// <summary>
/// S2-01 / S2-02：单飞刷新、Hub 短 token 缓存、刷新失败分流。
/// </summary>
public class TokenRefreshServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Responder { get; set; } =
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        public int CallCount;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            lock (Requests)
            {
                Requests.Add(request);
            }

            return await Responder(request).ConfigureAwait(false);
        }
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly Dictionary<string, FakeHandler> _handlers;
        public FakeFactory(Dictionary<string, FakeHandler> handlers) => _handlers = handlers;

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(_handlers[name]) { BaseAddress = new Uri("http://localhost/") };
            return client;
        }
    }

    private static (TokenRefreshService Sut, SecureTokenStorage Storage, FakeHandler RefreshHandler, FakeHandler ApiHandler, AppEventBus Bus) CreateSut()
    {
        var secretStore = new InMemorySecretStore();
        var storage = new SecureTokenStorage(secretStore);
        var refreshHandler = new FakeHandler();
        var apiHandler = new FakeHandler();
        var factory = new FakeFactory(new Dictionary<string, FakeHandler>
        {
            ["auth-refresh"] = refreshHandler,
            ["api"] = apiHandler
        });
        var bus = new AppEventBus();
        var sut = new TokenRefreshService(factory, storage, bus);
        return (sut, storage, refreshHandler, apiHandler, bus);
    }

    private static HttpResponseMessage RefreshSuccessResponse()
    {
        var payload = new LoginResponse(
            new UserProfileDto("Admin"),
            "new-access",
            DateTimeOffset.UtcNow.AddHours(24),
            "new-refresh",
            DateTimeOffset.UtcNow.AddDays(30));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ApiEnvelope<LoginResponse>.Ok(payload))
        };
    }

    [Fact]
    public async Task RefreshAsync_Success_SavesNewTokens()
    {
        var (sut, storage, refreshHandler, _, _) = CreateSut();
        await storage.SaveTokenAsync("old-access", DateTimeOffset.UtcNow.AddMinutes(-1));
        await storage.SaveRefreshTokenAsync("old-refresh", DateTimeOffset.UtcNow.AddDays(1));
        refreshHandler.Responder = _ => Task.FromResult(RefreshSuccessResponse());

        var ok = await sut.RefreshAsync();

        ok.Should().BeTrue();
        refreshHandler.CallCount.Should().Be(1);
        (await storage.GetTokenAsync()).Should().Be("new-access");
        (await storage.GetRefreshTokenAsync()).Should().Be("new-refresh");
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentCalls_IssueOnlyOneRequest()
    {
        var (sut, storage, refreshHandler, _, _) = CreateSut();
        await storage.SaveRefreshTokenAsync("old-refresh", DateTimeOffset.UtcNow.AddDays(1));
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        refreshHandler.Responder = async _ => await tcs.Task.ConfigureAwait(false);

        var tasks = Enumerable.Range(0, 10).Select(_ => sut.RefreshAsync()).ToArray();
        await Task.Delay(100);
        refreshHandler.CallCount.Should().Be(1, "10 个并发刷新只应发一次请求（单飞）");
        tcs.SetResult(RefreshSuccessResponse());

        var results = await Task.WhenAll(tasks);
        results.Should().AllBeEquivalentTo(true);
        refreshHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_ServerRejects_ClearsTokensAndRaisesAuthExpiredOnce()
    {
        var (sut, storage, refreshHandler, _, bus) = CreateSut();
        await storage.SaveTokenAsync("old-access", DateTimeOffset.UtcNow.AddHours(1));
        await storage.SaveRefreshTokenAsync("old-refresh", DateTimeOffset.UtcNow.AddDays(1));
        refreshHandler.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var eventCount = 0;
        bus.AuthExpired += () => eventCount++;

        var ok = await sut.RefreshAsync();

        ok.Should().BeFalse();
        (await storage.GetTokenAsync()).Should().BeNull();
        (await storage.GetRefreshTokenAsync()).Should().BeNull();
        eventCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_NetworkFailure_KeepsTokensAndDoesNotRaiseAuthExpired()
    {
        var (sut, storage, refreshHandler, _, bus) = CreateSut();
        await storage.SaveTokenAsync("old-access", DateTimeOffset.UtcNow.AddHours(1));
        await storage.SaveRefreshTokenAsync("old-refresh", DateTimeOffset.UtcNow.AddDays(1));
        refreshHandler.Responder = _ => throw new HttpRequestException("network down");
        var eventCount = 0;
        bus.AuthExpired += () => eventCount++;

        var ok = await sut.RefreshAsync();

        ok.Should().BeFalse();
        (await storage.GetTokenAsync()).Should().Be("old-access");
        (await storage.GetRefreshTokenAsync()).Should().Be("old-refresh");
        eventCount.Should().Be(0);
    }

    [Fact]
    public async Task EnsureValidAccessTokenAsync_WhenTokenFresh_DoesNotCallRefresh()
    {
        var (sut, storage, refreshHandler, _, _) = CreateSut();
        await storage.SaveTokenAsync("access", DateTimeOffset.UtcNow.AddHours(2));
        await storage.SaveRefreshTokenAsync("refresh", DateTimeOffset.UtcNow.AddDays(1));

        var ok = await sut.EnsureValidAccessTokenAsync();

        ok.Should().BeTrue();
        refreshHandler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EnsureValidAccessTokenAsync_WhenTokenExpiringSoon_Refreshes()
    {
        var (sut, storage, refreshHandler, _, _) = CreateSut();
        await storage.SaveTokenAsync("access", DateTimeOffset.UtcNow.AddMinutes(2));
        await storage.SaveRefreshTokenAsync("refresh", DateTimeOffset.UtcNow.AddDays(1));
        refreshHandler.Responder = _ => Task.FromResult(RefreshSuccessResponse());

        var ok = await sut.EnsureValidAccessTokenAsync();

        ok.Should().BeTrue();
        refreshHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHubTokenAsync_CachesUntilNearExpiry()
    {
        var (sut, storage, _, apiHandler, _) = CreateSut();
        await storage.SaveTokenAsync("access", DateTimeOffset.UtcNow.AddHours(2));
        apiHandler.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ApiEnvelope<HubTokenResponse>.Ok(
                new HubTokenResponse("hub-token-1", DateTimeOffset.UtcNow.AddMinutes(5))))
        });

        var first = await sut.GetHubTokenAsync();
        var second = await sut.GetHubTokenAsync();

        first.Should().Be("hub-token-1");
        second.Should().Be("hub-token-1");
        apiHandler.CallCount.Should().Be(1, "缓存期内不应重复请求 Hub token");
    }

    [Fact]
    public async Task GetHubTokenAsync_WhenNoSession_ReturnsNull()
    {
        var (sut, _, _, apiHandler, _) = CreateSut();

        var token = await sut.GetHubTokenAsync();

        token.Should().BeNull();
        apiHandler.CallCount.Should().Be(0);
    }
}
