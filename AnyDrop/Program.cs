using AnyDrop.Api;
using AnyDrop.Components;
using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Infrastructure;
using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Net;
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

// S1-05（DAT-11）：Data Protection 密钥持久化。Cookie 与防伪令牌都由它签名，
// 密钥只放内存时容器一重建全员掉登录。默认落到数据库同目录下的 keys 子目录
// （Docker 里是 /data/keys，随 anydrop-data 卷持久化；本地是 data/keys），
// 可用 DataProtection:KeysPath（环境变量 DataProtection__KeysPath）显式指定。
// SetApplicationName 让密钥环与应用名绑定，容器重建后仍认同一套密钥。
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(keysPath))
{
    keysPath = Path.Combine(dbDirectory ?? "data", "keys");
}
keysPath = Path.GetFullPath(keysPath);
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("AnyDrop")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// S1-03（DAT-09）：改用 DbContextFactory。Blazor Server 里 scoped 服务
// 与电路同寿命，一个请求作用域的 DbContext 会被同一电路持有数小时，
// 变更跟踪越积越多、还会读到过期数据。此后每个写操作都用工厂建短寿命
// 上下文；请求作用域的 API 端点同样每次调用建新上下文。
// WAL 在启动时由 DatabaseMigrationExtensions.EnsureWalModeAsync 对库文件设置
// （按文件持久化）；busy_timeout 由 SqliteBusyTimeoutInterceptor 在每次打开
// 连接时设置（按连接生效）。见 S0-04。
builder.Services.AddDbContextFactory<AnyDropDbContext>(options =>
    options.UseSqlite($"Data Source={fullDbPath}")
        .AddInterceptors(new SqliteBusyTimeoutInterceptor()));
builder.Services.AddSingleton<DbWriteGate>();
builder.Services.AddSignalR();

// 反向代理头的可信来源统一由 ReverseProxy 节配置（见下面的 reverseProxyOptions），
// 不再另设 ForwardedHeaders 节——两个 Configure 同时存在会让配置分裂、行为取决于注册顺序。

// S0-03：HTTPS 重定向的目标端口。应用本身只监听容器内 plain HTTP，
// 若不显式指定，HttpsRedirectionMiddleware 会因无法确定 HTTPS 端口
// 而静默放行（日志只留一条警告），生产上等于重定向没生效。
// 反代的公开 HTTPS 端口通常是 443；非标准端口时配 ReverseProxy:HttpsPort。
builder.Services.Configure<HttpsRedirectionOptions>(options =>
{
    options.HttpsPort = builder.Configuration.GetValue<int?>("ReverseProxy:HttpsPort") ?? 443;
});
// 统一的时间源。业务代码一律通过 TimeProvider 取当前时间，
// 从而可以在测试中用假时钟确定性地验证限流冷却、令牌过期、阅后即焚等时间相关逻辑。
builder.Services.AddSingleton(TimeProvider.System);
// S1-07（D6）：后台任务统一调度时区（配置 BackgroundTasks:TimeZoneId，
// 未配置时用当前计算机时区）。单例，构造时即记启动日志。
builder.Services.AddSingleton<BackgroundTaskTimeZone>();
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<StorageHealthService>();
builder.Services.AddScoped<ITopicService, TopicService>();
builder.Services.AddScoped<ITopicStateService, TopicStateService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
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

// 反向代理支持（默认关闭）。详见 ReverseProxyOptions 的说明。
var reverseProxyOptions = builder.Configuration.GetSection("ReverseProxy").Get<ReverseProxyOptions>()
                          ?? new ReverseProxyOptions();
if (reverseProxyOptions.Enabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // 不用 ForwardLimit 限制跳数，而由可信来源列表界定：只有来自
        // KnownProxies / KnownNetworks 的请求，其 X-Forwarded-* 才会被采纳。
        options.ForwardLimit = null;

        var (proxies, networks) = reverseProxyOptions.ParseTrustedSources();

        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    });
}

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
        // S0-03（D3）：默认只走 HTTPS。Auth:CookieSecurePolicy 可配
        // Always / SameAsRequest / None；纯 HTTP 局域网例外需显式配 None，
        // 否则浏览器不会回传 Cookie，登录后立刻被登出。
        options.Cookie.SecurePolicy = Enum.TryParse<CookieSecurePolicy>(
                authOptions.CookieSecurePolicy, ignoreCase: true, out var securePolicy)
            ? securePolicy
            : CookieSecurePolicy.Always;
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

