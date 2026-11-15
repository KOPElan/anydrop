using System.Security.Cryptography;
using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Shared;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>S2-07：服务端分片协议——初始化幂等、逐片确认、合并校验、取消与过期清扫。</summary>
public class ChunkedUploadServiceTests : IDisposable
{
    private const int ChunkSize = UploadLimits.DefaultChunkSizeBytes; // 4MB
    private readonly string _basePath;
    private readonly FakeTimeProvider _timeProvider;

    public ChunkedUploadServiceTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"anydrop-chunked-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_basePath);
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        try { Directory.Delete(_basePath, recursive: true); } catch { /* ignore */ }
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

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private (ChunkedUploadService Service, Mock<IShareService> ShareMock, Guid UserId, TestDbContextFactory Factory) CreateService()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"chunked-tests-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = _basePath
            })
            .Build();
        var storage = new LocalFileStorageService(config, _timeProvider);
        var userId = Guid.NewGuid();

        var shareMock = new Mock<IShareService>();
        shareMock.Setup(s => s.SendFileAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Guid?>(), It.IsAny<long?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream stream, string fileName, string mimeType, Guid? topicId,
                long? knownSize, bool burn, CancellationToken ct) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return new ShareItemDto(
                    Guid.NewGuid(), ShareContentType.File, fileName, fileName, ms.Length,
                    mimeType, null, null, _timeProvider.GetUtcNow(), null, topicId);
            });

        var service = new ChunkedUploadService(factory, storage, config, shareMock.Object, _timeProvider);
        return (service, shareMock, userId, factory);
    }

    [Fact]
    public async Task InitAsync_CreatesSessionWithDefaultChunkSize()
    {
        var (service, _, userId, _) = CreateService();

        var (response, created) = await service.InitAsync(userId,
            new InitUploadRequest("big.bin", 100, null, "key-1", null, false, "application/octet-stream"));

        created.Should().BeTrue();
        response.ChunkSize.Should().Be(ChunkSize);
        response.ConfirmedChunks.Should().BeEmpty();
        response.ExpiresAt.Should().Be(_timeProvider.GetUtcNow() + UploadLimits.SessionLifetime);
    }

    [Fact]
    public async Task InitAsync_SameKeyReturnsSameSession()
    {
        var (service, _, userId, _) = CreateService();
        var (first, _) = await service.InitAsync(userId,
            new InitUploadRequest("big.bin", 100, null, "key-1", null, false, null));

        var (second, created) = await service.InitAsync(userId,
            new InitUploadRequest("big.bin", 100, null, "key-1", null, false, null));

        created.Should().BeFalse();
        second.UploadId.Should().Be(first.UploadId);
    }

    [Fact]
    public async Task InitAsync_SameKeyDifferentFile_Returns409()
    {
        var (service, _, userId, _) = CreateService();
        await service.InitAsync(userId,
            new InitUploadRequest("big.bin", 100, null, "key-1", null, false, null));

        var act = () => service.InitAsync(userId,
            new InitUploadRequest("other.bin", 100, null, "key-1", null, false, null));

        (await act.Should().ThrowAsync<ChunkedUploadException>())
            .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task InitAsync_UnknownTopic_Returns400()
    {
        var (service, _, userId, _) = CreateService();

        var act = () => service.InitAsync(userId,
            new InitUploadRequest("big.bin", 100, null, "key-1", Guid.NewGuid(), false, null));

        (await act.Should().ThrowAsync<ChunkedUploadException>())
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task InitAsync_OverLimit_Returns413()
    {
        var (service, _, userId, _) = CreateService();

        var act = () => service.InitAsync(userId,
            new InitUploadRequest("huge.bin", UploadLimits.MaxFileSizeBytes + 1, null, "key-1", null, false, null));

        (await act.Should().ThrowAsync<ChunkedUploadException>())
            .Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public async Task UploadChunkAsync_WrongContentRange_Returns400()
    {
        var (service, _, userId, _) = CreateService();
        var (init, _) = await service.InitAsync(userId,
            new InitUploadRequest("big.bin", 100, null, "key-1", null, false, null));

        var act = () => service.UploadChunkAsync(userId, init.UploadId, 0,
            "bytes 10-109/100", new MemoryStream(new byte[100]));

        (await act.Should().ThrowAsync<ChunkedUploadException>())
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UploadChunkAsync_DuplicateChunk_DoesNotStoreTwice()
    {
        var (service, _, userId, _) = CreateService();
        var (init, _) = await service.InitAsync(userId,
            new InitUploadRequest("small.bin", 100, null, "key-1", null, false, null));
        var content = MakeContent(100);

        var first = await service.UploadChunkAsync(userId, init.UploadId, 0,
            "bytes 0-99/100", new MemoryStream(content));
        var second = await service.UploadChunkAsync(userId, init.UploadId, 0,
            "bytes 0-99/100", new MemoryStream(content));

        first.ConfirmedChunks.Should().Equal(0);
        second.ConfirmedChunks.Should().Equal(0);
        var status = await service.GetStatusAsync(userId, init.UploadId);
        status!.ConfirmedChunks.Should().Equal(0);
        Directory.EnumerateFiles(Path.Combine(_basePath, "uploads-tmp", init.UploadId.ToString("N")), "*.chunk")
            .Should().HaveCount(1);
    }

    [Fact]
    public async Task CompleteAsync_FullFlow_MergesVerifiesAndReturnsSingleItem()
    {
        var (service, shareMock, userId, factory) = CreateService();
        var totalSize = ChunkSize * 2L + 1; // 3 片：4MB、4MB、1 字节
        var content = MakeContent(totalSize);
        var topicId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Topics.Add(new Topic { Id = topicId, Name = "T" });
            await db.SaveChangesAsync();
        }

        var (init, _) = await service.InitAsync(userId,
            new InitUploadRequest("big.bin", totalSize, null, "key-1", topicId, false, "application/octet-stream"));

        await service.UploadChunkAsync(userId, init.UploadId, 0,
            $"bytes 0-{ChunkSize - 1}/{totalSize}", new MemoryStream(content, 0, ChunkSize));
        await service.UploadChunkAsync(userId, init.UploadId, 1,
            $"bytes {ChunkSize}-{ChunkSize * 2 - 1}/{totalSize}", new MemoryStream(content, ChunkSize, ChunkSize));
        await service.UploadChunkAsync(userId, init.UploadId, 2,
            $"bytes {ChunkSize * 2}-{totalSize - 1}/{totalSize}", new MemoryStream(content, ChunkSize * 2, 1));

        var dto = await service.CompleteAsync(userId, init.UploadId, Sha256Hex(content));

        dto.FileName.Should().Be("big.bin");
        dto.FileSize.Should().Be(totalSize);
        dto.TopicId.Should().Be(topicId);
        shareMock.Verify(s => s.SendFileAsync(
            It.IsAny<Stream>(), "big.bin", "application/octet-stream", topicId,
            totalSize, false, It.IsAny<CancellationToken>()), Times.Once);

        // 重复完成返回同一条消息，不再落库第二份。
        // 注意：ShareService 是 mock，不会真写 ShareItems 行；为让重放能查到该消息，
        // 这里按返回的 Id 补一行（模拟真实 SendFileAsync 落库后的状态）。
        await using (var db = factory.CreateDbContext())
        {
            db.ShareItems.Add(new ShareItem
            {
                Id = dto.Id,
                ContentType = ShareContentType.File,
                Content = "20261115/test.bin",
                FileName = "big.bin",
                FileSize = totalSize,
                MimeType = "application/octet-stream",
                CreatedAt = _timeProvider.GetUtcNow(),
                TopicId = topicId,
                UploadState = UploadState.Ready
            });
            await db.SaveChangesAsync();
        }

        var replay = await service.CompleteAsync(userId, init.UploadId, Sha256Hex(content));
        replay.Id.Should().Be(dto.Id);
        shareMock.Verify(s => s.SendFileAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<long?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);

        // 临时分片已清掉
        Directory.Exists(Path.Combine(_basePath, "uploads-tmp", init.UploadId.ToString("N")))
            .Should().BeFalse();
    }

    [Fact]
    public async Task CompleteAsync_WrongHash_DiscardsChunksAndReturns400()
    {
        var (service, shareMock, userId, _) = CreateService();
        var content = MakeContent(100);
        var (init, _) = await service.InitAsync(userId,
            new InitUploadRequest("small.bin", 100, null, "key-1", null, false, null));
        await service.UploadChunkAsync(userId, init.UploadId, 0,
            "bytes 0-99/100", new MemoryStream(content));

        var wrongHash = new string('0', 64);
        var act = () => service.CompleteAsync(userId, init.UploadId, wrongHash);

        (await act.Should().ThrowAsync<ChunkedUploadException>())
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        shareMock.Verify(s => s.SendFileAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<long?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        // 已确认分片已丢弃：客户端需重传
        var status = await service.GetStatusAsync(userId, init.UploadId);
        status!.ConfirmedChunks.Should().BeEmpty();
        status.Status.Should().Be("Active");
    }

    [Fact]
    public async Task CompleteAsync_MissingChunk_Returns400()
    {
        var (service, _, userId, _) = CreateService();
        var totalSize = ChunkSize + 10L;
        var content = MakeContent(totalSize);
        var (init, _) = await service.InitAsync(userId,
            new InitUploadRequest("big.bin", totalSize, null, "key-1", null, false, null));
        await service.UploadChunkAsync(userId, init.UploadId, 0,
            $"bytes 0-{ChunkSize - 1}/{totalSize}", new MemoryStream(content, 0, ChunkSize));

        var act = () => service.CompleteAsync(userId, init.UploadId, Sha256Hex(content));

        (await act.Should().ThrowAsync<ChunkedUploadException>())
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CancelAsync_RemovesSessionAndChunks()
    {
        var (service, _, userId, _) = CreateService();
        var content = MakeContent(100);
        var (init, _) = await service.InitAsync(userId,
            new InitUploadRequest("small.bin", 100, null, "key-1", null, false, null));
        await service.UploadChunkAsync(userId, init.UploadId, 0,
            "bytes 0-99/100", new MemoryStream(content));

        await service.CancelAsync(userId, init.UploadId);

        (await service.GetStatusAsync(userId, init.UploadId)).Should().BeNull();
        Directory.Exists(Path.Combine(_basePath, "uploads-tmp", init.UploadId.ToString("N")))
            .Should().BeFalse();

        var act = () => service.UploadChunkAsync(userId, init.UploadId, 0,
            "bytes 0-99/100", new MemoryStream(content));
        (await act.Should().ThrowAsync<ChunkedUploadException>())
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task SweepExpiredAsync_RemovesExpiredSessionsAndFiles()
    {
        var (service, _, userId, _) = CreateService();
        var content = MakeContent(100);
        var (init, _) = await service.InitAsync(userId,
            new InitUploadRequest("small.bin", 100, null, "key-1", null, false, null));
        await service.UploadChunkAsync(userId, init.UploadId, 0,
            "bytes 0-99/100", new MemoryStream(content));

        _timeProvider.Advance(TimeSpan.FromHours(25));

        var swept = await service.SweepExpiredAsync();

        swept.Should().Be(1);
        (await service.GetStatusAsync(userId, init.UploadId)).Should().BeNull();
        Directory.Exists(Path.Combine(_basePath, "uploads-tmp", init.UploadId.ToString("N")))
            .Should().BeFalse();
    }
}
