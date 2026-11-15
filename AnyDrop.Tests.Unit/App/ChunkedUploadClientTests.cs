using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AnyDrop.App.Models;
using AnyDrop.App.Services;
using AnyDrop.Shared;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace AnyDrop.Tests.Unit.App;

/// <summary>S2-07：客户端分片上传——按 Content-Range 逐片、断点续传只补未确认片、取消通知服务端、单片重试。</summary>
public class ChunkedUploadClientTests
{
    private const int ChunkSize = UploadLimits.DefaultChunkSizeBytes; // 4MB

    private sealed class FakeChunkedServerHandler : HttpMessageHandler
    {
        private sealed class Session
        {
            public required Guid UploadId { get; init; }
            public required long TotalSize { get; init; }
            public HashSet<int> Confirmed { get; } = [];
            public Dictionary<int, byte[]> Chunks { get; } = [];
        }

        private readonly Dictionary<string, Session> _sessionsByKey = [];
        private readonly Dictionary<Guid, Session> _sessionsById = [];

        public HttpStatusCode? FailInitWith { get; set; }
        public string? FailInitError { get; set; }
        public int InitCallCount { get; private set; }
        public int ChunkPutCount { get; private set; }
        public List<string> ChunkContentRanges { get; } = [];
        public int ChunkFailuresBeforeSuccess { get; set; }
        public HttpStatusCode? FailChunkWith { get; set; }
        /// <summary>仅让指定序号的分片失败（null 表示全部失败），用于模拟中途断网。</summary>
        public int? FailChunkIndex { get; set; }
        public string? LastCompleteHash { get; private set; }
        public bool CancelDeleteCalled { get; private set; }
        public int CompleteCallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post && path == "/api/v1/uploads/init")
            {
                InitCallCount++;
                if (FailInitWith.HasValue)
                {
                    return FailEnvelope(FailInitWith.Value, FailInitError ?? "init failed");
                }

                var init = await request.Content!.ReadFromJsonAsync<InitUploadRequest>(ct);
                if (_sessionsByKey.TryGetValue(init!.IdempotencyKey, out var existing))
                {
                    return Ok(new InitUploadResponse(
                        existing.UploadId, ChunkSize, existing.Confirmed.OrderBy(i => i).ToList(),
                        DateTimeOffset.UtcNow.AddHours(24)));
                }

                var session = new Session { UploadId = Guid.NewGuid(), TotalSize = init.TotalSize };
                _sessionsByKey[init.IdempotencyKey] = session;
                _sessionsById[session.UploadId] = session;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(ApiEnvelope<InitUploadResponse>.Ok(
                        new InitUploadResponse(session.UploadId, ChunkSize, [], DateTimeOffset.UtcNow.AddHours(24))))
                };
            }

            if (request.Method == HttpMethod.Put && path.Contains("/chunks/"))
            {
                ChunkPutCount++;
                var segments = path.Split('/');
                var uploadId = Guid.Parse(segments[4]);
                var index = int.Parse(segments[6]);
                if (FailChunkWith.HasValue && (FailChunkIndex is null || FailChunkIndex == index))
                {
                    return FailEnvelope(FailChunkWith.Value, "chunk rejected");
                }

                if (ChunkFailuresBeforeSuccess > 0)
                {
                    ChunkFailuresBeforeSuccess--;
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                var session = _sessionsById[uploadId];
                // Content-Range 是内容头，不在 request.Headers 里，在 request.Content.Headers 里。
                var range = request.Content?.Headers.TryGetValues("Content-Range", out var values) == true
                    ? values.First()
                    : string.Empty;
                ChunkContentRanges.Add(range);
                var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
                session.Chunks[index] = bytes;
                session.Confirmed.Add(index);
                return Ok(new ChunkUploadResponse(index, bytes.Length, session.Confirmed.OrderBy(i => i).ToList()));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/complete"))
            {
                CompleteCallCount++;
                var segments = path.Split('/');
                var uploadId = Guid.Parse(segments[4]);
                var session = _sessionsById[uploadId];
                var complete = await request.Content!.ReadFromJsonAsync<CompleteUploadRequest>(ct);
                LastCompleteHash = complete?.TotalSha256;

                using var ms = new MemoryStream();
                foreach (var index in session.Confirmed.OrderBy(i => i))
                {
                    ms.Write(session.Chunks[index]);
                }

                var actualHash = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
                if (LastCompleteHash is not null && LastCompleteHash != actualHash)
                {
                    return FailEnvelope(HttpStatusCode.BadRequest, "hash mismatch");
                }

                var item = new ShareItemDto(
                    Guid.NewGuid(), ShareContentType.File, "big.bin", "big.bin", session.TotalSize,
                    "application/octet-stream", null, null, DateTimeOffset.UtcNow, null, Guid.NewGuid());
                return Ok(item);
            }

            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/v1/uploads/"))
            {
                CancelDeleteCalled = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Ok<T>(T data) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(ApiEnvelope<T>.Ok(data)) };

        private static HttpResponseMessage FailEnvelope(HttpStatusCode status, string error) =>
            new(status) { Content = JsonContent.Create(ApiEnvelope<object?>.Fail(error)) };
    }

    private static byte[] MakeContent(long size)
    {
        var bytes = new byte[size];
        for (long i = 0; i < size; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    private static (FileUploadService Sut, FakeChunkedServerHandler Handler) CreateSut(
        FakeChunkedServerHandler? handler = null)
    {
        handler ??= new FakeChunkedServerHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("api")).Returns(client);
        return (new FileUploadService(factoryMock.Object), handler);
    }

    [Fact]
    public async Task UploadFileAsync_LargeFile_UploadsAllChunksWithCorrectRangesAndHash()
    {
        var totalSize = ChunkSize * 6L + 1 * 1024 * 1024; // 25MB，7 片
        var content = MakeContent(totalSize);
        var expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var (sut, handler) = CreateSut();
        var progress = new List<double>();
        var progressReporter = new Progress<double>(v => progress.Add(v));

        var result = await sut.UploadFileAsync(
            new MemoryStream(content), "big.bin", "application/octet-stream",
            Guid.NewGuid(), false, progressReporter);

        result.FileSize.Should().Be(totalSize);
        handler.InitCallCount.Should().Be(1);
        handler.ChunkPutCount.Should().Be(7);
        handler.ChunkContentRanges.Should().HaveCount(7);
        handler.ChunkContentRanges[0].Should().Be($"bytes 0-{ChunkSize - 1}/{totalSize}");
        handler.ChunkContentRanges[6].Should().Be($"bytes {ChunkSize * 6}-{totalSize - 1}/{totalSize}");
        handler.LastCompleteHash.Should().Be(expectedHash);
        progress.Should().NotBeEmpty();
        progress[^1].Should().Be(1.0);
        progress.Should().BeInAscendingOrder();
    }

    /// <summary>
    /// 断点续传：第一轮传 2 片后第 3 片持续 500（模拟断网），客户端重试耗尽后抛错；
    /// 第二轮用新 FileUploadService 实例（模拟杀进程重启）上传同一文件，
    /// 幂等键相同，服务端返回已确认 [0,1]，只补传剩余分片，最终恰好完成一次、哈希一致。
    /// </summary>
    [Fact]
    public async Task UploadFileAsync_ResumeAfterFailure_OnlyUploadsMissingChunks()
    {
        var totalSize = ChunkSize * 5L + 100; // 6 片（20MB+100 字节，触发分片）
        var content = MakeContent(totalSize);
        var expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var tempFile = Path.Combine(Path.GetTempPath(), $"anydrop-resume-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempFile, content);
        try
        {
            var handler = new FakeChunkedServerHandler
            {
                FailChunkWith = HttpStatusCode.InternalServerError,
                FailChunkIndex = 2
            };
            var (firstSut, _) = CreateSut(handler);

            await using (var stream = File.OpenRead(tempFile))
            {
                var act = () => firstSut.UploadFileAsync(
                    stream, "resume.bin", "application/octet-stream", Guid.NewGuid());
                await act.Should().ThrowAsync<HttpRequestException>();
            }

            // 第一轮：第 0、1 片成功，第 2 片 1+3 次重试均失败；complete 从未调用
            handler.ChunkPutCount.Should().Be(2 + 4);
            handler.CompleteCallCount.Should().Be(0);

            // “重启”：故障清除，新实例上传同一文件
            handler.FailChunkWith = null;
            handler.FailChunkIndex = null;
            var putsBeforeSecondRound = handler.ChunkPutCount;
            var (secondSut, _) = CreateSut(handler);

            ShareItemDto result;
            await using (var stream = File.OpenRead(tempFile))
            {
                result = await secondSut.UploadFileAsync(
                    stream, "resume.bin", "application/octet-stream", Guid.NewGuid());
            }

            // 第二轮只补传 4 片（2、3、4、5），不再重传 0、1
            (handler.ChunkPutCount - putsBeforeSecondRound).Should().Be(4);
            handler.CompleteCallCount.Should().Be(1);
            handler.LastCompleteHash.Should().Be(expectedHash);
            result.FileSize.Should().Be(totalSize);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UploadFileAsync_ChunkFailsTwiceThenSucceeds_RetriesOnlyThatChunk()
    {
        var totalSize = ChunkSize * 5L + 10; // 6 片（20MB+10 字节，触发分片）
        var content = MakeContent(totalSize);
        var handler = new FakeChunkedServerHandler { ChunkFailuresBeforeSuccess = 2 };
        var (sut, _) = CreateSut(handler);

        var result = await sut.UploadFileAsync(
            new MemoryStream(content), "big.bin", "application/octet-stream", Guid.NewGuid());

        result.FileSize.Should().Be(totalSize);
        // 第 0 片失败 2 次后成功（3 次 PUT），其余 5 片各 1 次
        handler.ChunkPutCount.Should().Be(8);
    }

    [Fact]
    public async Task UploadFileAsync_ChunkRejectedWith400_DoesNotRetry()
    {
        var totalSize = ChunkSize * 5L + 10; // 6 片（20MB+10 字节，触发分片）
        var content = MakeContent(totalSize);
        var handler = new FakeChunkedServerHandler { FailChunkWith = HttpStatusCode.BadRequest };
        var (sut, _) = CreateSut(handler);

        var act = () => sut.UploadFileAsync(
            new MemoryStream(content), "big.bin", "application/octet-stream", Guid.NewGuid());

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*chunk rejected*");
        handler.ChunkPutCount.Should().Be(1);
    }

    [Fact]
    public async Task UploadFileAsync_InitRejectedWith413_ThrowsWithServerMessage()
    {
        var totalSize = ChunkSize * 5L + 10; // 6 片（20MB+10 字节，触发分片）
        var content = MakeContent(totalSize);
        var handler = new FakeChunkedServerHandler
        {
            FailInitWith = HttpStatusCode.RequestEntityTooLarge,
            FailInitError = "文件大小超出上传上限。"
        };
        var (sut, _) = CreateSut(handler);

        var act = () => sut.UploadFileAsync(
            new MemoryStream(content), "big.bin", "application/octet-stream", Guid.NewGuid());

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*超出上传上限*");
    }

    [Fact]
    public async Task UploadFileAsync_Cancelled_NotifiesServerToDiscard()
    {
        var totalSize = ChunkSize * 5L; // 恰好 20MB，触发分片
        var content = MakeContent(totalSize);
        var (sut, handler) = CreateSut();
        using var cts = new CancellationTokenSource();
        // 第一片确认后（进度回调）再取消：此时会话已建好，取消路径应发 DELETE 通知服务端丢弃。
        var progress = new Progress<double>(v =>
        {
            if (v > 0)
            {
                cts.Cancel();
            }
        });

        var act = () => sut.UploadFileAsync(
            new MemoryStream(content), "big.bin", "application/octet-stream",
            Guid.NewGuid(), false, progress, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.CancelDeleteCalled.Should().BeTrue();
    }

    [Fact]
    public void ComputeIdempotencyKey_SameFileIdentity_ReturnsSameKey()
    {
        var mtime = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

        var first = FileUploadService.ComputeIdempotencyKey("a.mp4", 12345, mtime);
        var second = FileUploadService.ComputeIdempotencyKey("a.mp4", 12345, mtime);

        first.Should().Be(second);
        FileUploadService.ComputeIdempotencyKey("a.mp4", 12345, mtime.AddSeconds(1))
            .Should().NotBe(first);
        FileUploadService.ComputeIdempotencyKey("b.mp4", 12345, mtime)
            .Should().NotBe(first);
    }
}
