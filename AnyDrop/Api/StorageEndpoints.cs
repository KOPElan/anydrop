using AnyDrop.Services;
using AnyDrop.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AnyDrop.Api;

/// <summary>
/// 存储健康端点（S1-01，DAT-02）：缺失引用修复入口。
/// </summary>
public static class StorageEndpoints
{
    public static IEndpointRouteBuilder MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/storage").WithTags("Storage").RequireAuthorization();

        group.MapGet("/missing-files", ListMissingFilesAsync)
            .WithName("ListMissingFiles")
            .WithSummary("列出未解决的缺失引用记录（数据库引用、磁盘上不存在的文件）");

        group.MapPost("/missing-files/{id:guid}/resolve", ResolveMissingFileAsync)
            .WithName("ResolveMissingFile")
            .WithSummary("处理一条缺失引用记录：delete-message 删除对应消息，或 dismiss 标记已处理");

        return app;
    }

    public static async Task<IResult> ListMissingFilesAsync(
        StorageHealthService storageHealthService,
        CancellationToken ct)
    {
        var reports = await storageHealthService.ListMissingFileReportsAsync(ct);
        return TypedResults.Ok(ApiEnvelope<IReadOnlyList<MissingFileReportDto>>.Ok(reports));
    }

    public static async Task<IResult> ResolveMissingFileAsync(
        Guid id,
        [FromBody] ResolveMissingFileRequest request,
        StorageHealthService storageHealthService,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Action))
        {
            return TypedResults.BadRequest(ApiEnvelope<bool>.Fail("必须指定处理方式 action。"));
        }

        try
        {
            var resolved = await storageHealthService.ResolveMissingFileAsync(id, request.Action.Trim(), ct);
            if (!resolved)
            {
                return TypedResults.NotFound(ApiEnvelope<bool>.Fail("记录不存在。"));
            }

            return TypedResults.Ok(ApiEnvelope<bool>.Ok(true));
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(ApiEnvelope<bool>.Fail(ex.Message));
        }
    }
}
