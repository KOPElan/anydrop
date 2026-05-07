namespace AnyDrop.App
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
#if IOS || MACCATALYST
            // 让 ContentPage 自动处理 iOS 安全区域（状态栏 + 底部 Home 区域），
            // BlazorWebView 将从状态栏下方开始渲染
            On<Microsoft.Maui.Controls.PlatformConfiguration.iOS>().SetUseSafeArea(true);
#endif
        }

#if ANDROID
        protected override void OnAppearing()
        {
            base.OnAppearing();
            SetAndroidStatusBarPadding();
        }

        private void SetAndroidStatusBarPadding()
        {
            try
            {
                var context = Android.App.Application.Context;
                if (context.Resources is null) return;
                // 通过系统资源获取状态栏高度（像素），转换为 MAUI 独立像素后设为顶部 Padding
                var resourceId = context.Resources.GetIdentifier("status_bar_height", "dimen", "android");
                if (resourceId > 0)
                {
                    var heightPx = context.Resources.GetDimensionPixelSize(resourceId);
                    var density = DeviceDisplay.Current.MainDisplayInfo.Density;
                    Padding = new Thickness(0, heightPx / density, 0, 0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] 获取状态栏高度失败: {ex.Message}");
            }
        }
#endif
    }
}
