using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AnyDrop.App.Infrastructure;
using AnyDrop.App.Models;

namespace AnyDrop.App
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
        Exported = true)]
    [IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
    [IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "image/*")]
    [IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            HandleIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            HandleIntent(intent);
        }

        private static void HandleIntent(Intent? intent)
        {
            if (intent is null) return;

            // 通知深链接
            var topicId = intent.GetStringExtra("topic_id");
            if (topicId is not null && Guid.TryParse(topicId, out var tid))
            {
                PendingShareStore.NotificationTopicId = tid;
                return;
            }

            if (intent.Action != Intent.ActionSend) return;

            // 文本分享
            if (intent.Type == "text/plain")
            {
                var text = intent.GetStringExtra(Intent.ExtraText);
                if (text is not null)
                    PendingShareStore.Content = new SharedContent(text, [], null);
                return;
            }

            // 文件/图片分享
#pragma warning disable CA1422
            var uri = intent.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
#pragma warning restore CA1422
            if (uri is not null)
            {
                var mimeType = intent.Type ?? "application/octet-stream";
                var path = uri.ToString() ?? string.Empty;
                PendingShareStore.Content = new SharedContent(null, [path], mimeType);
            }
        }
    }
}
