using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Tests.Unit.TestDoubles;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

public class ShareServiceTests
{
    [Fact]
    public async Task SendTextAsync_ValidContent_PersistsToDatabase()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out _);

        var dto = await service.SendTextAsync("hello world");

        var entity = await dbContext.ShareItems.SingleAsync();
        entity.ContentType.Should().Be(ShareContentType.Text);
        entity.Content.Should().Be("hello world");
        dto.Content.Should().Be("hello world");
        dto.ContentType.Should().Be(ShareContentType.Text);
    }

    [Fact]
    public async Task SendTextAsync_ValidContent_BroadcastsViaSignalR()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out var clientProxyMock, out _);

        var dto = await service.SendTextAsync("broadcast me");

        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                "ReceiveShareItem",
                It.Is<object?[]>(args =>
                    args.Length == 1 &&
                    args[0] != null &&
                    args[0]!.GetType() == typeof(ShareItemDto) &&
                    ((ShareItemDto)args[0]!).Content == "broadcast me" &&
                    ((ShareItemDto)args[0]!).Id == dto.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTextAsync_EmptyContent_ThrowsArgumentException()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out _);

        var act = () => service.SendTextAsync("   ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendTextAsync_ContentExceeds10000Chars_ThrowsArgumentException()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out _);
        var content = new string('a', 10_001);

        var act = () => service.SendTextAsync(content);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendTextAsync_ContentWithin10000CharsAfterTrim_PersistsTrimmedContent()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out _);
        var content = $"   {new string('a', 10_000)}   ";

        var dto = await service.SendTextAsync(content);

        dto.Content.Length.Should().Be(10_000);
        dto.Content.Should().Be(new string('a', 10_000));
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsTopNOrderedByCreatedAtDesc()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        dbContext.ShareItems.AddRange(
            new ShareItem { Content = "first", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3), ContentType = ShareContentType.Text },
            new ShareItem { Content = "second", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2), ContentType = ShareContentType.Text },
            new ShareItem { Content = "third", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _, out _);
        var result = await service.GetRecentAsync(2);

        result.Select(x => x.Content).Should().Equal("third", "second");
    }

    [Fact]
    public async Task SendFileAsync_ImageMimeType_PersistsAsImageAndBroadcasts()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out var clientProxyMock, out _);

        await using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]); // S3-02：魔数校验要求 image/png 带真实 PNG 文件头
        var dto = await service.SendFileAsync(stream, "photo.png", "image/png");

        dto.ContentType.Should().Be(ShareContentType.Image);
        dto.FileName.Should().Be("photo.png");
        var entity = await dbContext.ShareItems.SingleAsync();
        entity.ContentType.Should().Be(ShareContentType.Image);
        entity.Content.Should().StartWith("saved/");

        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                "ReceiveShareItem",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendFileAsync_MagicNumberMismatch_ThrowsAndLeavesNoPlaceholder()
    {
        // S3-02：JPEG 字节自称 PNG——魔数校验在建预占位行之前拒绝，
        // 不留 Uploading 残留行、不落盘、不推送。
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out var clientProxyMock, out var fileStorageMock);

        await using var stream = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0x00, 0x00]);
        var act = () => service.SendFileAsync(stream, "photo.png", "image/png");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*文件内容与声明的文件类型*");

        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        fileStorageMock.Verify(
            x => x.SaveFileAtPathAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendFileAsync_Image_WhenScheduledThumbnailGenerationDisabled_ShouldGenerateThumbnailAsync()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateServiceWithThumbnailSettings(
            factory,
            scheduledThumbnailGenerationEnabled: false,
            out var thumbnailServiceMock);

        var thumbnailTriggered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        thumbnailServiceMock
            .Setup(x => x.GenerateThumbnailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => thumbnailTriggered.TrySetResult(true))
            .ReturnsAsync((string?)null);

        await using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]); // S3-02：魔数校验要求 image/png 带真实 PNG 文件头
        var dto = await service.SendFileAsync(stream, "photo.png", "image/png");

        var completedTask = await Task.WhenAny(thumbnailTriggered.Task, Task.Delay(300));
        completedTask.Should().Be(thumbnailTriggered.Task);
        thumbnailServiceMock.Verify(x => x.GenerateThumbnailAsync(dto.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendFileAsync_Image_WhenScheduledThumbnailGenerationEnabled_ShouldNotGenerateThumbnailAsync()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateServiceWithThumbnailSettings(
            factory,
            scheduledThumbnailGenerationEnabled: true,
            out var thumbnailServiceMock);

        await using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]); // S3-02：魔数校验要求 image/png 带真实 PNG 文件头
        await service.SendFileAsync(stream, "photo.png", "image/png");
        await Task.Delay(150);

        thumbnailServiceMock.Verify(x => x.GenerateThumbnailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendTextAsync_Link_WhenAutoFetchDisabled_ShouldNotTriggerMetadataFetchFailure()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out _, autoFetchEnabled: false);

        var dto = await service.SendTextAsync("https://example.com");

        dto.ContentType.Should().Be(ShareContentType.Link);
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_WithOldMessages_DeletesAndReturnsCount()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        dbContext.ShareItems.AddRange(
            new ShareItem { Content = "old", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2), ContentType = ShareContentType.Text },
            new ShareItem { Content = "new", CreatedAt = DateTimeOffset.UtcNow.AddHours(-1), ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _, out _);
        var count = await service.CleanupOldMessagesAsync(1);

        count.Should().Be(1);
        dbContext.ShareItems.Should().ContainSingle(x => x.Content == "new");
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_WithNoOldMessages_ReturnsZero()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        dbContext.ShareItems.Add(new ShareItem { Content = "new", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _, out _);
        var count = await service.CleanupOldMessagesAsync(1);

        count.Should().Be(0);
        dbContext.ShareItems.Should().HaveCount(1);
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_WithOldMessages_BroadcastsShareItemsDeleted()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var oldItem = new ShareItem { Content = "old", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2), ContentType = ShareContentType.Text };
        dbContext.ShareItems.Add(oldItem);
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out var clientProxyMock, out _);
        await service.CleanupOldMessagesAsync(1);

        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                "ShareItemsDeleted",
                It.Is<object?[]>(args =>
                    args.Length == 1 &&
                    args[0] != null &&
                    ((List<Guid>)args[0]!).Contains(oldItem.Id)),
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task DeleteShareItemsAsync_WithExistingIds_DeletesAndReturnsActualCount()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var item1 = new ShareItem { Content = "a", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Text };
        var item2 = new ShareItem { Content = "b", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Text };
        dbContext.ShareItems.AddRange(item1, item2);
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _, out _);
        // item2.Id 不存在于请求中，只删除 item1
        var nonExistentId = Guid.NewGuid();
        var count = await service.DeleteShareItemsAsync([item1.Id, nonExistentId]);

        count.Should().Be(1);
        dbContext.ShareItems.Should().ContainSingle(x => x.Content == "b");
    }

    [Fact]
    public async Task DeleteShareItemsAsync_WithEmptyIds_ReturnsZero()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out _);

        var count = await service.DeleteShareItemsAsync([]);

        count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteShareItemsAsync_WithExistingIds_BroadcastsShareItemsDeleted()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var item = new ShareItem { Content = "x", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Text };
        dbContext.ShareItems.Add(item);
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out var clientProxyMock, out _);
        await service.DeleteShareItemsAsync([item.Id]);

        clientProxyMock.Verify(
            proxy => proxy.SendCoreAsync(
                "ShareItemsDeleted",
                It.Is<object?[]>(args =>
                    args.Length == 1 &&
                    args[0] != null &&
                    ((List<Guid>)args[0]!).Contains(item.Id)),
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task DeleteShareItemsAsync_FileItem_DeletesFileAndDoesNotThrow()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var item = new ShareItem { Content = "files/photo.png", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Image, FileName = "photo.png", MimeType = "image/png" };
        dbContext.ShareItems.Add(item);
        await dbContext.SaveChangesAsync();

        var service = CreateService(factory, out _, out var fileStorageMock);
        fileStorageMock.Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await service.DeleteShareItemsAsync([item.Id]);

        fileStorageMock.Verify(x => x.DeleteFileAsync("files/photo.png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendFileAsync_NonExistentTopic_DoesNotSaveFile()
    {
        // 主题校验必须发生在落盘之前：反过来时主题不存在会抛出 ArgumentException，
        // 端点转换成 400 返回，但文件已写入磁盘且永远不会被任何 ShareItem 引用。
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out var fileStorageMock);
        await using var stream = new MemoryStream([1, 2, 3]);

        var act = () => service.SendFileAsync(
            stream, "a.bin", "application/octet-stream", topicId: Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();

        // 主题不存在时，连预占位行都不建、文件也不落盘
        fileStorageMock.Verify(
            x => x.SaveFileAtPathAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        await using var verifyDb = factory.CreateDbContext();
        (await verifyDb.ShareItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteShareItemsAsync_ImageItem_AlsoDeletesThumbnail()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out var fileStorageMock);

        var item = new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.Image,
            Content = "20260101/a.png",
            ThumbnailPath = "thumbnails/a.jpg",
            MimeType = "image/png",
            FileName = "a.png",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ShareItems.Add(item);
        await dbContext.SaveChangesAsync();

        await service.DeleteShareItemsAsync([item.Id]);

        fileStorageMock.Verify(
            x => x.DeleteFileAsync("20260101/a.png", It.IsAny<CancellationToken>()), Times.Once);
        fileStorageMock.Verify(
            x => x.DeleteFileAsync("thumbnails/a.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    // S0-05（D5）：批量清理改为只写延迟删除标记，不再立即删文件。
    // 原断言（清理时同时删原文件与缩略图）是旧语义，已废弃；
    // 文件由 Stage 1 的清扫任务在 DeleteAfter 到期后删除。
    [Fact]
    public async Task CleanupOldMessagesAsync_VideoItem_MarksForDelayedDeletionWithoutDeletingFiles()
    {
        var factory = CreateDbContextFactory();
        await using var dbContext = factory.CreateDbContext();
        var service = CreateService(factory, out _, out var fileStorageMock);

        dbContext.ShareItems.Add(new ShareItem
        {
            Id = Guid.NewGuid(),
            ContentType = ShareContentType.Video,
            Content = "20260101/v.mp4",
            ThumbnailPath = "thumbnails/v.jpg",
            MimeType = "video/mp4",
            FileName = "v.mp4",
            CreatedAt = DateTimeOffset.UtcNow.AddMonths(-3)
        });
        await dbContext.SaveChangesAsync();

        var deleted = await service.CleanupOldMessagesAsync(1);

        deleted.Should().Be(1);
        fileStorageMock.Verify(
            x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // 全新上下文验证：播种用的上下文仍跟踪着旧实体，会返回过期值。
        await using var verifyDb = factory.CreateDbContext();
        var marked = await verifyDb.ShareItems.IgnoreQueryFilters().SingleAsync();
        marked.DeleteAfter.Should().NotBeNull();
    }

    private static TestDbContextFactory CreateDbContextFactory()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-{Guid.NewGuid():N}")
            .Options;

        return new TestDbContextFactory(options);
    }

    private static ShareService CreateServiceWithThumbnailSettings(
        TestDbContextFactory factory,
        bool scheduledThumbnailGenerationEnabled,
        out Mock<IThumbnailService> thumbnailServiceMock,
        TimeProvider? timeProvider = null)
    {
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(clients => clients.All).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<ShareHub>>();
        hubContextMock.Setup(context => context.Clients).Returns(hubClientsMock.Object);

        var topicServiceMock = new Mock<ITopicService>();
        topicServiceMock
            .Setup(x => x.GetAllTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TopicDto>());

        var fileStorageServiceMock = new Mock<IFileStorageService>();
        fileStorageServiceMock
            .Setup(x => x.GenerateStoragePath(It.IsAny<string>()))
            .Returns("saved/file.bin");
        fileStorageServiceMock
            .Setup(x => x.SaveFileAtPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string path, string _, CancellationToken _) => path);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var linkMetadataService = new LinkMetadataService(httpClientFactoryMock.Object, NullLogger<LinkMetadataService>.Instance);

        var systemSettingsMock = new Mock<ISystemSettingsService>();
        systemSettingsMock.Setup(x => x.IsAutoFetchLinkPreviewEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        systemSettingsMock.Setup(x => x.IsScheduledThumbnailGenerationEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduledThumbnailGenerationEnabled);

        thumbnailServiceMock = new Mock<IThumbnailService>();
        thumbnailServiceMock
            .Setup(x => x.GenerateThumbnailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        return new ShareService(
            factory,
            hubContextMock.Object,
            topicServiceMock.Object,
            fileStorageServiceMock.Object,
            thumbnailServiceMock.Object,
            linkMetadataService,
            systemSettingsMock.Object,
            timeProvider ?? TimeProvider.System,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<ShareService>>(),
            new MessageSearchIndex());
    }

    private static ShareService CreateService(
        TestDbContextFactory factory,
        out Mock<IClientProxy> clientProxyMock,
        out Mock<IFileStorageService> fileStorageServiceMock,
        bool autoFetchEnabled = true,
        TimeProvider? timeProvider = null)
    {
        clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClientsMock = new Mock<IHubClients>();
        hubClientsMock.Setup(clients => clients.All).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<ShareHub>>();
        hubContextMock.Setup(context => context.Clients).Returns(hubClientsMock.Object);

        var topicServiceMock = new Mock<ITopicService>();
        topicServiceMock
            .Setup(x => x.GetAllTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TopicDto>());

        fileStorageServiceMock = new Mock<IFileStorageService>();
        fileStorageServiceMock
            .Setup(x => x.GenerateStoragePath(It.IsAny<string>()))
            .Returns("saved/file.bin");
        fileStorageServiceMock
            .Setup(x => x.SaveFileAtPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string path, string _, CancellationToken _) => path);

        // LinkMetadataService: 使用真实实例，但 HttpClientFactory 不会发起外部请求
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var linkMetadataService = new LinkMetadataService(httpClientFactoryMock.Object, NullLogger<LinkMetadataService>.Instance);

        // IServiceScopeFactory: fire-and-forget 中使用，测试中不需要实际调用
        var systemSettingsMock = new Mock<ISystemSettingsService>();
        systemSettingsMock.Setup(x => x.IsAutoFetchLinkPreviewEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(autoFetchEnabled);
        systemSettingsMock.Setup(x => x.IsScheduledThumbnailGenerationEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var thumbnailServiceMock = new Mock<IThumbnailService>();
        thumbnailServiceMock
            .Setup(x => x.GenerateThumbnailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        return new ShareService(
            factory,
            hubContextMock.Object,
            topicServiceMock.Object,
            fileStorageServiceMock.Object,
            thumbnailServiceMock.Object,
            linkMetadataService,
            systemSettingsMock.Object,
            timeProvider ?? TimeProvider.System,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<ShareService>>(),
            new MessageSearchIndex());
    }
}
