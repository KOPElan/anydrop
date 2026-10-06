using AnyDrop.Api;
using AnyDrop.Components;
using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 文件上传大小限制：优先从配置读取，回退到 1 GB
const long defaultMaxUploadBytes = 1L * 1024 * 1024 * 1024;
var maxUploadBytes = builder.Configuration.GetValue<long?>("Storage:MaxFileSizeBytes") ?? defaultMaxUploadBytes;

// 放宽 Kestrel 的最大请求体限制（默认 30 MB），确保能接收大文件上传
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});

// 放宽 ASP.NET Core 表单（multipart）解析器的大小限制（默认 128 MB）
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});
const string appAuthenticationScheme = "AnyDrop";

// Add services to the container.
const string defaultDatabasePath = "data/anydrop.db";
var dbPath = builder.Configuration["Storage:DatabasePath"] ?? defaultDatabasePath;
var fullDbPath = Path.GetFullPath(dbPath);
var dbDirectory = Path.GetDirectoryName(fullDbPath);
if (!string.IsNullOrWhiteSpace(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddDbContext<AnyDropDbContext>(options =>
    options.UseSqlite($"Data Source={fullDbPath}"));
builder.Services.AddSignalR();
// 统一的时间源。业务代码一律通过 TimeProvider 取当前时间，
// 从而可以在测试中用假时钟确定性地验证限流冷却、令牌过期、阅后即焚等时间相关逻辑。
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<ITopicService, TopicService>();
builder.Services.AddScoped<ITopicStateService, TopicStateService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IThumbnailService, ThumbnailService>();
builder.Services.AddScoped<OrphanFileReconciler>();
builder.Services.AddHostedService<OrphanedFileCleanupService>();
builder.Services.AddSingleton<ThumbnailGenerationBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ThumbnailGenerationBackgroundService>());
builder.Services.AddSingleton<LinkMetadataService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
// 抓取外链元数据专用的客户端：禁用自动重定向，并在建连前校验目标 IP（SSRF 防护）
builder.Services.AddHttpClient(LinkMetadataService.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => LinkMetadataService.CreateHandler());
builder.Services.AddHostedService<ExpiredMessageCleanupService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 多语言本地化：SharedStrings 资源当前嵌入名为 AnyDrop.SharedStrings.resources，
// 因此不设置 ResourcesPath，避免运行时按 AnyDrop.Resources.* 查找导致回退键名。
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(AnyDrop.Models.SupportedLanguages.ZhCN);
    options.SupportedCultures = AnyDrop.Models.SupportedLanguages.All.Select(c => new CultureInfo(c)).ToList();
    options.SupportedUICultures = AnyDrop.Models.SupportedLanguages.All.Select(c => new CultureInfo(c)).ToList();
    options.RequestCultureProviders = [new CookieRequestCultureProvider()];
});

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
if (string.IsNullOrWhiteSpace(authOptions.JwtSecret) ||
    authOptions.JwtSecret.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Auth:JwtSecret is required and must be provided via configuration/environment.");
}

if (authOptions.JwtSecret.Length < 32)
{
    throw new InvalidOperationException("Auth:JwtSecret must be at least 32 characters long.");
}