// S1-07（D6）：显式解析后台任务时区，让生效时区与来源一定出现在启动日志里，
// 而不是等第一个后台任务启动时才出现。
app.Services.GetRequiredService<BackgroundTaskTimeZone>();

// S1-05（DAT-11）：密钥目录进启动日志——丢密钥等于全员掉登录，
// 部署后一眼就能确认密钥落到了持久卷而不是容器内临时目录。
app.Logger.LogInformation("Data Protection keys persisted to {KeysPath}.", keysPath);

// 必须放在管道最前面：任何读取 RemoteIpAddress 或 Request.Scheme 的中间件
// （限流、访问日志、Cookie 的 Secure 判定）都要看到代理转发的真实值。
// 注意 UseForwardedHeaders 只信任上面配置过的代理，未知来源的 X-Forwarded-For 会被丢弃。
if (reverseProxyOptions.Enabled)
{
    app.UseForwardedHeaders();
}

// S0-03（D3）：默认要求 TLS 反代。ReverseProxy:RequireHttps 未配置时，
// 生产环境默认 true、开发环境默认 false（本地开发仍是 http://0.0.0.0:5002）。
// 纯 HTTP 局域网例外：显式设置 ReverseProxy__RequireHttps=false，
// 同时须把 Auth:CookieSecurePolicy 设为 None，否则 Cookie 不会被浏览器回传。
var requireHttps = reverseProxyOptions.RequireHttps ?? !app.Environment.IsDevelopment();
if (requireHttps)
{
    app.UseHsts();
    // 健康检查探针（/health）不重定向：编排器与 compose healthcheck
    // 在容器内用 plain HTTP 访问它，重定向到 https 会因容器内无 TLS
    // 监听而失败，容器会被误判为不健康。
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase),
        branch => branch.UseHttpsRedirection());
}
else
{
    app.Logger.LogWarning(
        "ReverseProxy:RequireHttps is disabled: serving plain HTTP. " +
        "Only use this on a trusted LAN; set Auth:CookieSecurePolicy=None or login cookies will be dropped by browsers.");
}

// 访问日志：默认只记录慢请求（级别为 Warning），可通过 AnyDrop.RequestLog 调整
app.UseMiddleware<RequestLoggingMiddleware>();

// 安全响应头：确保所有响应（含静态资源与错误页）都带上。
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

    // 完整的内容安全策略。
    // 唯一保留的例外是 style-src 的 'unsafe-inline'：组件里大量使用 style 属性
    // （132 处），而内联样式属性无法用 nonce 或 hash 覆盖。
    // script-src 已收紧为 'self'：页面内不再有内联 <script>，原先的内联 onerror
    // 也改成了由同源脚本在捕获阶段统一处理（见 wwwroot/js/image-fallback.js）。
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "media-src 'self' blob:; " +
        "font-src 'self'; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "object-src 'none'";

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
   .RequireAuthorization()
   .DisableAntiforgery();
app.MapShareItemEndpoints();
app.MapFileEndpoints();
app.MapTopicEndpoints();
app.MapAuthEndpoints();
app.MapSettingsEndpoints();
app.MapStorageEndpoints();
app.MapHealthEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

// S1-04（DAT-10）：迁移失败时启动即失败并记一条致命日志，然后以非零码退出，
// 而不是带着坏库/半截表结构继续跑。顶层语句里 return int 即进程退出码，
// 容器编排器能看到非零退出并按重启策略处理。
try
{
    await app.Services.MigrateDatabaseAsync();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup");
    logger.LogCritical(
        ex,
        "数据库迁移失败，服务端拒绝启动。请先备份 data/anydrop.db，再按 README「数据库迁移」一节修复；切勿删除数据库文件重建——会丢失全部用户与消息。");
    return 1;
}

app.Run();

// 正常关机时退出码 0（上面迁移失败分支已 return 1）。
return 0;
