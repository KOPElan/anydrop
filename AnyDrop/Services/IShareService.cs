using AnyDrop.Models;

namespace AnyDrop.Services;

/// <summary>
/// Defines operations for share item workflows.
/// </summary>
public interface IShareService
{
    /// <summary>
    /// Sends a text payload to be persisted and broadcast.
    /// </summary>
    /// <param name="content">The text content to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created share item DTO.</returns>
    Task<ShareItemDto> SendTextAsync(string content, CancellationToken ct = default);

    /// <summary>
    /// Gets recent share items ordered by creation time ascending.
    /// </summary>
    /// <param name="count">Maximum number of records to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recent share item list.</returns>
    Task<IReadOnlyList<ShareItemDto>> GetRecentAsync(int count = 50, CancellationToken ct = default);
}
