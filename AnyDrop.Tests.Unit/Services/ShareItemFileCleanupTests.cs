using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

/// <summary>
/// 验证「一条消息在磁盘上对应哪些文件」的清理逻辑。
///
/// 此前三处删除路径（手动清理、批量删除、过期自动清理）都只删除 <c>Content</c>，
/// 漏掉 <c>ThumbnailPath</c>，导致 <c>Storage:BasePath/thumbnails/</c> 下的 JPEG 永久残留。
/// </summary>
public class ShareItemFileCleanupTests
{
    private static ShareItem CreateItem(ShareContentType type, string content, string? thumbnailPath = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ContentType = type,
            Content = content,
            ThumbnailPath = thumbnailPath
        };

    [Fact]
    public async Task DeleteItemFilesAsync_ImageWithThumbnail_DeletesBoth()
    {
        var storageMock = new Mock<IFileStorageService>();
        var item = CreateItem(ShareContentType.Image, "20260101/a.png", "thumbnails/a.jpg");

        await ShareItemFileCleanup.DeleteItemFilesAsync(
            storageMock.Object, NullLogger.Instance, item, CancellationToken.None, "test");

        storageMock.Verify(x => x.DeleteFileAsync("20260101/a.png", It.IsAny<CancellationToken>()), Times.Once);
        storageMock.Verify(x => x.DeleteFileAsync("thumbnails/a.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteItemFilesAsync_VideoWithThumbnail_DeletesBoth()
    {
        var storageMock = new Mock<IFileStorageService>();
        var item = CreateItem(ShareContentType.Video, "20260101/v.mp4", "thumbnails/v.jpg");

        await ShareItemFileCleanup.DeleteItemFilesAsync(
            storageMock.Object, NullLogger.Instance, item, CancellationToken.None, "test");

        storageMock.Verify(x => x.DeleteFileAsync("20260101/v.mp4", It.IsAny<CancellationToken>()), Times.Once);
        storageMock.Verify(x => x.DeleteFileAsync("thumbnails/v.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteItemFilesAsync_TextItem_DeletesNothing()
    {
        var storageMock = new Mock<IFileStorageService>();
        var item = CreateItem(ShareContentType.Text, "hello");

        await ShareItemFileCleanup.DeleteItemFilesAsync(
            storageMock.Object, NullLogger.Instance, item, CancellationToken.None, "test");

        storageMock.Verify(
            x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteItemFilesAsync_ImageWithoutThumbnail_DeletesOnlyContent()
    {
        var storageMock = new Mock<IFileStorageService>();
        var item = CreateItem(ShareContentType.Image, "20260101/a.png");

        await ShareItemFileCleanup.DeleteItemFilesAsync(
            storageMock.Object, NullLogger.Instance, item, CancellationToken.None, "test");

        storageMock.Verify(
            x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteItemFilesAsync_WhenStorageThrows_DoesNotPropagate()
    {
        // 单个文件删除失败不应阻断整批清理
        var storageMock = new Mock<IFileStorageService>();
        storageMock
            .Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("simulated disk error"));
        var item = CreateItem(ShareContentType.Image, "20260101/a.png", "thumbnails/a.jpg");

        var act = () => ShareItemFileCleanup.DeleteItemFilesAsync(
            storageMock.Object, NullLogger.Instance, item, CancellationToken.None, "test");

        await act.Should().NotThrowAsync();
        // 缩略图仍应被尝试删除（第一个失败不短路第二个）
        storageMock.Verify(x => x.DeleteFileAsync("thumbnails/a.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryDeleteAsync_WithBlankPath_DoesNotCallStorage()
    {
        var storageMock = new Mock<IFileStorageService>();

        await ShareItemFileCleanup.TryDeleteAsync(
            storageMock.Object, NullLogger.Instance, "   ", Guid.NewGuid(), CancellationToken.None, "test");

        storageMock.Verify(
            x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
