namespace AnyDrop.App.Services;

/// <summary>
/// 本地推送通知服务，使用平台原生 API。
/// net10.0 测试目标为 no-op 实现（仅记 Debug 日志）。
/// S2-08：点击通知跳回对应话题（PendingIntent 带 topic_id，
/// MainActivity 已处理该 extra 并导航到话题）；通知 ID 由话题 Guid
/// 的前 4 字节稳定导出（同一话题覆盖旧通知，不同话题互不覆盖；
/// 旧实现 Math.Abs(topicId.GetHashCode()) 在 GetHashCode() == int.MinValue
/// 时抛 OverflowException，且跨进程不保证稳定）。
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
        System.Diagnostics.Debug.WriteLine("[NotificationService] 非 Android 平台，通知权限请求为 no-op");
        return false;
#endif
    }

    /// <summary>由话题 Guid 稳定导出非负通知 ID（同一话题始终同一 ID）。见 Core 的 NotificationIds。</summary>
    internal static int NotificationIdForTopic(Guid topicId) => NotificationIds.ForTopic(topicId);

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

                // 点击通知打开 MainActivity 并跳到对应话题；Immutable 满足 Android 12+ 要求。
                var intent = new Android.Content.Intent(context, typeof(MainActivity));
                intent.PutExtra("topic_id", topicId.ToString());
                intent.SetFlags(Android.Content.ActivityFlags.SingleTop | Android.Content.ActivityFlags.ClearTop);
                var pendingIntent = Android.App.PendingIntent.GetActivity(
                    context,
                    0,
                    intent,
                    Android.App.PendingIntentFlags.UpdateCurrent | Android.App.PendingIntentFlags.Immutable);

#pragma warning disable CS8602 // AndroidX Builder 方法链从不返回 null
                var builder = new AndroidX.Core.App.NotificationCompat.Builder(context!, channelId)
                    .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                    .SetContentTitle(topicName)
                    .SetContentText(preview)
                    .SetContentIntent(pendingIntent)
                    .SetAutoCancel(true);

                manager.Notify(NotificationIdForTopic(topicId), builder.Build()!);
#pragma warning restore CS8602
            }).ConfigureAwait(false);
        }
        catch { /* 权限被拒绝时静默失败 */ }
#else
        await Task.CompletedTask.ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[NotificationService] 非 Android 平台，通知未显示：{topicName} / {preview}");
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
#else
        System.Diagnostics.Debug.WriteLine("[NotificationService] 非 Android 平台，取消通知为 no-op");
#endif
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
