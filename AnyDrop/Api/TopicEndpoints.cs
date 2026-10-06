using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Shared;

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
        group.MapGet("/{id:guid}", GetTopicByIdAsync);
        group.MapGet("/{id:guid}/messages", GetTopicMessagesAsync);
        group.MapGet("/{id:guid}/messages/search", SearchTopicMessagesAsync);
        group.MapGet("/{id:guid}/messages/by-date", GetTopicMessagesByDateAsync);
        group.MapGet("/{id:guid}/active-dates", GetTopicActiveDatesAsync);
        group.MapGet("/{id:guid}/messages/by-type", GetTopicMessagesByTypeAsync);
        group.MapPut("/{id:guid}", UpdateTopicAsync);
        group.MapPut("/{id:guid}/pin", PinTopicAsync);
        group.MapPut("/{id:guid}/archive", ArchiveTopicAsync);
        group.MapPut("/{id:guid}/icon", UpdateTopicIconAsync);
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

    private static async Task<IResult> UpdateTopicIconAsync(
        Guid id,
        UpdateTopicIconRequest request,
        ITopicService topicService,
        CancellationToken ct)
    {
        var updated = await topicService.UpdateTopicIconAsync(id, request, ct);
        return updated is null
            ? Results.NotFound(ApiEnvelope<TopicDto>.Fail("主题不存在"))
            : Results.Ok(ApiEnvelope<TopicDto>.Ok(updated));
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

    private static async Task<IResult> GetTopicByIdAsync(Guid id, ITopicService topicService, CancellationToken ct)
    {
        var topic = await topicService.GetTopicByIdAsync(id, ct);
        return topic is null
            ? Results.NotFound(ApiEnvelope<TopicDto>.Fail("主题不存在"))
            : Results.Ok(ApiEnvelope<TopicDto>.Ok(topic));
    }

    private static async Task<IResult> SearchTopicMessagesAsync(
        Guid id,
        string q,
        int? limit,
        DateTimeOffset? before,
        IShareService shareService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Results.BadRequest(ApiEnvelope<TopicMessagesResponse>.Fail("搜索关键词不能为空"));
        }

        var result = await shareService.SearchTopicMessagesAsync(id, q, limit ?? 50, before, ct);
        return Results.Ok(ApiEnvelope<TopicMessagesResponse>.Ok(result));
    }

    private static async Task<IResult> GetTopicMessagesByDateAsync(
        Guid id,
        DateOnly date,
        string? timeZone,
        IShareService shareService,
        CancellationToken ct)
    {
        if (!TryResolveTimeZone(timeZone, out var resolved, out var error))
        {
            return Results.BadRequest(ApiEnvelope<IReadOnlyList<ShareItemDto>>.Fail(error));
        }

        var messages = await shareService.GetTopicMessagesByDateAsync(id, date, resolved, ct);
        return Results.Ok(ApiEnvelope<IReadOnlyList<ShareItemDto>>.Ok(messages));
    }

    private static async Task<IResult> GetTopicActiveDatesAsync(
        Guid id,
        DateOnly start,
        DateOnly end,
        string? timeZone,
        IShareService shareService,
        CancellationToken ct)
    {
        if (end < start)
        {
            return Results.BadRequest(ApiEnvelope<IReadOnlyCollection<DateOnly>>.Fail("end 不能早于 start"));
        }

        if (!TryResolveTimeZone(timeZone, out var resolved, out var error))
        {
            return Results.BadRequest(ApiEnvelope<IReadOnlyCollection<DateOnly>>.Fail(error));
        }

        var dates = await shareService.GetTopicActiveDatesAsync(id, start, end, resolved, ct);
        return Results.Ok(ApiEnvelope<IReadOnlyCollection<DateOnly>>.Ok(dates));
    }

    /// <summary>
    /// 解析调用方传入的时区标识（IANA，如 <c>Asia/Shanghai</c>）。
    ///
    /// 日期类查询必须由客户端指定时区：服务端的时区与用户看到的时区不一致时，
    /// 「某一天有哪些消息」会整体错位。缺省按 UTC 处理以保持行为可预期。
    /// </summary>
    internal static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo timeZone, out string error)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = TimeZoneInfo.Utc;
            error = string.Empty;
            return true;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            error = $"无法识别的时区标识：{timeZoneId}";
            return false;
        }
    }

    private static async Task<IResult> GetTopicMessagesByTypeAsync(
        Guid id,
        ShareContentType contentType,
        int? limit,
        DateTimeOffset? before,
        IShareService shareService,
        CancellationToken ct)
    {
        var result = await shareService.GetTopicMessagesByTypeAsync(id, contentType, limit ?? 50, before, ct);
        return Results.Ok(ApiEnvelope<TopicMessagesResponse>.Ok(result));
    }
}
