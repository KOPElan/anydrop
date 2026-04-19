using System.Security.Claims;
using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace AnyDrop.Api;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/settings").WithTags("Settings").RequireAuthorization();

        group.MapPut("/profile", UpdateProfileAsync);
        group.MapPut("/password", UpdatePasswordAsync);
        group.MapGet("/security", GetSecurityAsync);
        group.MapPut("/security", UpdateSecurityAsync);

        return app;
    }

    public static async Task<IResult> UpdateProfileAsync(
        UpdateNicknameRequest request,
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var result = await authService.UpdateNicknameAsync(userId.Value, request, ct);
        if (result.Succeeded)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, result.Data!.Nickname),
                new("nickname", result.Data.Nickname)
            };

            var sub = httpContext.User.FindFirstValue("sub") ?? userId.Value.ToString();
            var sessionVersion = httpContext.User.FindFirstValue("sessionVersion") ?? "1";
            claims.Add(new Claim("sub", sub));
            claims.Add(new Claim(ClaimTypes.NameIdentifier, sub));
            claims.Add(new Claim("sessionVersion", sessionVersion));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
            var requestServices = httpContext.RequestServices;
            var authenticationService = requestServices is null ? null : requestServices.GetService<IAuthenticationService>();
            if (authenticationService is not null)
            {
                await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            }
        }

        return result.Succeeded
            ? Results.Ok(ApiEnvelope<UserProfileDto>.Ok(result.Data!))
            : Results.Json(ApiEnvelope<UserProfileDto>.Fail(result.Error ?? "更新昵称失败。"), statusCode: result.StatusCode);
    }

    public static async Task<IResult> UpdatePasswordAsync(
        UpdatePasswordRequest request,
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var result = await authService.UpdatePasswordAsync(userId.Value, request, ct);
        if (!result.Succeeded)
        {
            return Results.Json(ApiEnvelope<bool>.Fail(result.Error ?? "更新密码失败。"), statusCode: result.StatusCode);
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(ApiEnvelope<object>.Ok(new { updated = true }));
    }

    public static async Task<IResult> GetSecurityAsync(ISystemSettingsService systemSettingsService, CancellationToken ct)
    {
        var settings = await systemSettingsService.GetSecuritySettingsAsync(ct);
        return Results.Ok(ApiEnvelope<SecuritySettingsDto>.Ok(settings));
    }

    public static async Task<IResult> UpdateSecurityAsync(
        UpdateSecuritySettingsRequest request,
        ISystemSettingsService systemSettingsService,
        CancellationToken ct)
    {
        var result = await systemSettingsService.UpdateSecuritySettingsAsync(request.AutoFetchLinkPreview, ct);
        return result.Succeeded
            ? Results.Ok(ApiEnvelope<SecuritySettingsDto>.Ok(result.Data!))
            : Results.Json(ApiEnvelope<SecuritySettingsDto>.Fail(result.Error ?? "更新失败。"), statusCode: result.StatusCode);
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var userId) ? userId : null;
    }
}
