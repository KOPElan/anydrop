using AnyDrop.Components;
using AnyDrop.Api;
using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<ITopicService, TopicService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddSingleton<LinkMetadataService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<ExpiredMessageCleanupService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
            return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : CookieAuthenticationDefaults.AuthenticationScheme;
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "anydrop.auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
else
{
    app.UseSwagger(options => { options.RouteTemplate = "openapi/{documentName}.json"; });
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "AnyDrop API v1");
        options.RoutePrefix = "swagger";
    });
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseAntiforgery();
app.UseAuthentication();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var isStaticAssetRequest = Path.HasExtension(path);
    if (path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/not-found", StringComparison.OrdinalIgnoreCase) ||
        isStaticAssetRequest)
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
app.MapHub<ShareHub>("/hubs/share");
app.MapShareItemEndpoints();
app.MapTopicEndpoints();
app.MapAuthEndpoints();
app.MapSettingsEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

await app.Services.MigrateAndSeedAsync();

app.Run();
