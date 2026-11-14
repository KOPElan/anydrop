using AnyDrop.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AnyDrop.App
{
    public partial class App : Application
    {
        private readonly ISecureTokenStorage _tokenStorage;
        private readonly IServerConfigService _serverConfig;

        public App(ISecureTokenStorage tokenStorage, IServerConfigService serverConfig)
        {
            _tokenStorage = tokenStorage;
            _serverConfig = serverConfig;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = "AnyDrop" };
        }

        protected override async void OnResume()
        {
            base.OnResume();
            await NavigateOnStartupAsync();

            // S2-05：回到前台时恢复实时连接并补发离线发件箱。
            // SignalR 的 Reconnected 事件会触发 MessageList 的增量补拉；
            // 发件箱补发后新消息经 SignalR/补拉进入列表，无需在此直接写 AppState。
            try
            {
                var services = Handler?.MauiContext?.Services;
                if (services is null)
                {
                    return;
                }

                var signalR = services.GetService<ISignalRService>();
                if (signalR is not null)
                {
                    await signalR.StartAsync();
                }

                var outbox = services.GetService<IOutboxService>();
                var shareService = services.GetService<IShareService>();
                if (outbox is not null && shareService is not null)
                {
                    await outbox.DrainAsync(m => shareService.SendTextAsync(
                        new AnyDrop.Shared.CreateTextShareItemRequest(m.Content, m.TopicId, m.BurnAfterReading)));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] OnResume 恢复失败: {ex.Message}");
            }
        }

        private async Task NavigateOnStartupAsync()
        {
            await Task.Yield(); // 等待 Blazor 路由初始化
#if ANDROID || IOS || MACCATALYST || WINDOWS
            if (!_serverConfig.HasBaseUrl())
            {
                // 无 BaseUrl，跳转配置页（由 Routes.razor 处理）
                return;
            }
            if (!await _tokenStorage.IsAuthenticatedAsync())
            {
                // 有 BaseUrl 但无 Token，跳转登录页（由 Routes.razor 处理）
                return;
            }
            // Token 有效，跳转主界面
#endif
        }
    }
}