var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSecret));
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = appAuthenticationScheme;
        options.DefaultAuthenticateScheme = appAuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = appAuthenticationScheme;
    })
    .AddPolicyScheme(appAuthenticationScheme, appAuthenticationScheme, options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return JwtBearerDefaults.AuthenticationScheme;
            }

            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrWhiteSpace(accessToken)
                && (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                    || context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)))
            {
                return JwtBearerDefaults.AuthenticationScheme;
            }

            return CookieAuthenticationDefaults.AuthenticationScheme;
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "anydrop.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";

        // 与 JWT 路径保持一致的会话版本校验。
        // 缺少这一步时：修改密码 / 登出虽然会递增 SessionVersion，但已签发的 Cookie
        // 仍会被接受，直到它自身过期（登录时按令牌有效期持久化，默认 24 小时）。
        // 也就是说被盗 Cookie 在改密码后依然可用，JWT 的撤销能力对 Web 端形同虚设。
        options.Events.OnValidatePrincipal = async context =>
        {
            var sub = context.Principal?.FindFirstValue("sub");
            var versionRaw = context.Principal?.FindFirstValue("sessionVersion");
            if (!Guid.TryParse(sub, out var userId) || !int.TryParse(versionRaw, out var sessionVersion))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
            var isValid = await authService.ValidateSessionVersionAsync(
                userId, sessionVersion, context.HttpContext.RequestAborted);
            if (!isValid)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };

        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return context.Response.WriteAsJsonAsync(ApiEnvelope<object>.Fail("未授权。"));
            }

            var returnUrl = Uri.EscapeDataString($"{context.Request.Path}{context.Request.QueryString}");
            context.Response.Redirect($"/login?returnUrl={returnUrl}");
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsJsonAsync(ApiEnvelope<object>.Fail("无权限访问。"));
            }

            var returnUrl = Uri.EscapeDataString($"{context.Request.Path}{context.Request.QueryString}");
            context.Response.Redirect($"/login?returnUrl={returnUrl}");
            return Task.CompletedTask;
        };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = authOptions.JwtIssuer,
            ValidAudience = authOptions.JwtAudience,
            IssuerSigningKey = jwtKey,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            // 禁止 JWT Claim 映射（默认会将 "sub" → ClaimTypes.NameIdentifier），
            // 否则 OnTokenValidated 中 FindFirstValue("sub") 返回 null 导致认证失败。
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var sub = context.Principal?.FindFirstValue("sub");
                var versionRaw = context.Principal?.FindFirstValue("sessionVersion");
                if (!Guid.TryParse(sub, out var userId) || !int.TryParse(versionRaw, out var tokenVersion))
                {
                    context.Fail("Invalid token claims.");
                    return;
                }

                var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                var isValid = await authService.ValidateSessionVersionAsync(userId, tokenVersion, context.HttpContext.RequestAborted);
                if (!isValid)
                {
                    context.Fail("Session is no longer valid.");
                }
            },
            OnChallenge = context =>
            {
                if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return context.Response.WriteAsJsonAsync(ApiEnvelope<object>.Fail("未授权。"));
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// 安全响应头：放在管道最前面，确保所有响应（含静态资源与错误页）都带上。
// 这里刻意只添加不影响资源加载的指令——完整的 default-src 'self' 还需验证
// Blazor 的内联脚本与 WebSocket 行为，在浏览器端回归测试（E2E）接入 CI 之前
// 不宜贸然收紧，否则可能以「页面白屏」的形式破坏应用。
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;

    // 阻止浏览器对响应内容做 MIME 嗅探。
    // 这是「上传 HTML / SVG 后被内联执行」的主要兜底：即使 Content-Type 判断出错，
    // 浏览器也不会把内容当成脚本或 HTML 执行。
    headers["X-Content-Type-Options"] = "nosniff";

    // 避免 URL 中的敏感信息（例如 SignalR 的 ?access_token=）通过 Referer 泄露给第三方站点
    headers["Referrer-Policy"] = "same-origin";

    // 禁止被嵌入 iframe，防点击劫持
    headers["X-Frame-Options"] = "DENY";
    headers["Content-Security-Policy"] = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";

    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// 根据配置决定是否启用 Swagger UI（开发环境默认启用，生产环境默认禁用）
var enableSwaggerUI = builder.Configuration.GetValue<bool?>("OpenApi:EnableSwaggerUI")
                      ?? app.Environment.IsDevelopment();

if (enableSwaggerUI)
{
    app.UseSwagger(options => { options.RouteTemplate = "openapi/{documentName}.json"; });
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "AnyDrop API v1");
        options.RoutePrefix = "swagger";
    });
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseRequestLocalization();
app.UseRouting();
app.UseAntiforgery();
app.UseAuthentication();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    // Blazor 框架资源、SignalR、API、静态资源等路径直接放行，
    // 不进入业务路由守卫，防止 blazor.web.js 等被重定向到 /setup
    var isStaticAssetRequest = Path.HasExtension(path);
    if (isStaticAssetRequest ||
        path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
        // 健康检查必须匿名可达，否则容器编排会被重定向到 /setup 或 /login
        path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/not-found", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var userService = context.RequestServices.GetRequiredService<IUserService>();
    var hasUser = await userService.HasUserAsync(context.RequestAborted);

    if (!hasUser && !path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/setup");
        return;
    }

    if (hasUser && path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect(context.User.Identity?.IsAuthenticated == true ? "/" : "/login");
        return;
    }

    var isAnonymousPage = path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
                          || path.StartsWith("/error", StringComparison.OrdinalIgnoreCase);
    if (hasUser && !isAnonymousPage && context.User.Identity?.IsAuthenticated != true)
    {
        var returnUrl = Uri.EscapeDataString($"{context.Request.Path}{context.Request.QueryString}");
        context.Response.Redirect($"/login?returnUrl={returnUrl}");
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapHub<ShareHub>("/hubs/share")
   .DisableAntiforgery();
app.MapShareItemEndpoints();
app.MapFileEndpoints();
app.MapTopicEndpoints();
app.MapAuthEndpoints();
app.MapSettingsEndpoints();
app.MapHealthEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

await app.Services.MigrateDatabaseAsync();

app.Run();
