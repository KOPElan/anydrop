using System.Security.Claims;
using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AnyDrop.Api;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapGet("/setup-status", GetSetupStatusAsync).AllowAnonymous();
        group.MapPost("/setup", SetupAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous();

        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", MeAsync).RequireAuthorization();

        return app;
    }

    public static async Task<Ok<ApiEnvelope<SetupStatusDto>>> GetSetupStatusAsync(
        IUserService userService,
        CancellationToken ct)
    {
        var requiresSetup = !await userService.HasUserAsync(ct);
        return TypedResults.Ok(ApiEnvelope<SetupStatusDto>.Ok(new SetupStatusDto(requiresSetup)));
    }

    public static async Task<IResult> SetupAsync(
        SetupRequest request,
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken ct)
    {
        var result = await authService.SetupAsync(request, BuildRateLimitKey(httpContext), ct);
        return await BuildAuthResultAsync(httpContext, result);
    }

    public static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, BuildRateLimitKey(httpContext), ct);
        return await BuildAuthResultAsync(httpContext, result);
    }

    public static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var result = await authService.LogoutAsync(userId.Value, ct);
        if (!result.Succeeded)
        {
            return Results.Json(ApiEnvelope<LogoutResultDto>.Fail(result.Error ?? "登出失败。"), statusCode: result.StatusCode);
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(ApiEnvelope<LogoutResultDto>.Ok(result.Data!));
    }

    public static async Task<IResult> MeAsync(
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var result = await authService.GetProfileAsync(userId.Value, ct);
        return result.Succeeded
            ? Results.Ok(ApiEnvelope<UserProfileDto>.Ok(result.Data!))
            : Results.Json(ApiEnvelope<UserProfileDto>.Fail(result.Error ?? "获取用户信息失败。"), statusCode: result.StatusCode);
    }

    private static async Task<IResult> BuildAuthResultAsync(HttpContext httpContext, AuthResult<LoginResponse> result)
    {
        if (!result.Succeeded)
        {
            return Results.Json(ApiEnvelope<LoginResponse>.Fail(result.Error ?? "认证失败。"), statusCode: result.StatusCode);
        }

        await SignInCookieAsync(httpContext, result.Data!);
        return Results.Json(ApiEnvelope<LoginResponse>.Ok(result.Data!), statusCode: result.StatusCode);
    }

    private static async Task SignInCookieAsync(HttpContext httpContext, LoginResponse loginResponse)
    {
        var user = loginResponse.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Nickname),
            new("nickname", user.Nickname)
        };

        if (TryReadClaim(loginResponse.AccessToken, JwtClaimTypes.Subject, out var sub))
        {
            claims.Add(new Claim(JwtClaimTypes.Subject, sub!));
            claims.Add(new Claim(ClaimTypes.NameIdentifier, sub!));
        }

        if (TryReadClaim(loginResponse.AccessToken, "sessionVersion", out var sessionVersion))
        {
            claims.Add(new Claim("sessionVersion", sessionVersion!));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = loginResponse.ExpiresAt
            });
    }

    private static string BuildRateLimitKey(HttpContext httpContext)
        => $"single-user:{httpContext.Connection.RemoteIpAddress}";

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var claim = principal.FindFirstValue(JwtClaimTypes.Subject)
                    ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    private static bool TryReadClaim(string token, string claimType, out string? value)
    {
        value = null;
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            value = jwt.Claims.FirstOrDefault(x => x.Type == claimType)?.Value;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    private static class JwtClaimTypes
    {
        public const string Subject = "sub";
    }
}
