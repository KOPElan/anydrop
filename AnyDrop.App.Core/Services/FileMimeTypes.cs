namespace AnyDrop.App.Services;

/// <summary>
/// 按文件名扩展名推断 MIME 类型（选择器返回的 ContentType 为空时的兜底）。
/// 与服务端 <c>MimeTypeHelper</c> 的映射保持一致：未知扩展名返回
/// application/octet-stream，不猜测。
/// </summary>
public static class FileMimeTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".bmp"] = "image/bmp",
        [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".csv"] = "text/csv"
    };

    public static string FromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "application/octet-stream";

        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && ByExtension.TryGetValue(ext, out var mime)
            ? mime
            : "application/octet-stream";
    }
}
