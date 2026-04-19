using AnyDrop.Models;
using AnyDrop.Services;

namespace AnyDrop.Api;

public static class TopicEndpoints
{
    public static IEndpointRouteBuilder MapTopicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/topics").WithTags("Topics").RequireAuthorization();

        group.MapGet("/", GetAllTopicsAsync);
        group.MapGet("/archived", GetArchivedTopicsAsync);
        group.MapPost("/", CreateTopicAsync);
        group.MapPut("/reorder", ReorderTopicsAsync);
        group.MapGet("/{id:guid}/messages", GetTopicMessagesAsync);
        group.MapPut("/{id:guid}", UpdateTopicAsync);
        group.MapPut("/{id:guid}/pin", PinTopicAsync);
        group.MapPut("/{id:guid}/archive", ArchiveTopicAsync);
        group.MapDelete("/{id:guid}", DeleteTopicAsync);

        return app;
    }

    private static async Task<IResult> GetAllTopicsAsync(ITopicService topicService, CancellationToken ct)
    {
        var topics = await topicService.GetAllTopicsAsync(ct);
        return Results.Ok(ApiEnvelope<IReadOnlyList<TopicDto>>.Ok(topics));
    }

    private static async Task<IResult> GetArchivedTopicsAsync(ITopicService topicService, CancellationToken ct)
    {
        var topics = await topicService.GetArchivedTopicsAsync(ct);
        return Results.Ok(ApiEnvelope<IReadOnlyList<TopicDto>>.Ok(topics));
    }

    private static async Task<IResult> CreateTopicAsync(CreateTopicRequest request, ITopicService topicService, CancellationToken ct)
    {
        try
        {
            var topic = await topicService.CreateTopicAsync(request, ct);
            return Results.Created($"/api/v1/topics/{topic.Id}", ApiEnvelope<TopicDto>.Ok(topic));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ApiEnvelope<TopicDto>.Fail(ex.Message));
        }
    }

    private static async Task<IResult> GetTopicMessagesAsync(
        Guid id,
        int? limit,
        DateTimeOffset? before,
        ITopicService topicService,
        CancellationToken ct)
    {
        var result = await topicService.GetTopicMessagesAsync(id, limit ?? 50, before, ct);
        return result is null
            ? Results.NotFound(ApiEnvelope<TopicMessagesResponse>.Fail("主题不存在"))
            : Results.Ok(ApiEnvelope<TopicMessagesResponse>.Ok(result));
    }

    private static async Task<IResult> UpdateTopicAsync(
        Guid id,
        UpdateTopicRequest request,
        ITopicService topicService,
        CancellationToken ct)
    {
        try
        {
            var updated = await topicService.UpdateTopicAsync(id, request, ct);
            return updated is null
                ? Results.NotFound(ApiEnvelope<TopicDto>.Fail("主题不存在"))
                : Results.Ok(ApiEnvelope<TopicDto>.Ok(updated));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ApiEnvelope<TopicDto>.Fail(ex.Message));
        }
    }

    private static async Task<IResult> DeleteTopicAsync(Guid id, ITopicService topicService, CancellationToken ct)
    {
        var deleted = await topicService.DeleteTopicAsync(id, ct);
        return deleted
            ? Results.NoContent()
            : Results.NotFound(ApiEnvelope<object>.Fail("主题不存在"));
    }

    private static async Task<IResult> PinTopicAsync(
        Guid id,
        PinTopicRequest request,
        ITopicService topicService,
        CancellationToken ct)
    {
        try
        {
            var result = await topicService.PinTopicAsync(id, request.IsPinned, ct);
            return Results.Ok(ApiEnvelope<TopicDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(ApiEnvelope<TopicDto>.Fail("主题不存在"));
        }
    }

    private static async Task<IResult> ArchiveTopicAsync(
        Guid id,
        ArchiveTopicRequest request,
        ITopicService topicService,
        CancellationToken ct)
    {
        try
        {
            var result = await topicService.ArchiveTopicAsync(id, request.IsArchived, ct);
            return Results.Ok(ApiEnvelope<TopicDto>.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(ApiEnvelope<TopicDto>.Fail("主题不存在"));
        }
    }

    private static async Task<IResult> ReorderTopicsAsync(ReorderTopicsRequest request, ITopicService topicService, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(ApiEnvelope<object>.Fail("items 不能为空"));
        }

        try
        {
            await topicService.ReorderTopicsAsync(request, ct);
            return Results.Ok(ApiEnvelope<object?>.Ok(null));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ApiEnvelope<object>.Fail(ex.Message));
        }
    }
}
