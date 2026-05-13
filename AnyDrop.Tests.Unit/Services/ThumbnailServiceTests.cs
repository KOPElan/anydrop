using AnyDrop.Data;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

public class ThumbnailServiceTests
{
    // ── 辅助方法 ───────────────────────────────────────────────────────────────

    private static AnyDropDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-thumb-{Guid.NewGuid():N}")
            .Options);

    private static ThumbnailService CreateService(
        AnyDropDbContext db,
        IFileStorageService? storageService = null,
        string? basePath = null)
    {
        var storage = storageService ?? new Mock<IFileStorageService>().Object;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BasePath"] = basePath ?? Path.Combine(Path.GetTempPath(), "anydrop-test")
            })
            .Build();
        return new ThumbnailService(db, storage, config, NullLogger<ThumbnailService>.Instance);
    }

    // ── GenerateThumbnailAsync 基础路径 ───────────────────────────────────────

    [Fact]
    public async Task GenerateThumbnailAsync_WhenItemNotFound_ShouldReturnNull()
    {
        await using var db = CreateDbContext();
        var sut = CreateService(db);

        var result = await sut.GenerateThumbnailAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenItemIsText_ShouldReturnNull()
    {
        await using var db = CreateDbContext();
        var item = new ShareItem { ContentType = ShareContentType.Text, Content = "hello" };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var sut = CreateService(db);
        var result = await sut.GenerateThumbnailAsync(item.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenThumbnailAlreadyExists_ShouldReturnExistingPath()
    {
        await using var db = CreateDbContext();
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
        var sut = CreateService(db, storageMock.Object);
        var result = await sut.GenerateThumbnailAsync(item.Id);

        result.Should().Be(existingPath);
        // 已有缩略图时不应再调用文件存储
        storageMock.Verify(s => s.GetFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateThumbnailAsync_WhenImageDataInvalid_ShouldReturnNull()
    {
        // 传入非图片数据时 ImageSharp 抛出异常，服务应捕获异常并返回 null（不向上抛出）
        await using var db = CreateDbContext();
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/invalid.jpg"
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(s => s.GetFileAsync(item.Content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream("not_an_image"u8.ToArray()));

        var sut = CreateService(db, storageMock.Object);
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
        await using var db = CreateDbContext();
        var sut = CreateService(db);

        var count = await sut.ProcessPendingThumbnailsAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task ProcessPendingThumbnailsAsync_ShouldSkipItemsWithExistingThumbnailPath()
    {
        await using var db = CreateDbContext();
        var item = new ShareItem
        {
            ContentType = ShareContentType.Image,
            Content = "20240101/img.jpg",
            ThumbnailPath = "thumbnails/already.jpg"  // 已有缩略图
        };
        db.ShareItems.Add(item);
        await db.SaveChangesAsync();

        var storageMock = new Mock<IFileStorageService>();
        var sut = CreateService(db, storageMock.Object);
        var count = await sut.ProcessPendingThumbnailsAsync();

        // 已有缩略图的条目不计入"待处理"，成功数为 0
        count.Should().Be(0);
        storageMock.Verify(s => s.GetFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
