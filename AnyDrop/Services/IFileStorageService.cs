namespace AnyDrop.Services;

/// <summary>
/// Defines file storage operations for future file-sharing support.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Saves a file stream into storage.
    /// </summary>
    /// <param name="content">The source content stream.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="mimeType">Content mime type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted storage path.</returns>
    Task<string> SaveFileAsync(Stream content, string fileName, string mimeType, CancellationToken ct = default);

    /// <summary>
    /// Opens a file stream from storage.
    /// </summary>
    /// <param name="storagePath">Persisted storage path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The opened file stream.</returns>
    Task<Stream> GetFileAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file from storage.
    /// </summary>
    /// <param name="storagePath">Persisted storage path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteFileAsync(string storagePath, CancellationToken ct = default);
}
