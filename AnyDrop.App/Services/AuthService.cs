using System.Net.Http.Json;
using AnyDrop.App.Models;

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
        var response = await client.GetFromJsonAsync<ApiResponse<SetupStatusDto>>("api/v1/auth/setup-status")
            .ConfigureAwait(false);
        return response?.Data ?? new SetupStatusDto(false);
    }

    public async Task<LoginResponse> SetupAsync(SetupRequest request)
    {
        var client = _httpClientFactory.CreateClient("api");
        var httpResponse = await client.PostAsJsonAsync("api/v1/auth/setup", request).ConfigureAwait(false);
        var response = await httpResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>().ConfigureAwait(false);

        if (response?.Data is { Success: true, Token: { } token, ExpiresAt: { } expiresAt })
            await _tokenStorage.SaveTokenAsync(token, expiresAt).ConfigureAwait(false);

        return response?.Data ?? new LoginResponse(false, null, null, "Unknown error");
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var client = _httpClientFactory.CreateClient("api");
        var httpResponse = await client.PostAsJsonAsync("api/v1/auth/login", request).ConfigureAwait(false);
        var response = await httpResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>().ConfigureAwait(false);

        if (response?.Data is { Success: true, Token: { } token, ExpiresAt: { } expiresAt })
            await _tokenStorage.SaveTokenAsync(token, expiresAt).ConfigureAwait(false);

        return response?.Data ?? new LoginResponse(false, null, null, "Unknown error");
    }

    public async Task LogoutAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("api");
            await client.PostAsync("api/v1/auth/logout", null).ConfigureAwait(false);
        }
        catch { /* ignore logout API failures */ }
        finally
        {
            await _tokenStorage.ClearTokenAsync().ConfigureAwait(false);
        }
    }

    public async Task<UserProfileDto> GetCurrentUserAsync()
    {
        var client = _httpClientFactory.CreateClient("api");
        var response = await client.GetFromJsonAsync<ApiResponse<UserProfileDto>>("api/v1/auth/me")
            .ConfigureAwait(false);
        return response?.Data ?? new UserProfileDto("Unknown");
    }
}
