using AnyDrop.Models;
using AnyDrop.Services;
using Microsoft.AspNetCore.Http.HttpResults;

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
}
