using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AnyDrop.App.Models;
using AnyDrop.Shared;

namespace AnyDrop.App.Services;

/// <summary>
/// 文件上传（S2-07，CLI-05）：小于 20MB 走单次 multipart（小文件兜底）；
/// 达到 20MB 一律走分片上传：初始化（幂等键）→ 按 Content-Range 逐片上传
/// （单片失败只重试该片，3 次退避）→ 完成时提交整文件 SHA-256 校验。
/// 断线或杀进程后凭同一幂等键恢复会话，只补未确认分片；进度按已确认字节数算。
/// </summary>
public sealed class FileUploadService : IFileUploadService
{
    /// <summary>单片失败重试次数（不含首次尝试）。</summary>
    internal const int MaxChunkRetries = 3;

    private static readonly TimeSpan[] ChunkRetryDelays =
        [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    /// <summary>单片请求超时：30 秒（全局 HttpClient 超时同样是 30 秒，此处显式约束单片）。</summary>
    internal static readonly TimeSpan ChunkTimeout = TimeSpan.FromSeconds(30);

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
        bool burnAfterReading = false,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (fileStream is null) throw new ArgumentNullException(nameof(fileStream));
        if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("fileName required", nameof(fileName));
        if (string.IsNullOrEmpty(mimeType)) throw new ArgumentException("mimeType required", nameof(mimeType));

        // 只有可 Seek 的流才能知道总大小、按片定位；不可 Seek 的流（如某些 picker 流）
        // 走单次上传兜底（服务端仍按 500MB 上限校验）。
        if (fileStream.CanSeek && fileStream.Length >= UploadLimits.SingleUploadMaxBytes)
        {
            return await UploadChunkedAsync(fileStream, fileName, mimeType, topicId, burnAfterReading, progress, ct)
                .ConfigureAwait(false);
        }

        return await UploadSingleAsync(fileStream, fileName, mimeType, topicId, burnAfterReading, progress, ct)
            .ConfigureAwait(false);
    }

    private async Task<ShareItemDto> UploadSingleAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid topicId,
        bool burnAfterReading,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("api");

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        content.Add(streamContent, "file", fileName);
        content.Add(new StringContent(topicId.ToString()), "topicId");
        content.Add(new StringContent(burnAfterReading ? "true" : "false"), "burnAfterReading");

        var httpResponse = await client.PostAsync("api/v1/files", content, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(httpResponse).ConfigureAwait(false);
        var response = await httpResponse.Content.ReadFromJsonAsync<ApiEnvelope<ShareItemDto>>(cancellationToken: ct)
            .ConfigureAwait(false);
        progress?.Report(1.0);
        return response!.Data!;
    }

    private async Task<ShareItemDto> UploadChunkedAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        Guid topicId,
        bool burnAfterReading,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("api");
        var totalSize = fileStream.Length;
        var idempotencyKey = ComputeIdempotencyKey(fileName, totalSize, GetLastWriteTimeUtc(fileStream));

