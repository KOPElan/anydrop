namespace AnyDrop.Services;

/// <summary>
/// MIME 类型规范化（SEC-07）。
///
/// 上传方完全控制 multipart 段头的 <c>Content-Type</c>，可能带参数
/// （<c>image/svg+xml; charset=utf-8</c>）或尾空格。所有 MIME 比较前必须先
/// 规范化——只取 <c>;</c> 前的 media type 并转小写——否则精确字符串比较
/// 会被绕过，SVG 等危险类型可能落入内联分支。
/// </summary>
internal static class MimeTypeHelper
{
    /// <summary>去参数、去首尾空白、转小写；空值回退为 application/octet-stream。</summary>
    public static string Normalize(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return "application/octet-stream";
        }

        var semicolon = mimeType.IndexOf(';');
        var mediaType = (semicolon >= 0 ? mimeType[..semicolon] : mimeType).Trim().ToLowerInvariant();
        return mediaType.Length == 0 ? "application/octet-stream" : mediaType;
    }

    /// <summary>是否为 SVG（含带参数/大小写变体）。SVG 可内嵌脚本，永不内联。</summary>
    public static bool IsSvg(string? mimeType)
        => Normalize(mimeType).StartsWith("image/svg", StringComparison.Ordinal);
}
