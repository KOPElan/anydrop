using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Util;
using Android.Webkit;
using AnyDrop.App.Infrastructure;
using AnyDrop.App.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;

namespace AnyDrop.App
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
        Exported = true)]
    [IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
    [IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "image/*")]
    [IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
    // S2-06：多文件分享（相册多选）走 ACTION_SEND_MULTIPLE
    [IntentFilter([Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "image/*")]
    [IntentFilter([Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
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

            var store = ResolvePendingShareStore();

            // 通知深链接
            var topicId = intent.GetStringExtra("topic_id");
            if (topicId is not null && Guid.TryParse(topicId, out var tid))
            {
                store.NotificationTopicId = tid;
                return;
            }

            // S2-06：多文件分享
            if (intent.Action == Intent.ActionSendMultiple)
            {
#pragma warning disable CA1422
                var uris = intent.GetParcelableArrayListExtra(Intent.ExtraStream);
#pragma warning restore CA1422
                var files = new List<SharedFile>();
                if (uris is not null)
                {
                    foreach (var parcelable in uris)
                    {
                        if (parcelable is Android.Net.Uri sharedUri)
                        {
                            var copied = CopyUriToCache(sharedUri, intent.Type);
                            if (copied is not null)
                            {
                                files.Add(copied);
                            }
                        }
                    }
                }

                if (files.Count > 0)
                {
                    SaveShare(store, new SharedContent(null, files));
                }
                else
                {
                    Log.Warn("AnyDrop", "ACTION_SEND_MULTIPLE 未复制到任何文件");
                }

                return;
            }

            if (intent.Action != Intent.ActionSend) return;

            // 文本分享
            if (intent.Type == "text/plain")
            {
                var text = intent.GetStringExtra(Intent.ExtraText);
                if (text is not null)
                    SaveShare(store, new SharedContent(text, []));
                return;
            }

            // 文件/图片分享（单文件）
#pragma warning disable CA1422
            var uri = intent.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
#pragma warning restore CA1422
            if (uri is not null)
            {
                var copied = CopyUriToCache(uri, intent.Type);
                if (copied is not null)
                {
                    SaveShare(store, new SharedContent(null, [copied]));
                }
            }
        }

        /// <summary>
        /// S2-06：接收分享时立即用 ContentResolver 把 content:// 文件复制进 App 缓存目录。
        /// 旧代码只存了 content:// URI 字符串，消费方 File.OpenRead(path) 打不开，分享必失败。
        /// Intent 授予的读取权限只在接收时刻有效，立即复制后不再依赖原 Uri。
        /// </summary>
        private static SharedFile? CopyUriToCache(Android.Net.Uri uri, string? intentMimeType)
        {
            try
            {
                var resolver = Android.App.Application.Context.ContentResolver!;
                // 每文件的真实 MIME 类型优先取该 Uri 自身的类型，Intent 的 Type 只是兜底
                // （多文件分享时 Intent.Type 常为 */*，无法区分各文件）。
                var mimeType = resolver.GetType(uri) ?? intentMimeType ?? "application/octet-stream";
                var displayName = QueryDisplayName(resolver, uri);
                var fileName = string.IsNullOrEmpty(displayName)
                    ? $"share-{Guid.NewGuid():N}{ExtensionForMimeType(mimeType)}"
                    : displayName;
                // 显示名可能含路径分隔符，只取文件名部分，防止路径穿越写出缓存目录。
                fileName = Path.GetFileName(fileName);
                var destPath = Path.Combine(
                    FileSystem.CacheDirectory,
                    $"share-{Guid.NewGuid():N}-{fileName}");

                using var input = resolver.OpenInputStream(uri);
                if (input is null)
                {
                    Log.Warn("AnyDrop", $"无法打开分享文件流: {uri}");
                    return null;
                }

                using var output = File.Create(destPath);
                input.CopyTo(output);
                return new SharedFile(destPath, mimeType);
            }
            catch (Exception ex)
            {
                Log.Warn("AnyDrop", $"复制分享文件失败 {uri}: {ex.Message}");
                return null;
            }
        }

        private static string? QueryDisplayName(ContentResolver resolver, Android.Net.Uri uri)
        {
            try
            {
                using var cursor = resolver.Query(uri, [OpenableColumns.DisplayName], null, null, null);
                if (cursor is null || !cursor.MoveToFirst())
                {
                    return null;
                }

                var index = cursor.GetColumnIndex(OpenableColumns.DisplayName);
                return index >= 0 ? cursor.GetString(index) : null;
            }
            catch (Exception ex)
            {
                Log.Warn("AnyDrop", $"读取分享文件名失败 {uri}: {ex.Message}");
                return null;
            }
        }

        private static string ExtensionForMimeType(string mimeType)
        {
            try
            {
                var extension = MimeTypeMap.Singleton?.GetExtensionFromMimeType(mimeType);
                return string.IsNullOrEmpty(extension) ? string.Empty : "." + extension;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void SaveShare(IPendingShareStore store, SharedContent content)
        {
            try
            {
                // 同步写盘完成才返回：FilePendingShareStore 的写盘本身是同步的，
                // 避免进程在异步写盘完成前被杀导致分享丢失。
                store.SaveAsync(content).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warn("AnyDrop", $"保存待分享内容失败: {ex.Message}");
            }
        }

        private static IPendingShareStore ResolvePendingShareStore()
        {
            try
            {
                var store = MauiApplication.Current?.Services.GetService<IPendingShareStore>();
                if (store is not null)
                {
                    return store;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("AnyDrop", $"从 DI 解析待分享存储失败，使用直连实现: {ex.Message}");
            }

            // 回退：DI 尚未就绪时直接建文件实现（同一 JSON 文件，数据一致）。
            return new FilePendingShareStore(FileSystem.AppDataDirectory, FileSystem.CacheDirectory);
        }
    }
}
