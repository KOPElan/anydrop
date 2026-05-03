using AnyDrop.App.Services;

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
