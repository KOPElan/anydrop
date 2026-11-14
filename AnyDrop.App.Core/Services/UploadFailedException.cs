namespace AnyDrop.App.Services;

/// <summary>
/// 服务端明确拒绝上传（4xx）：重试无意义，重试循环遇到它直接抛，不重试。
/// 继承 <see cref="HttpRequestException"/>，现有 catch 逻辑不受影响；
/// <see cref="ErrorCode"/> 供界面按码显示本地化文案（S3-06）。
/// </summary>
public sealed class UploadFailedException : HttpRequestException
{
    /// <summary>服务端错误码（见 <see cref="Shared.ApiErrorCodes"/>）；读不到信封时为 null。</summary>
    public string? ErrorCode { get; }

    public UploadFailedException(string message, System.Net.HttpStatusCode statusCode, string? errorCode = null)
        : base(message, null, statusCode)
    {
        ErrorCode = errorCode;
    }
}
