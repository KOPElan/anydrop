using System.Net.Http.Json;
using AnyDrop.App.Models;
using AnyDrop.Shared;

namespace AnyDrop.App.Services;

/// <summary>调用服务端认证 API，成功后保存 Token。</summary>
public sealed class AuthService : IAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecureTokenStorage _tokenStorage;

    public AuthService(IHttpClientFactory httpClientFactory, ISecureTokenStorage tokenStorage)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStorage = tokenStorage;
    }

    public async Task<SetupStatusDto> GetSetupStatusAsync()
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.GetFromJsonAsync<ApiEnvelope<SetupStatusDto>>("api/v1/auth/setup-status")
            .ConfigureAwait(false);
        return response?.Data ?? new SetupStatusDto(false);
    }

    public async Task<AppAuthResult> SetupAsync(SetupRequest request)
    {
        return await PostAuthAsync("api/v1/auth/setup", request).ConfigureAwait(false);
    }

    public async Task<AppAuthResult> LoginAsync(LoginRequest request)
    {
        return await PostAuthAsync("api/v1/auth/login", request).ConfigureAwait(false);
    }

    public async Task LogoutAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("api");
            await client.PostAsync("api/v1/auth/logout", null).ConfigureAwait(false);
        }
        catch { /* 忽略注销 API 失败 */ }
        finally
        {
            await _tokenStorage.ClearTokenAsync().ConfigureAwait(false);
        }
    }

    public async Task<UserProfileDto> GetCurrentUserAsync()
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.GetFromJsonAsync<ApiEnvelope<UserProfileDto>>("api/v1/auth/me")
            .ConfigureAwait(false);
        return response?.Data ?? new UserProfileDto("Unknown");
    }

    private async Task<AppAuthResult> PostAuthAsync<TRequest>(string url, TRequest request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("api");
            var httpResponse = await client.PostAsJsonAsync(url, request).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorEnvelope = await httpResponse.Content
                    .ReadFromJsonAsync<ApiEnvelope<LoginResponse>>()
                    .ConfigureAwait(false);
                return new AppAuthResult(false, errorEnvelope?.Error ?? "请求失败", ErrorCode: errorEnvelope?.ErrorCode);
            }

            var envelope = await httpResponse.Content
                .ReadFromJsonAsync<ApiEnvelope<LoginResponse>>()
                .ConfigureAwait(false);

            if (envelope?.Data is { } data)
            {
                await _tokenStorage.SaveTokenAsync(data.AccessToken, data.ExpiresAt).ConfigureAwait(false);
                // S2-01：服务端双发 Refresh Token；旧服务端不返回时保持只有 Access Token。
                if (data.RefreshToken is not null && data.RefreshTokenExpiresAt.HasValue)
                {
                    await _tokenStorage.SaveRefreshTokenAsync(data.RefreshToken, data.RefreshTokenExpiresAt.Value)
                        .ConfigureAwait(false);
                }

                return new AppAuthResult(true, null, data.AccessToken, data.ExpiresAt, data.User);
            }

            return new AppAuthResult(false, "响应数据为空");
        }
        catch (Exception ex)
        {
            return new AppAuthResult(false, ex.Message);
        }
    }
}

