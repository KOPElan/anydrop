namespace AnyDrop.App.Services;

/// <summary>
/// 本地推送通知服务，使用平台原生 API。
/// net10.0 测试目标为 no-op 实现。
/// </summary>
public sealed class NotificationService : INotificationService
{
    public async Task<bool> RequestPermissionAsync()
    {
#if ANDROID
        try
        {
            var status = await Permissions.RequestAsync<Permissions.PostNotifications>().ConfigureAwait(false);
            return status == PermissionStatus.Granted;
        }
        catch
        {
            return false;
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return false;
#endif
    }

    public async Task ShowMessageNotificationAsync(string topicName, string preview, Guid topicId)
    {
#if ANDROID
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var context = Android.App.Application.Context;
                const string channelId = "anydrop_messages";
                if (context is null) return;
                var manager = (Android.App.NotificationManager?)context.GetSystemService(Android.Content.Context.NotificationService);
                if (manager is null) return;

                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                {
                    var channel = new Android.App.NotificationChannel(channelId, "消息通知", Android.App.NotificationImportance.Default);
                    manager.CreateNotificationChannel(channel);
                }

#pragma warning disable CS8602 // AndroidX Builder 方法链从不返回 null
                var builder = new AndroidX.Core.App.NotificationCompat.Builder(context!, channelId)
                    .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                    .SetContentTitle(topicName)
                    .SetContentText(preview)
                    .SetAutoCancel(true);

                manager.Notify(Math.Abs(topicId.GetHashCode()), builder.Build()!);
#pragma warning restore CS8602
            }).ConfigureAwait(false);
        }
        catch { /* 权限被拒绝时静默失败 */ }
#else
        await Task.CompletedTask.ConfigureAwait(false);
#endif
    }

    public async Task CancelAllAsync()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var manager = (Android.App.NotificationManager?)context.GetSystemService(Android.Content.Context.NotificationService);
            manager?.CancelAll();
        }
        catch { }
#endif
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
