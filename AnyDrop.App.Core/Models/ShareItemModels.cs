using AnyDrop.Shared;

namespace AnyDrop.App.Models;

// ShareContentType, ShareItemDto, CreateTextShareItemRequest
// 均已迁移到 AnyDrop.Shared，可直接使用。

/// <summary>外部分享里的单个文件：接收时已复制进 App 缓存目录的本地路径 + 真实 MIME 类型。</summary>
public sealed record SharedFile(string Path, string MimeType);

/// <summary>
/// App 特有：来自其他应用的分享内容（Android/iOS Share Intent）。
/// S2-06：文件不再存 content:// URI 字符串（消费方 File.OpenRead 无法打开），
/// 而是接收时立即用 ContentResolver 复制进缓存目录的本地路径，每文件带真实 MIME 类型。
/// </summary>
public sealed record SharedContent(string? Text, IReadOnlyList<SharedFile> Files);
