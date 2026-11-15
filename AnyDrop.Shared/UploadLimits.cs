namespace AnyDrop.Shared;

/// <summary>
/// 上传上限集中定义（D2 定稿：统一 500MB）。服务端与 MAUI 客户端共用同一份常量，
/// 不再各处散落 1GB 回退值与 100MB 开发值。
/// </summary>
public static class UploadLimits
{
    /// <summary>上传上限：500MB = 524288000 字节（D2，Elan 2026-11-14 定稿）。</summary>
    public const long MaxFileSizeBytes = 500L * 1024 * 1024;

    /// <summary>分片默认大小：4MB。服务端在初始化响应里返回实际值，客户端以返回值为准。</summary>
    public const int DefaultChunkSizeBytes = 4 * 1024 * 1024;

    /// <summary>单次 multipart 上传仅用于小于此值的文件；达到此值一律走分片上传。</summary>
    public const long SingleUploadMaxBytes = 20L * 1024 * 1024;

    /// <summary>分片会话有效期：24 小时；过期后由对账清扫（S1-01 孤儿治理）。</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
}
