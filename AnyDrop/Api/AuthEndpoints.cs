using System.Globalization;
using System.Security.Claims;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Shared;
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
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapGet("/hub-token", GetHubTokenAsync).RequireAuthorization();

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
        var result = await authService.SetupAsync(request, BuildRateLimitKey(httpContext, "setup"), ct);
        return await BuildAuthResultAsync(httpContext, result);
    }

    public static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, BuildRateLimitKey(httpContext, "login"), ct);
        return await BuildAuthResultAsync(httpContext, result);
    }

    /// <summary>
    /// S2-01：用 Refresh Token 换新 Access Token（旋转：旧 refresh token 作废，
    /// 返回新的 refresh token）。重放已撤销的 token 会吊销整族——响应与普通
    /// 无效 token 无异，不向调用方透露是否触发了重放检测。
    /// </summary>
    public static async Task<IResult> RefreshAsync(
        RefreshTokenRequest request,
        IRefreshTokenService refreshTokenService,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.Json(
                ApiEnvelope<LoginResponse>.Fail("刷新令牌无效。"),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await refreshTokenService.RotateAsync(request.RefreshToken, ct);
        if (!result.Succeeded || result.Payload is null)
        {
            return Results.Json(
                ApiEnvelope<LoginResponse>.Fail(result.Error ?? "刷新令牌无效。"),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(ApiEnvelope<LoginResponse>.Ok(result.Payload));
    }

    /// <summary>
    /// S2-02：签发 Hub 专用短 token（默认 5 分钟）。MAUI 的 Hub 连接用它协商，
    /// 不再把 24 小时长效 Access Token 交给 SignalR 传输层。
    /// Web 端仍在服务端直接签发（ResolveHubAccessTokenAsync），不走本接口。
    /// </summary>
    public static async Task<IResult> GetHubTokenAsync(
        HttpContext httpContext,
        ITokenService tokenService,
        IUserService userService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Results.Json(
                ApiEnvelope<HubTokenResponse>.Fail("未授权。"),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = await userService.GetByIdAsync(userId.Value, ct);
        if (user is null)
        {
            return Results.Json(
                ApiEnvelope<HubTokenResponse>.Fail("用户不存在。"),
                statusCode: StatusCodes.Status404NotFound);
        }

        var (token, expiresAt) = tokenService.GenerateHubToken(user);
        return Results.Ok(ApiEnvelope<HubTokenResponse>.Ok(new HubTokenResponse(token, expiresAt)));
    }

    public static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        IAuthService authService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Results.Json(
                ApiEnvelope<LogoutResultDto>.Fail("未授权。"),
                statusCode: StatusCodes.Status401Unauthorized);
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
            return Results.Json(
                ApiEnvelope<UserProfileDto>.Fail("未授权。"),
                statusCode: StatusCodes.Status401Unauthorized);
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
            // 限流失败时带上 Retry-After，让客户端知道何时可以重试
            if (result.RetryAfter is { } retryAfter)
            {
                httpContext.Response.Headers["Retry-After"] =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
            }

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

        if (!TryReadClaim(loginResponse.AccessToken, JwtClaimTypes.Subject, out var sub))
        {
            // 没有 sub 的 Cookie 过不了 OnValidatePrincipal，表现为「登录成功但下一次请求立刻被登出」，
            // 极难诊断。与其留下这种状态，不如直接失败。
            throw new InvalidOperationException(
                "Issued access token is missing the 'sub' claim; cannot build a valid cookie session.");
        }

        claims.Add(new Claim(JwtClaimTypes.Subject, sub!));
        claims.Add(new Claim(ClaimTypes.NameIdentifier, sub!));

        if (!TryReadClaim(loginResponse.AccessToken, "sessionVersion", out var sessionVersion))
        {
            throw new InvalidOperationException(
                "Issued access token is missing the 'sessionVersion' claim; cannot build a valid cookie session.");
        }

        claims.Add(new Claim("sessionVersion", sessionVersion!));

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

    /// <summary>
    /// 限流 key：按接口 + 真实来源 IP 区分。反代部署下 <see cref="HttpContext.Connection"/>
    /// 的 RemoteIpAddress 由 ForwardedHeaders 中间件根据 X-Forwarded-For 还原
    /// （只信任已配置的代理，见 Program.cs），因此同一出口 IP 的攻击只锁该 IP，
    /// 不会像原来那样按连接 IP 把所有用户一起锁住。
    /// </summary>
    private static string BuildRateLimitKey(HttpContext httpContext, string kind)
        => $"single-user:{kind}:{httpContext.Connection.RemoteIpAddress}";

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
