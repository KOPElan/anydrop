using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Api;

public static class ShareItemEndpoints
{
    public static IEndpointRouteBuilder MapShareItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/share-items").WithTags("Share Items").RequireAuthorization();

        group.MapGet("/", GetRecentAsync)
            .WithName("GetRecentShareItems")
            .WithSummary("Get recent shared items");

        group.MapPost("/text", SendTextAsync)
            .WithName("SendTextShareItem")
            .WithSummary("Share a text item");

        group.MapGet("/{id:guid}/file", GetFileAsync)
            .WithName("GetShareItemFile")
            .WithSummary("Get a shared file");

        group.MapGet("/{id:guid}/thumbnail", GetThumbnailAsync)
            .WithName("GetShareItemThumbnail")
            .WithSummary("Get the thumbnail/preview image for an image or video share item");

        group.MapDelete("/cleanup", CleanupOldMessagesAsync)
            .WithName("CleanupOldMessages")
            .WithSummary("Manually clean up messages older than the specified number of months");

        group.MapDelete("/batch", BatchDeleteAsync)
            .WithName("BatchDeleteShareItems")
            .WithSummary("Batch delete share items by IDs");

        return app;
    }

    public static async Task<Ok<ApiEnvelope<IReadOnlyList<ShareItemDto>>>> GetRecentAsync(
        int? count,
        IShareService shareService,
        CancellationToken cancellationToken)
    {
        var items = await shareService.GetRecentAsync(count ?? 50, cancellationToken);
        return TypedResults.Ok(ApiEnvelope<IReadOnlyList<ShareItemDto>>.Ok(items));
    }

    public static async Task<Results<Ok<ApiEnvelope<ShareItemDto>>, BadRequest<ApiEnvelope<ShareItemDto>>>> SendTextAsync(
        CreateTextShareItemRequest request,
        IShareService shareService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return TypedResults.BadRequest(ApiEnvelope<ShareItemDto>.Fail("Content is required."));
        }

        try
        {
            var item = await shareService.SendTextAsync(request.Content, request.TopicId, burnAfterReading: request.BurnAfterReading, cancellationToken);
            return TypedResults.Ok(ApiEnvelope<ShareItemDto>.Ok(item));
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(ApiEnvelope<ShareItemDto>.Fail(ex.Message));
        }
    }

    public static async Task<Results<FileStreamHttpResult, NotFound<ApiEnvelope<object>>, BadRequest<ApiEnvelope<object>>>> GetFileAsync(
        Guid id,
        bool? download,
        AnyDropDbContext dbContext,
        IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.ShareItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound(ApiEnvelope<object>.Fail("消息不存在"));
        }

        if (item.ContentType is not (ShareContentType.File or ShareContentType.Image or ShareContentType.Video))
        {
            return TypedResults.BadRequest(ApiEnvelope<object>.Fail("该消息不包含文件"));
        }

        try
        {
            var stream = await fileStorageService.GetFileAsync(item.Content, cancellationToken);
            var contentType = string.IsNullOrWhiteSpace(item.MimeType) ? "application/octet-stream" : item.MimeType;
            var fileName = item.FileName ?? "download.bin";
            var asAttachment = download == true || ShouldForceAttachment(contentType);
            return asAttachment
                ? TypedResults.File(stream, contentType, fileName, enableRangeProcessing: true, lastModified: null, entityTag: null)
                : TypedResults.File(stream, contentType, enableRangeProcessing: true);
        }
        catch (FileNotFoundException)
        {
            return TypedResults.NotFound(ApiEnvelope<object>.Fail("文件不存在"));
        }
    }

    /// <summary>
    /// 手动清理指定月数前的消息，同时清理相关文件资源。
    /// </summary>
    public static async Task<Results<Ok<ApiEnvelope<CleanupResult>>, BadRequest<ApiEnvelope<object>>>> CleanupOldMessagesAsync(
        int months,
        IShareService shareService,
        CancellationToken cancellationToken)
    {
        if (months is not (1 or 3 or 6))
        {
            return TypedResults.BadRequest(ApiEnvelope<object>.Fail("months 参数必须为 1、3 或 6。"));
        }

        var deletedCount = await shareService.CleanupOldMessagesAsync(months, null, cancellationToken);
        return TypedResults.Ok(ApiEnvelope<CleanupResult>.Ok(new CleanupResult(deletedCount)));
    }

    /// <summary>
    /// 批量删除指定 ID 的消息，同时清理相关文件资源。
    /// </summary>
    public static async Task<Results<Ok<ApiEnvelope<object>>, BadRequest<ApiEnvelope<object>>>> BatchDeleteAsync(
        [FromBody] BatchDeleteRequest request,
        IShareService shareService,
        CancellationToken cancellationToken)
    {
        if (request.Ids is null || request.Ids.Count == 0)
        {
            return TypedResults.BadRequest(ApiEnvelope<object>.Fail("ids 不能为空。"));
        }

        if (request.Ids.Count > 500)
        {
            return TypedResults.BadRequest(ApiEnvelope<object>.Fail("单次批量删除不能超过 500 条。"));
        }

        var deletedCount = await shareService.DeleteShareItemsAsync(request.Ids, cancellationToken);
        return TypedResults.Ok(ApiEnvelope<object>.Ok(new { deleted = deletedCount }));
    }

    /// <summary>
    /// 获取图片或视频的缩略图文件流。若缩略图尚未生成则返回 404，客户端应回退到原始文件。
    /// </summary>
    public static async Task<Results<FileStreamHttpResult, NotFound<ApiEnvelope<object>>>> GetThumbnailAsync(
        Guid id,
        AnyDropDbContext dbContext,
        IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.ShareItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.ThumbnailPath })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null || string.IsNullOrWhiteSpace(item.ThumbnailPath))
        {
            return TypedResults.NotFound(ApiEnvelope<object>.Fail("缩略图尚未生成"));
        }

        try
        {
            var stream = await fileStorageService.GetFileAsync(item.ThumbnailPath, cancellationToken);
            return TypedResults.File(stream, "image/jpeg", enableRangeProcessing: true);
        }
        catch (FileNotFoundException)
        {
            return TypedResults.NotFound(ApiEnvelope<object>.Fail("缩略图文件不存在"));
        }
    }

    private static bool ShouldForceAttachment(string mimeType)
    {
        return mimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
               || mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)
               || mimeType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
               || mimeType.Equals("text/javascript", StringComparison.OrdinalIgnoreCase);
    }
}
