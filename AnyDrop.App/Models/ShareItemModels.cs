using AnyDrop.Shared;

namespace AnyDrop.App.Models;

// ShareContentType, ShareItemDto, CreateTextShareItemRequest
// 均已迁移到 AnyDrop.Shared，可直接使用。

/// <summary>App 特有：来自其他应用的分享内容（Android/iOS Share Intent）。</summary>
public sealed record SharedContent(string? Text, IReadOnlyList<string> FilePaths, string? MimeType);

