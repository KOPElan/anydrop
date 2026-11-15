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
        IDbContextFactory<AnyDropDbContext> dbContextFactory,
        IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        // S1-03：上下文由工厂现建现用，请求结束即释放。
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var item = await dbContext.ShareItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return TypedResults.NotFound(ApiEnvelope<object>.Fail("消息不存在"));
        }

        // S0-06：已到期的定时销毁消息不可下载，不等清理任务。
        if (item.ExpiresAt is not null && item.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return TypedResults.NotFound(ApiEnvelope<object>.Fail("消息已过期"));
        }

        if (item.ContentType is not (ShareContentType.File or ShareContentType.Image or ShareContentType.Video))
        {
            return TypedResults.BadRequest(ApiEnvelope<object>.Fail("该消息不包含文件"));
        }

        try
        {
            var stream = await fileStorageService.GetFileAsync(item.Content, cancellationToken);
            // MIME 先规范化再比较：上传方控制的 Content-Type 可能带参数或尾空格。
            var contentType = MimeTypeHelper.Normalize(item.MimeType);
            var fileName = item.FileName ?? "download.bin";
            var asAttachment = download == true || !CanServeInline(contentType);
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
    /// 手动清理指定月数前的消息：只写延迟删除标记（7 天后物理删除），消息立即不可见。
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
    /// 批量删除指定 ID 的消息：先提交数据库删除，再删文件。
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
        IDbContextFactory<AnyDropDbContext> dbContextFactory,
        IFileStorageService fileStorageService,
        CancellationToken cancellationToken)
    {
        // S1-03：上下文由工厂现建现用，请求结束即释放。
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var item = await dbContext.ShareItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.ThumbnailPath, x.ExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null || string.IsNullOrWhiteSpace(item.ThumbnailPath))
        {
            return TypedResults.NotFound(ApiEnvelope<object>.Fail("缩略图尚未生成"));
        }

        // S0-06：已到期的定时销毁消息的缩略图也不可访问。
        if (item.ExpiresAt is not null && item.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return TypedResults.NotFound(ApiEnvelope<object>.Fail("消息已过期"));
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

    /// <summary>
    /// 该类型是否可以安全地内联展示。
    ///
    /// 采用白名单而非黑名单：黑名单只能挡住已知的危险类型，会漏掉
    /// <c>application/xhtml+xml</c> 这类同样能执行脚本的类型。
    /// 现在只有图片、视频、音频内联，且排除 SVG（SVG 可以内嵌脚本与外部引用）；
    /// 其余内容一律作为附件下载，并由 <c>X-Content-Type-Options: nosniff</c> 兜底。
    ///
    /// 调用方必须先对 MIME 做规范化（见 <see cref="MimeTypeHelper"/>）：
    /// <c>image/svg+xml; charset=utf-8</c> 或带尾空格的变体未经规范化会绕过
    /// 精确匹配、落入 <c>image/</c> 内联分支。
    /// </summary>
    internal static bool CanServeInline(string mimeType)
    {
        var normalized = MimeTypeHelper.Normalize(mimeType);
        if (MimeTypeHelper.IsSvg(normalized))
        {
            return false;
        }

        return normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
    }
}
