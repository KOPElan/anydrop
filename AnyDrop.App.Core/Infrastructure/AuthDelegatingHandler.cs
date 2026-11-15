using System.Net;
using AnyDrop.App.Services;

namespace AnyDrop.App.Infrastructure;

/// <summary>
/// Transient DelegatingHandler，自动将 Bearer Token 注入请求头，
/// 并在 401 响应时走刷新重试（S2-01 / S2-03）。
///
/// 分流规则：
/// <list type="bullet">
/// <item>登录、建号、刷新端点不挂 401 处理：输错密码的 401 不再清掉已有有效 token；</item>
/// <item>其余端点的 401 先走单飞刷新，刷新成功则原请求重试一次；</item>
/// <item>刷新失败才清 token、触发过期事件（<see cref="AppEventBus"/> 内 10 秒去重）。</item>
/// </list>
/// </summary>
public sealed class AuthDelegatingHandler : DelegatingHandler
{
    private static readonly string[] AuthEndpoints =
    [
        "/api/v1/auth/login",
        "/api/v1/auth/setup",
        "/api/v1/auth/refresh"
    ];

    private readonly ISecureTokenStorage _tokenStorage;
    private readonly AppEventBus _eventBus;
    private readonly ITokenRefreshService _refreshService;

    public AuthDelegatingHandler(
        ISecureTokenStorage tokenStorage,
        AppEventBus eventBus,
        ITokenRefreshService refreshService)
    {
        _tokenStorage = tokenStorage;
        _eventBus = eventBus;
        _refreshService = refreshService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsAuthEndpoint(request))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var token = await _tokenStorage.GetTokenAsync().ConfigureAwait(false);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // 401：先刷新一次，成功则原请求重试一次。
        // 请求内容可能已被发送端消费，重试前用预先克隆的副本重发。
        var retryRequest = await CloneRequestAsync(request).ConfigureAwait(false);
        if (retryRequest is null)
        {
            await FailSessionAsync().ConfigureAwait(false);
            return response;
        }

        var refreshed = await _refreshService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (!refreshed)
        {
            // 刷新失败分两种：服务端明确拒绝时 RefreshAsync 已清掉全部 token，
            // 此时触发过期事件；网络故障时 token 仍在，不跳登录，等下次请求再试。
            if (await _tokenStorage.GetRefreshTokenAsync().ConfigureAwait(false) is null)
            {
                _eventBus.RaiseAuthExpired();
            }

            return response;
        }

        response.Dispose();
        var newToken = await _tokenStorage.GetTokenAsync().ConfigureAwait(false);
        if (newToken is not null)
        {
            retryRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newToken);
        }

        return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private async Task FailSessionAsync()
    {
        await _tokenStorage.ClearTokenAsync().ConfigureAwait(false);
        await _tokenStorage.ClearRefreshTokenAsync().ConfigureAwait(false);
        _eventBus.RaiseAuthExpired();
    }

    /// <summary>登录 / 建号 / 刷新端点不挂 401 处理（S2-03）。</summary>
    public static bool IsAuthEndpoint(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        foreach (var endpoint in AuthEndpoints)
        {
            if (path.EndsWith(endpoint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 深拷贝请求（含请求体缓冲），供 401 刷新后重试使用。
    /// 无请求体的请求直接新建等价请求。
    /// </summary>
    private static async Task<HttpRequestMessage?> CloneRequestAsync(HttpRequestMessage request)
    {
        try
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version
            };
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                var content = new ByteArrayContent(bytes);
                foreach (var header in request.Content.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                clone.Content = content;
            }

            foreach (var option in request.Options)
            {
                clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            }

            return clone;
        }
        catch
        {
            return null;
        }
    }
}
