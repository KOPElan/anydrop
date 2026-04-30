using AnyDrop.App.Infrastructure;
using AnyDrop.App.Services;
using Microsoft.Extensions.Logging;

namespace AnyDrop.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

#if ANDROID || IOS || MACCATALYST || WINDOWS
            // 本地通知通过 INotificationService 的平台实现处理
#endif

            // 基础设施
            builder.Services.AddSingleton<AppEventBus>();
            builder.Services.AddSingleton<ISecureTokenStorage, SecureTokenStorage>();
            builder.Services.AddSingleton<IServerConfigService, ServerConfigService>();
            builder.Services.AddSingleton<IConnectivityService, ConnectivityService>();
            builder.Services.AddSingleton<HubConnectionManager>();
            builder.Services.AddSingleton<ISignalRService, SignalRService>();
            builder.Services.AddSingleton<INotificationService, NotificationService>();
            builder.Services.AddSingleton<IPickerService, PickerService>();

            // AuthDelegatingHandler（Transient）
            builder.Services.AddTransient<AuthDelegatingHandler>();

            // 命名 HttpClient "api"，挂载 AuthDelegatingHandler
            builder.Services.AddHttpClient("api", (sp, client) =>
            {
                var config = sp.GetRequiredService<IServerConfigService>();
                var baseUrl = config.GetBaseUrl();
                if (!string.IsNullOrEmpty(baseUrl))
                    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(30);
            }).AddHttpMessageHandler<AuthDelegatingHandler>();

            // 用于 ServerConfigService 验证 URL（无 auth handler）
            builder.Services.AddHttpClient("plain");

            // Scoped 业务服务
            builder.Services.AddScoped<IAppStateService, AppStateService>();
            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<IShareService, ShareService>();
            builder.Services.AddScoped<IFileUploadService, FileUploadService>();
            builder.Services.AddScoped<ITopicService, TopicService>();
            builder.Services.AddScoped<ISearchService, SearchService>();
            builder.Services.AddScoped<ISettingsService, SettingsService>();

            return builder.Build();
        }
    }
}
