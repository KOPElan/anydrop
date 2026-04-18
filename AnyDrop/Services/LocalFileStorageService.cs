namespace AnyDrop.Services;

/// <summary>
/// Provides a local file storage placeholder implementation for MVP.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    /// <summary>
    /// Saves a file stream into storage.
    /// </summary>
    /// <param name="content">The source content stream.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="mimeType">Content mime type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that is not supported in MVP.</returns>
    public Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default)
    {
        throw new NotSupportedException("File upload is not implemented in MVP.");
    }

    /// <summary>
    /// Opens a file stream from storage.
    /// </summary>
    /// <param name="storagePath">Persisted storage path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that is not supported in MVP.</returns>
    public Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default)
    {
        throw new NotSupportedException("File retrieval is not implemented in MVP.");
    }

    /// <summary>
    /// Deletes a file from storage.
    /// </summary>
    /// <param name="storagePath">Persisted storage path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that is not supported in MVP.</returns>
    public Task DeleteFileAsync(string storagePath, CancellationToken ct = default)
    {
        throw new NotSupportedException("File deletion is not implemented in MVP.");
    }
}
