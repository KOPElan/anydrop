namespace AnyDrop.App.Services;

/// <summary>一次选择返回的一份文件。</summary>
public sealed record PickedFile(Stream Stream, string FileName, string MimeType);

/// <summary>选择器的返回状态：Ok=选到了文件，Cancelled=用户取消，PermissionDenied=系统权限被拒。</summary>
public enum PickStatus
{
    Ok,
    Cancelled,
    PermissionDenied
}

/// <summary>多选结果。Status != Ok 时 Files 为空。</summary>
public sealed record PickResult(IReadOnlyList<PickedFile> Files, PickStatus Status);

/// <summary>文件与媒体选择器接口，封装平台差异。</summary>
public interface IPickerService
{
    Task<(Stream Stream, string FileName, string MimeType)?> PickFileAsync();
    Task<(Stream Stream, string FileName, string MimeType)?> PickPhotoAsync();
    Task<(Stream Stream, string FileName, string MimeType)?> PickVideoAsync();

    /// <summary>多选文件（FilePicker.PickMultipleAsync）。</summary>
    Task<PickResult> PickFilesAsync();

    /// <summary>多选照片（MediaPicker.PickPhotosAsync）。</summary>
    Task<PickResult> PickPhotosAsync();

    /// <summary>多选视频（MediaPicker.PickVideosAsync）。</summary>
    Task<PickResult> PickVideosAsync();
}
