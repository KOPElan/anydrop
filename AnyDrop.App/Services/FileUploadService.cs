using System.Net.Http.Json;
using AnyDrop.App.Models;

namespace AnyDrop.App.Services;

public sealed class FileUploadService : IFileUploadService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public FileUploadService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ShareItemDto> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid topicId,
        IProgress<double>? progress = null)
    {
        if (fileStream is null) throw new ArgumentNullException(nameof(fileStream));
        if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("fileName required", nameof(fileName));
        if (string.IsNullOrEmpty(mimeType)) throw new ArgumentException("mimeType required", nameof(mimeType));

        var client = _httpClientFactory.CreateClient("api");

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        content.Add(streamContent, "file", fileName);
        content.Add(new StringContent(topicId.ToString()), "topicId");

        var httpResponse = await client.PostAsync("api/v1/files", content).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<ApiResponse<ShareItemDto>>().ConfigureAwait(false);
        progress?.Report(1.0);
        return response!.Data!;
    }
}
