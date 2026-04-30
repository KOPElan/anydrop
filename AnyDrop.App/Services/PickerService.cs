namespace AnyDrop.App.Services;

/// <summary>
/// 文件与媒体选择器，封装 FilePicker / MediaPicker。
/// net10.0 测试目标返回 null。
/// </summary>
public sealed class PickerService : IPickerService
{
    public async Task<(Stream Stream, string FileName, string MimeType)?> PickFileAsync()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        var result = await FilePicker.PickAsync().ConfigureAwait(false);
        if (result is null) return null;
        var stream = await result.OpenReadAsync().ConfigureAwait(false);
        return (stream, result.FileName, result.ContentType ?? "application/octet-stream");
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return null;
#endif
    }

    public async Task<(Stream Stream, string FileName, string MimeType)?> PickPhotoAsync()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        var result = await MediaPicker.PickPhotoAsync().ConfigureAwait(false);
        if (result is null) return null;
        var stream = await result.OpenReadAsync().ConfigureAwait(false);
        return (stream, result.FileName, "image/jpeg");
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return null;
#endif
    }

    public async Task<(Stream Stream, string FileName, string MimeType)?> PickVideoAsync()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        var result = await MediaPicker.PickVideoAsync().ConfigureAwait(false);
        if (result is null) return null;
        var stream = await result.OpenReadAsync().ConfigureAwait(false);
        return (stream, result.FileName, "video/mp4");
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return null;
#endif
    }
}
