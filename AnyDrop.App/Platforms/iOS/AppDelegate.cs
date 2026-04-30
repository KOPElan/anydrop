using Foundation;
using UIKit;

namespace AnyDrop.App
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
        {
            // 处理 anydrop:// 深链接
            if (url.Scheme == "anydrop")
            {
                var host = url.Host;
                if (host == "topic" && Guid.TryParse(url.Path?.TrimStart('/'), out var topicId))
                    PendingShareStore.NotificationTopicId = topicId;
            }
            return true;
        }

        public override bool ContinueUserActivity(UIApplication application, NSUserActivity userActivity, UIApplicationRestorationHandler completionHandler)
        {
            return base.ContinueUserActivity(application, userActivity, completionHandler);
        }
    }
}