        // (1) 初始化：同一幂等键重复初始化返回同一 uploadId 与已确认分片清单。
        var initRequest = new InitUploadRequest(fileName, totalSize, null, idempotencyKey, topicId, burnAfterReading, mimeType);
        var initResponse = await client.PostAsJsonAsync("api/v1/uploads/init", initRequest, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(initResponse).ConfigureAwait(false);
        var init = (await initResponse.Content.ReadFromJsonAsync<ApiEnvelope<InitUploadResponse>>(cancellationToken: ct)
            .ConfigureAwait(false))!.Data!;
        var uploadId = init.UploadId;
        var chunkSize = init.ChunkSize;
        var confirmed = new HashSet<int>(init.ConfirmedChunks);

        // 增量计算整文件 SHA-256：已确认分片只本地重读、不重传，仍计入哈希；
        // 这样断点续传后完成时仍能全量校验，无需为哈希多读一遍文件。
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var confirmedBytes = 0L;
        var chunkCount = (int)((totalSize + chunkSize - 1) / chunkSize);

        try
        {
            // 已确认分片：本地重读计入哈希并计进度，不发网络请求。
            foreach (var index in confirmed.OrderBy(i => i))
            {
                ct.ThrowIfCancellationRequested();
                var size = ChunkLength(index, chunkSize, totalSize);
                await HashChunkAsync(fileStream, index, chunkSize, size, hash, ct).ConfigureAwait(false);
                confirmedBytes += size;
            }

            progress?.Report(totalSize == 0 ? 1.0 : (double)confirmedBytes / totalSize);

            // (2) 逐片上传未确认分片，单片失败只重试该片。
            for (var index = 0; index < chunkCount; index++)
            {
                if (confirmed.Contains(index))
                {
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                var size = ChunkLength(index, chunkSize, totalSize);
                var bytes = await ReadChunkAsync(fileStream, index, chunkSize, size, ct).ConfigureAwait(false);
                hash.AppendData(bytes);

                await UploadChunkWithRetryAsync(client, uploadId, index, chunkSize, totalSize, bytes, ct)
                    .ConfigureAwait(false);

                confirmed.Add(index);
                confirmedBytes += size;
                progress?.Report((double)confirmedBytes / totalSize);
            }

            // (3) 完成：提交增量算出的整文件 SHA-256，服务端合并后校验，通过才落库。
            var totalSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var completeResponse = await client.PostAsJsonAsync(
                    $"api/v1/uploads/{uploadId}/complete",
                    new CompleteUploadRequest(totalSha256), ct)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(completeResponse).ConfigureAwait(false);
            var completed = (await completeResponse.Content
                    .ReadFromJsonAsync<ApiEnvelope<ShareItemDto>>(cancellationToken: ct)
                    .ConfigureAwait(false))!.Data!;
            progress?.Report(1.0);
            return completed;
        }
        catch (OperationCanceledException)
        {
            // 取消后通知服务端丢弃该会话的分片（尽力而为，不阻塞取消流程）。
            await CancelUploadQuietlyAsync(client, uploadId).ConfigureAwait(false);
            throw;
        }
    }

    private async Task UploadChunkWithRetryAsync(
        HttpClient client, Guid uploadId, int index, int chunkSize, long totalSize,
        byte[] bytes, CancellationToken ct)
    {
        var start = (long)index * chunkSize;
        var end = start + bytes.Length - 1;

        Exception? lastError = null;
        for (var attempt = 0; attempt <= MaxChunkRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt > 0)
            {
                await Task.Delay(ChunkRetryDelays[attempt - 1], ct).ConfigureAwait(false);
            }

            try
            {
                using var chunkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                chunkCts.CancelAfter(ChunkTimeout);
                using var content = new ByteArrayContent(bytes);
                content.Headers.Add("Content-Range", $"bytes {start}-{end}/{totalSize}");
                var response = await client.PutAsync(
                        $"api/v1/uploads/{uploadId}/chunks/{index}", content, chunkCts.Token)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                // 4xx（除 408/429）是永久性错误，重试无意义：直接抛，不进入重试循环。
                var status = (int)response.StatusCode;
                if (status is >= 400 and < 500 and not 408 and not 429)
                {
                    var serverError = await ReadServerErrorAsync(response).ConfigureAwait(false);
                    throw new NonRetryableException(
                        serverError ?? $"上传分片 {index} 被拒绝：{(int)response.StatusCode} {response.ReasonPhrase}",
                        response.StatusCode);
                }

                lastError = new HttpRequestException(
                    $"上传分片 {index} 失败：{(int)response.StatusCode} {response.ReasonPhrase}");
            }
            catch (NonRetryableException)
            {
                throw;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 单片 30 秒超时（非外层取消）：按网络失败重试。
                lastError = new TimeoutException($"上传分片 {index} 超时（30 秒）。");
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new HttpRequestException($"上传分片 {index} 失败。");
    }

    private static async Task<byte[]> ReadChunkAsync(
        Stream stream, int index, int chunkSize, long length, CancellationToken ct)
    {
        stream.Seek((long)index * chunkSize, SeekOrigin.Begin);
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, (int)length - read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                throw new IOException($"读取分片 {index} 时流提前结束：期望 {length} 字节，实际 {read} 字节。");
            }

            read += n;
        }

        return buffer;
    }

    private static async Task HashChunkAsync(
        Stream stream, int index, int chunkSize, long length,
        IncrementalHash hash, CancellationToken ct)
    {
        var bytes = await ReadChunkAsync(stream, index, chunkSize, length, ct).ConfigureAwait(false);
        hash.AppendData(bytes);
    }

    private static long ChunkLength(int index, int chunkSize, long totalSize)
    {
        var start = (long)index * chunkSize;
        return Math.Min(chunkSize, totalSize - start);
    }

    private static async Task CancelUploadQuietlyAsync(HttpClient client, Guid uploadId)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.DeleteAsync($"api/v1/uploads/{uploadId}", cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // 取消通知是尽力而为：会话 24 小时后由对账清扫。
        }
    }

    /// <summary>
    /// 幂等键：文件名 + 大小 + 最后修改时间（FileStream 才有）的 SHA-256。
    /// 同一文件杀进程重启后算出同一键，服务端返回同一会话、只补未确认分片；
    /// 非文件流没有修改时间，键内含随机值（同一次上传内重试仍可用，跨进程无法续传）。
    /// </summary>
    internal static string ComputeIdempotencyKey(string fileName, long length, DateTimeOffset? lastWriteTimeUtc)
    {
        var input = lastWriteTimeUtc.HasValue
            ? $"{fileName}|{length}|{lastWriteTimeUtc.Value:O}"
            : $"{fileName}|{length}|{Guid.NewGuid():N}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static DateTimeOffset? GetLastWriteTimeUtc(Stream stream)
    {
        if (stream is FileStream fileStream)
        {
            try
            {
                return File.GetLastWriteTimeUtc(fileStream.Name);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>失败时把服务端信封里的错误信息抛给调用方，而不是只给 HTTP 状态码。</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var serverError = await ReadServerErrorAsync(response).ConfigureAwait(false);

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(serverError)
                ? $"请求失败：{(int)response.StatusCode} {response.ReasonPhrase}"
                : serverError,
            null,
            response.StatusCode);
    }

    private static async Task<string?> ReadServerErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var envelope = await response.Content
                .ReadFromJsonAsync<ApiEnvelope<object?>>()
                .ConfigureAwait(false);
            return envelope?.Error;
        }
        catch
        {
            // 读不到信封时回退到状态码描述
            return null;
        }
    }

    /// <summary>服务端明确拒绝（4xx）：重试无意义，重试循环遇到它直接抛，不重试。</summary>
    private sealed class NonRetryableException(string message, System.Net.HttpStatusCode statusCode)
        : HttpRequestException(message, null, statusCode);
}
