using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using AnyDrop.Tests.Unit.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AnyDrop.Tests.Unit.Services;

public class ThumbnailServiceTests
{
    // ── 辅助方法 ───────────────────────────────────────────────────────────────

    private static TestDbContextFactory CreateDbContextFactory() =>
        new(new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-thumb-{Guid.NewGuid():N}")
            .Options);

    private static ThumbnailService CreateService(
        TestDbContextFactory factory,
        IFileStorageService? storageService = null,
        string? basePath = null,
        Dictionary<string, string?>? extraConfig = null)
    {
        var storage = storageService ?? new Mock<IFileStorageService>().Object;
        var settings = new Dictionary<string, string?>
        {
            ["Storage:BasePath"] = basePath ?? Path.Combine(Path.GetTempPath(), "anydrop-test")
        };
        if (extraConfig is not null)
        {
            foreach (var (key, value) in extraConfig)
            {
                settings[key] = value;
            }
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new ThumbnailService(factory, storage, config, NullLogger<ThumbnailService>.Instance);
    }

    /// <summary>
    /// 生成一张指定尺寸的真 PNG（ImageSharp 编码），供需要走完整解码路径的测试用。
    /// </summary>
    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    // ── GenerateThumbnailAsync 基础路径 ───────────────────────────────────────

    [Fact]
    public async Task GenerateThumbnailAsync_WhenItemNotFound_ShouldReturnNull()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var sut = CreateService(factory);

        var result = await sut.GenerateThumbnailAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenItemIsText_ShouldReturnNull()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = new ShareItem { ContentType = ShareContentType.Text, Content = "hello" };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var sut = CreateService(factory);
        var result = await sut.GenerateThumbnailAsync(item.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenThumbnailAlreadyExists_ShouldReturnExistingPath()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        const string existingPath = "thumbnails/existing.jpg";
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/img.jpg",
            ThumbnailPath = existingPath
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(s => s.FileExistsAsync(existingPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = CreateService(factory, storageMock.Object);
        var result = await sut.GenerateThumbnailAsync(item.Id);

        result.Should().Be(existingPath);
        // 已有缩略图时不应再调用文件存储
        storageMock.Verify(s => s.GetFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenImageDataInvalid_ShouldReturnNull()
    {
        // 传入非图片数据时 ImageSharp 抛出异常，服务应捕获异常并返回 null（不向上抛出）
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/invalid.jpg"
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(s => s.FileExistsAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await using var invalidData = new MemoryStream("not_an_image"u8.ToArray());
        storageMock
            .Setup(s => s.GetFileAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidData);

        var sut = CreateService(factory, storageMock.Object);
        var act = async () => await sut.GenerateThumbnailAsync(item.Id);

        // 不应抛出异常
        await act.Should().NotThrowAsync();
        var result = await sut.GenerateThumbnailAsync(item.Id);
        result.Should().BeNull();
    }

    // ── ProcessPendingThumbnailsAsync ─────────────────────────────────────────

    [Fact]
    public async Task ProcessPendingThumbnailsAsync_WhenNoPendingItems_ShouldReturnZero()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var sut = CreateService(factory);

        var count = await sut.ProcessPendingThumbnailsAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task ProcessPendingThumbnailsAsync_ShouldSkipItemsWithExistingThumbnailPath()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/img.jpg",
            ThumbnailPath = "thumbnails/already.jpg"  // 已有缩略图
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var storageMock = new Mock<IFileStorageService>();
        var sut = CreateService(factory, storageMock.Object);
        var count = await sut.ProcessPendingThumbnailsAsync();

        // 已有缩略图的条目不计入"待处理"，成功数为 0
        count.Should().Be(0);
        storageMock.Verify(s => s.GetFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── S1-02：生成前校验、尺寸上限、已删缩略图自愈、ffmpeg 超时 ───────────

    [Fact]
    public async Task GenerateThumbnailAsync_WhenOriginalFileMissing_ReturnsNullWithoutDecoding()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/gone.jpg"
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(s => s.FileExistsAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = CreateService(factory, storageMock.Object);

        var result = await sut.GenerateThumbnailAsync(item.Id);

        result.Should().BeNull();
        storageMock.Verify(s => s.GetFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenThumbnailFileDeleted_Regenerates()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/img.jpg",
            ThumbnailPath = "thumbnails/deleted.jpg"
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var pngBytes = CreatePng(10, 10);
        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(s => s.FileExistsAsync("thumbnails/deleted.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        storageMock
            .Setup(s => s.FileExistsAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storageMock
            .Setup(s => s.GetFileAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(pngBytes));
        storageMock
            .Setup(s => s.SaveFileAtPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string path, string _, CancellationToken _) => path);
        var sut = CreateService(factory, storageMock.Object);

        var result = await sut.GenerateThumbnailAsync(item.Id);

        result.Should().Be($"thumbnails/{item.Id:N}.jpg");
        await using var verifyDb = factory.CreateDbContext();
        var updated = await verifyDb.ShareItems.SingleAsync();
        updated.ThumbnailPath.Should().Be(result);
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenImageExceedsDimensionLimit_ReturnsNullWithoutSaving()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/big.jpg"
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        // 200×200 的真图，但上限配成 100px：应被头信息限流拦下，不走到保存
        var pngBytes = CreatePng(200, 200);
        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(s => s.FileExistsAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storageMock
            .Setup(s => s.GetFileAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(pngBytes));
        var sut = CreateService(factory, storageMock.Object, extraConfig: new Dictionary<string, string?>
        {
            ["Thumbnails:MaxImageDimensionPixels"] = "100"
        });

        var result = await sut.GenerateThumbnailAsync(item.Id);

        result.Should().BeNull();
        storageMock.Verify(
            s => s.SaveFileAtPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenImageWithinLimits_GeneratesThumbnail()
    {
        var factory = CreateDbContextFactory();
        await using var db = factory.CreateDbContext();
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/small.jpg"
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var pngBytes = CreatePng(10, 10);
        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(s => s.FileExistsAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storageMock
            .Setup(s => s.GetFileAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(pngBytes));
        storageMock
            .Setup(s => s.SaveFileAtPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string path, string _, CancellationToken _) => path);
        var sut = CreateService(factory, storageMock.Object);

        var result = await sut.GenerateThumbnailAsync(item.Id);

        result.Should().NotBeNull();
        storageMock.Verify(
            s => s.SaveFileAtPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessPendingThumbnailsAsync_SameTimestampBatch_NoSkips()
    {
        // 150 条同一时间戳：跨两个 100 条批次，(CreatedAt, Id) 游标必须一条不漏
        var factory = CreateDbContextFactory();
        var createdAt = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        await using var db = factory.CreateDbContext();
        for (var i = 0; i < 150; i++)
        {
            db.ShareItems.Add(new ShareItem
            {
                ContentType = ShareContentType.Image,
                Content = $"20240101/img{i}.jpg",
                CreatedAt = createdAt
            });
        }
        await db.SaveChangesAsync();

        var pngBytes = CreatePng(10, 10);
        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(s => s.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storageMock
            .Setup(s => s.GetFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(pngBytes));
        storageMock
            .Setup(s => s.SaveFileAtPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string path, string _, CancellationToken _) => path);
        var sut = CreateService(factory, storageMock.Object);

        var count = await sut.ProcessPendingThumbnailsAsync();

        count.Should().Be(150);
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenFfmpegTimesOut_ReturnsNull()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // 假 ffmpeg 是 sh 脚本，只在 Linux 上跑
        }

        // 假 ffmpeg：忽略参数，睡 30 秒；服务应在 5 秒超时后杀掉它
        var fakeDir = Path.Combine(Path.GetTempPath(), $"anydrop-fake-ffmpeg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fakeDir);
        var fakeFfmpeg = Path.Combine(fakeDir, "ffmpeg");
        await File.WriteAllTextAsync(fakeFfmpeg, "#!/bin/sh\nsleep 30\n");
        File.SetUnixFileMode(fakeFfmpeg, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var factory = CreateDbContextFactory();
            await using var db = factory.CreateDbContext();
            var item = new ShareItem
            {
                ContentType = ShareContentType.Video,
                Content = "20240101/clip.mp4"
            };
            db.ShareItems.Add(item);
            await db.SaveChangesAsync();

            var storageMock = new Mock<IFileStorageService>();
            storageMock
                .Setup(s => s.FileExistsAsync(item.Content, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var sut = CreateService(factory, storageMock.Object, extraConfig: new Dictionary<string, string?>
            {
                ["Thumbnails:FfmpegTimeoutSeconds"] = "5",
                ["Thumbnails:FfmpegPath"] = fakeFfmpeg
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await sut.GenerateThumbnailAsync(item.Id);
            sw.Stop();

            result.Should().BeNull();
            // 5 秒超时杀掉：既不能立刻失败（假 ffmpeg 没找到会秒回），也不能等满 30 秒
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
            sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(4));
            storageMock.Verify(
                s => s.SaveFileAtPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(fakeDir, true);
        }
    }
}
