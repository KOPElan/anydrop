using System.Security.Claims;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Shared;

namespace AnyDrop.Api;

/// <summary>
/// 分片上传端点（S2-07，CLI-05）：三步协议。
/// (1) POST /init：提交文件名、总大小、总哈希与幂等键，校验 500MB 上限后返回
///     uploadId、分片大小与已确认分片清单；同一幂等键重复初始化返回同一会话。
/// (2) PUT /{uploadId}/chunks/{index}：按 uploadId + Content-Range 传片，逐片确认，
///     重复分片直接返回已确认、不重复落盘。
/// (3) POST /{uploadId}/complete：分片齐全后合并，核对总大小与总哈希，通过才落盘落库。
/// GET /{uploadId} 供断点续传查询；DELETE /{uploadId} 取消并丢弃分片。
/// </summary>
public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/uploads").WithTags("Uploads").RequireAuthorization();

        group.MapPost("/init", InitUploadAsync)
            .WithName("InitChunkedUpload")
            .WithSummary("初始化分片上传会话");

        group.MapGet("/{uploadId:guid}", GetUploadStatusAsync)
            .WithName("GetChunkedUploadStatus")
            .WithSummary("查询分片上传会话状态与已确认分片");

        group.MapPut("/{uploadId:guid}/chunks/{index:int}", UploadChunkAsync)
            .WithName("UploadChunk")
            .WithSummary("上传单个分片（Content-Range）")
            .DisableAntiforgery();

        group.MapPost("/{uploadId:guid}/complete", CompleteUploadAsync)
            .WithName("CompleteChunkedUpload")
            .WithSummary("完成分片上传：合并并校验后落库");

        group.MapDelete("/{uploadId:guid}", CancelUploadAsync)
            .WithName("CancelChunkedUpload")
            .WithSummary("取消分片上传并丢弃分片");

        return app;
    }

    private static async Task<IResult> InitUploadAsync(
        InitUploadRequest request,
        HttpContext httpContext,
        ChunkedUploadService uploadService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var (response, created) = await uploadService.InitAsync(userId.Value, request, ct);
            return created
                ? Results.Created($"/api/v1/uploads/{response.UploadId}", ApiEnvelope<InitUploadResponse>.Ok(response))
                : Results.Ok(ApiEnvelope<InitUploadResponse>.Ok(response));
        }
        catch (ChunkedUploadException ex)
        {
            return Error(ex);
        }
    }

    private static async Task<IResult> GetUploadStatusAsync(
        Guid uploadId,
        HttpContext httpContext,
        ChunkedUploadService uploadService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var status = await uploadService.GetStatusAsync(userId.Value, uploadId, ct);
        return status is null
            ? Results.NotFound(ApiEnvelope<UploadStatusResponse>.Fail("上传会话不存在。"))
            : Results.Ok(ApiEnvelope<UploadStatusResponse>.Ok(status));
    }

    private static async Task<IResult> UploadChunkAsync(
        Guid uploadId,
        int index,
        HttpContext httpContext,
        ChunkedUploadService uploadService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var contentRange = httpContext.Request.Headers["Content-Range"].FirstOrDefault();
            var response = await uploadService.UploadChunkAsync(
                userId.Value, uploadId, index, contentRange, httpContext.Request.Body, ct);
            return Results.Ok(ApiEnvelope<ChunkUploadResponse>.Ok(response));
        }
        catch (ChunkedUploadException ex)
        {
            return Error(ex);
        }
    }

    private static async Task<IResult> CompleteUploadAsync(
        Guid uploadId,
        CompleteUploadRequest? request,
        HttpContext httpContext,
        ChunkedUploadService uploadService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var dto = await uploadService.CompleteAsync(userId.Value, uploadId, request?.TotalSha256, ct);
            return Results.Ok(ApiEnvelope<ShareItemDto>.Ok(dto));
        }
        catch (ChunkedUploadException ex)
        {
            return Error(ex);
        }
    }

    private static async Task<IResult> CancelUploadAsync(
        Guid uploadId,
        HttpContext httpContext,
        ChunkedUploadService uploadService,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext.User);
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            await uploadService.CancelAsync(userId.Value, uploadId, ct);
            return Results.NoContent();
        }
        catch (ChunkedUploadException ex)
        {
            return Error(ex);
        }
    }

    private static IResult Unauthorized() =>
        Results.Json(
            ApiEnvelope<object>.Fail("未授权。"),
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Error(ChunkedUploadException ex) =>
        Results.Json(
            ApiEnvelope<object>.Fail(ex.Message),
            statusCode: ex.StatusCode);

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var userId) ? userId : null;
    }
}
