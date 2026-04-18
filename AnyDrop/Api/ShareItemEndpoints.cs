using AnyDrop.Models;
using AnyDrop.Services;

namespace AnyDrop.Api;

/// <summary>
/// Provides minimal API endpoints for share item queries and creation.
/// </summary>
public static class ShareItemEndpoints
{
    /// <summary>
    /// Maps share item minimal API endpoints.
    /// </summary>
    /// <param name="app">Endpoint route builder.</param>
    /// <returns>The same route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapShareItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/share-items").WithTags("ShareItems");

        group.MapGet("/", async (IShareService shareService, int count = 50, CancellationToken ct = default) =>
        {
            if (count is < 1 or > 200)
            {
                return Results.BadRequest(ApiEnvelope.Error("count must be between 1 and 200"));
            }

            var items = await shareService.GetRecentAsync(count, ct);
            return Results.Ok(ApiEnvelope.Success(items));
        });

        group.MapPost("/", async (CreateShareItemRequest request, IShareService shareService, CancellationToken ct) =>
        {
            if (!string.Equals(request.ContentType, "text", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(ApiEnvelope.Error("only text contentType is supported in MVP"));
            }

            if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 10_000)
            {
                return Results.BadRequest(ApiEnvelope.Error("content is required and must be ≤ 10000 characters"));
            }

            var item = await shareService.SendTextAsync(request.Content, ct);
            return Results.Created($"/api/v1/share-items/{item.Id}", ApiEnvelope.Success(item));
        });

        return app;
    }

    /// <summary>
    /// Request contract for creating a share item.
    /// </summary>
    /// <param name="ContentType">The content type name.</param>
    /// <param name="Content">The content payload.</param>
    public sealed record CreateShareItemRequest(string ContentType, string Content);

    private static class ApiEnvelope
    {
        public static object Success<T>(T data)
        {
            return new { success = true, data, error = (string?)null };
        }

        public static object Error(string error)
        {
            return new { success = false, data = (object?)null, error };
        }
    }
}
