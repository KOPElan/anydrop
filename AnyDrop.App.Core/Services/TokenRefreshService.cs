using System.Net.Http.Json;
using AnyDrop.App.Infrastructure;
using AnyDrop.Shared;

namespace AnyDrop.App.Services;

/// <summary>单飞刷新与 Hub 短 token 提供器（S2-01 / S2-02）。</summary>
public interface ITokenRefreshService
{
    /// <summary>
    /// Access Token 在 5 分钟内过期（或已过期）时刷新一次；否则不做任何事。
    /// 返回刷新后是否持有有效 Access Token。
    /// </summary>
    Task<bool> EnsureValidAccessTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// 立即刷新一次。并发调用只发一次网络请求（单飞），其余调用方等同一个结果。
    /// 刷新失败时清掉本地 token 并触发一次过期事件（<see cref="AppEventBus"/> 内去重）。
    /// </summary>
    Task<bool> RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// 取 Hub 专用短 token（S2-02，默认 5 分钟有效）。缓存到过期前 1 分钟；
    /// 拿不到有效 token 时返回 null，调用方直接走过期流程，不传空值。
    /// </summary>
    Task<string?> GetHubTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Refresh Token 客户端（S2-01 / S2-02）。
///
/// 刷新请求走独立的 "auth-refresh" HttpClient（不挂 <see cref="AuthDelegatingHandler"/>），
/// 避免 401 处理器在刷新请求上递归触发刷新。
/// </summary>
public sealed class TokenRefreshService : ITokenRefreshService
{
    /// <summary>Access Token 剩余有效期低于此时提前刷新。</summary>
    public static readonly TimeSpan RefreshAhead = TimeSpan.FromMinutes(5);

    /// <summary>Hub 短 token 剩余有效期低于此时重新取。</summary>
    public static readonly TimeSpan HubTokenRefreshAhead = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureTokenStorage _tokenStorage;
    private readonly AppEventBus _eventBus;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task<bool>? _inFlightRefresh;

    private readonly SemaphoreSlim _hubTokenLock = new(1, 1);
    private string? _cachedHubToken;
    private DateTimeOffset _cachedHubTokenExpiresAt = DateTimeOffset.MinValue;

    public TokenRefreshService(
        IHttpClientFactory httpClientFactory,
        ISecureTokenStorage tokenStorage,
        AppEventBus eventBus)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStorage = tokenStorage;
        _eventBus = eventBus;
    }

    public async Task<bool> EnsureValidAccessTokenAsync(CancellationToken ct = default)
    {
        var expiresAt = await _tokenStorage.GetTokenExpiresAtAsync().ConfigureAwait(false);
        if (expiresAt.HasValue && expiresAt.Value - DateTimeOffset.UtcNow > RefreshAhead)
        {
            return true;
        }

        return await RefreshAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        Task<bool> inFlight;
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_inFlightRefresh is { IsCompleted: false } running)
            {
                inFlight = running;
            }
            else
            {
                inFlight = DoRefreshAsync(ct);
                _inFlightRefresh = inFlight;
            }
        }
        finally
        {
            _refreshLock.Release();
        }

        try
        {
            return await inFlight.ConfigureAwait(false);
        }
        finally
        {
            await _refreshLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_inFlightRefresh, inFlight))
                {
                    _inFlightRefresh = null;
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }

    private async Task<bool> DoRefreshAsync(CancellationToken ct)
    {
        var refreshToken = await _tokenStorage.GetRefreshTokenAsync().ConfigureAwait(false);
        if (refreshToken is null)
        {
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("auth-refresh");
            using var response = await client.PostAsJsonAsync(
                "api/v1/auth/refresh",
                new RefreshTokenRequest(refreshToken),
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await FailSessionAsync().ConfigureAwait(false);
                return false;
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiEnvelope<LoginResponse>>(cancellationToken: ct)
                .ConfigureAwait(false);
            var data = envelope?.Data;
            if (envelope?.Success != true || data is null)
            {
                await FailSessionAsync().ConfigureAwait(false);
                return false;
            }

            await _tokenStorage.SaveTokenAsync(data.AccessToken, data.ExpiresAt).ConfigureAwait(false);
            if (data.RefreshToken is not null && data.RefreshTokenExpiresAt.HasValue)
            {
                await _tokenStorage.SaveRefreshTokenAsync(data.RefreshToken, data.RefreshTokenExpiresAt.Value)
                    .ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // 网络故障不是会话失效：不断 token、不跳登录，等下次请求再试。
            System.Diagnostics.Debug.WriteLine($"[TokenRefreshService] 刷新请求网络失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>刷新明确失败（服务端拒绝）：清 token 并触发过期事件。</summary>
    private async Task FailSessionAsync()
    {
        await _tokenStorage.ClearTokenAsync().ConfigureAwait(false);
        await _tokenStorage.ClearRefreshTokenAsync().ConfigureAwait(false);
        _cachedHubToken = null;
        _cachedHubTokenExpiresAt = DateTimeOffset.MinValue;
        _eventBus.RaiseAuthExpired();
    }

    public async Task<string?> GetHubTokenAsync(CancellationToken ct = default)
    {
        await _hubTokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedHubToken is not null
                && _cachedHubTokenExpiresAt - DateTimeOffset.UtcNow > HubTokenRefreshAhead)
            {
                return _cachedHubToken;
            }

            if (!await EnsureValidAccessTokenAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            try
            {
                var client = _httpClientFactory.CreateClient("api");
                var envelope = await client.GetFromJsonAsync<ApiEnvelope<HubTokenResponse>>(
                    "api/v1/auth/hub-token", ct).ConfigureAwait(false);
                var data = envelope?.Data;
                if (envelope?.Success != true || data is null)
                {
                    return null;
                }

                _cachedHubToken = data.AccessToken;
                _cachedHubTokenExpiresAt = data.ExpiresAt;
                return _cachedHubToken;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[TokenRefreshService] 获取 Hub token 失败: {ex.Message}");
                return null;
            }
        }
        finally
        {
            _hubTokenLock.Release();
        }
    }
}
