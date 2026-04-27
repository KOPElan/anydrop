using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Mvc;

namespace AnyDrop.Api;

public static class FileEndpoints
{
    private const long DefaultMaxFileSizeBytes = 1L * 1024 * 1024 * 1024;

    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/files").WithTags("Files").RequireAuthorization();

        group.MapPost("/", UploadFileAsync)
            .WithName("UploadFile")
            .WithSummary("通过 HTTP multipart/form-data 上传文件并创建分享条目")
            .DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// 接收文件上传请求并创建 ShareItem。
    /// 文件通过标准 HTTP multipart/form-data 传输，避免占用 SignalR 连接带宽。
    /// </summary>
    public static async Task<IResult> UploadFileAsync(
        IFormFile file,
        [FromForm] Guid? topicId,
        [FromForm] bool burnAfterReading,
        IShareService shareService,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return TypedResults.BadRequest(ApiEnvelope<ShareItemDto>.Fail("未提供文件或文件为空。"));
        }

        if (!topicId.HasValue)
        {
            return TypedResults.BadRequest(ApiEnvelope<ShareItemDto>.Fail("必须指定目标主题（topicId）。"));
        }

        var maxFileSize = configuration.GetValue<long?>("Storage:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;
        if (file.Length > maxFileSize)
        {
            return TypedResults.BadRequest(ApiEnvelope<ShareItemDto>.Fail(
                $"文件大小（{file.Length:N0} 字节）超出限制（{maxFileSize:N0} 字节）。"));
        }

        try
        {
            // 使用 Path.GetFileName 防止浏览器传入完整路径（如 Windows 的 C:\... 格式）
            var fileName = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "upload";
            }

            var mimeType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

            await using var stream = file.OpenReadStream();
            var dto = await shareService.SendFileAsync(
                stream,
                fileName,
                mimeType,
                topicId.Value,
                knownFileSize: file.Length,
                burnAfterReading: burnAfterReading,
                ct: cancellationToken);

            return TypedResults.Ok(ApiEnvelope<ShareItemDto>.Ok(dto));
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(ApiEnvelope<ShareItemDto>.Fail(ex.Message));
        }
    }
}
