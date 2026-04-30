namespace AnyDrop.App.Services;

/// <summary>文件与媒体选择器接口，封装平台差异。</summary>
public interface IPickerService
{
    Task<(Stream Stream, string FileName, string MimeType)?> PickFileAsync();
    Task<(Stream Stream, string FileName, string MimeType)?> PickPhotoAsync();
    Task<(Stream Stream, string FileName, string MimeType)?> PickVideoAsync();
}
