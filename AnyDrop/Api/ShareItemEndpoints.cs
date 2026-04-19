using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AnyDrop.Api;

public static class ShareItemEndpoints
{
    public static IEndpointRouteBuilder MapShareItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/share-items").WithTags("Share Items");

        group.MapGet("/", GetRecentAsync)
            .WithName("GetRecentShareItems")
            .WithSummary("Get recent shared items");

        group.MapPost("/text", SendTextAsync)
            .WithName("SendTextShareItem")
            .WithSummary("Share a text item");

        group.MapGet("/{id:guid}/file", GetFileAsync)
            .WithName("GetShareItemFile")
            .WithSummary("Get a shared file");

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
            var item = await shareService.SendTextAsync(request.Content, request.TopicId, cancellationToken);
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

    private static bool ShouldForceAttachment(string mimeType)
    {
        return mimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
               || mimeType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)
               || mimeType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
               || mimeType.Equals("text/javascript", StringComparison.OrdinalIgnoreCase);
    }
}
