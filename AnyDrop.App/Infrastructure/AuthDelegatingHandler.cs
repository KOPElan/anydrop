using AnyDrop.App.Services;

namespace AnyDrop.App.Infrastructure;

/// <summary>
/// Transient DelegatingHandler，自动将 Bearer Token 注入请求头，
/// 并在 401 响应时触发 AuthExpired 事件。
/// </summary>
public sealed class AuthDelegatingHandler : DelegatingHandler
{
    private readonly ISecureTokenStorage _tokenStorage;
    private readonly AppEventBus _eventBus;

    public AuthDelegatingHandler(ISecureTokenStorage tokenStorage, AppEventBus eventBus)
    {
        _tokenStorage = tokenStorage;
        _eventBus = eventBus;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenStorage.GetTokenAsync().ConfigureAwait(false);
        if (token is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await _tokenStorage.ClearTokenAsync().ConfigureAwait(false);
            _eventBus.RaiseAuthExpired();
        }

        return response;
    }
}
