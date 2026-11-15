namespace AnyDrop.App.Services;

/// <summary>
/// 文件与媒体选择器，封装 FilePicker / MediaPicker。
/// net10.0 测试目标返回 null / Cancelled。
/// S2-08：多选（照片/视频/文件均支持多选）、真实 MIME 类型（ContentType
/// 为空时按扩展名推断，不再写死 image/jpeg / video/mp4）、权限被拒时
/// 返回 PermissionDenied（由调用方给文案与去设置入口）。
/// </summary>
public sealed class PickerService : IPickerService
{
    public async Task<(Stream Stream, string FileName, string MimeType)?> PickFileAsync()
    {
        var result = await PickFilesAsync().ConfigureAwait(false);
        var file = result.Files.FirstOrDefault();
        return file is null ? null : (file.Stream, file.FileName, file.MimeType);
    }

    public async Task<(Stream Stream, string FileName, string MimeType)?> PickPhotoAsync()
    {
        var result = await PickPhotosAsync().ConfigureAwait(false);
        var file = result.Files.FirstOrDefault();
        return file is null ? null : (file.Stream, file.FileName, file.MimeType);
    }

    public async Task<(Stream Stream, string FileName, string MimeType)?> PickVideoAsync()
    {
        var result = await PickVideosAsync().ConfigureAwait(false);
        var file = result.Files.FirstOrDefault();
        return file is null ? null : (file.Stream, file.FileName, file.MimeType);
    }

    public async Task<PickResult> PickFilesAsync()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        try
        {
            var results = await FilePicker.PickMultipleAsync().ConfigureAwait(false);
            var files = new List<PickedFile>();
            foreach (var result in results ?? [])
            {
                var stream = await result.OpenReadAsync().ConfigureAwait(false);
                files.Add(new PickedFile(stream, result.FileName, ResolveMimeType(result)));
            }
            return new PickResult(files, files.Count > 0 ? PickStatus.Ok : PickStatus.Cancelled);
        }
        catch (PermissionException)
        {
            return new PickResult([], PickStatus.PermissionDenied);
        }
        catch (OperationCanceledException)
        {
            return new PickResult([], PickStatus.Cancelled);
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return new PickResult([], PickStatus.Cancelled);
#endif
    }

    public async Task<PickResult> PickPhotosAsync()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        try
        {
            var results = await MediaPicker.Default.PickPhotosAsync().ConfigureAwait(false);
            var files = new List<PickedFile>();
            foreach (var result in results ?? [])
            {
                var stream = await result.OpenReadAsync().ConfigureAwait(false);
                files.Add(new PickedFile(stream, result.FileName, ResolveMimeType(result)));
            }
            return new PickResult(files, files.Count > 0 ? PickStatus.Ok : PickStatus.Cancelled);
        }
        catch (PermissionException)
        {
            return new PickResult([], PickStatus.PermissionDenied);
        }
        catch (OperationCanceledException)
        {
            return new PickResult([], PickStatus.Cancelled);
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return new PickResult([], PickStatus.Cancelled);
#endif
    }

    public async Task<PickResult> PickVideosAsync()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        try
        {
            var results = await MediaPicker.Default.PickVideosAsync().ConfigureAwait(false);
            var files = new List<PickedFile>();
            foreach (var result in results ?? [])
            {
                var stream = await result.OpenReadAsync().ConfigureAwait(false);
                files.Add(new PickedFile(stream, result.FileName, ResolveMimeType(result)));
            }
            return new PickResult(files, files.Count > 0 ? PickStatus.Ok : PickStatus.Cancelled);
        }
        catch (PermissionException)
        {
            return new PickResult([], PickStatus.PermissionDenied);
        }
        catch (OperationCanceledException)
        {
            return new PickResult([], PickStatus.Cancelled);
        }
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return new PickResult([], PickStatus.Cancelled);
#endif
    }

    /// <summary>优先用平台返回的真实 ContentType，为空时按扩展名推断。</summary>
    private static string ResolveMimeType(FileResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ContentType))
            return result.ContentType;
        return FileMimeTypes.FromFileName(result.FileName);
    }
}
