using AnyDrop.Data;
using AnyDrop.Hubs;
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
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _, out _);

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
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out var clientProxyMock, out _);

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
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _, out _);

        var act = () => service.SendTextAsync("   ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendTextAsync_ContentExceeds10000Chars_ThrowsArgumentException()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _, out _);
        var content = new string('a', 10_001);

        var act = () => service.SendTextAsync(content);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendTextAsync_ContentWithin10000CharsAfterTrim_PersistsTrimmedContent()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _, out _);
        var content = $"   {new string('a', 10_000)}   ";

        var dto = await service.SendTextAsync(content);

        dto.Content.Length.Should().Be(10_000);
        dto.Content.Should().Be(new string('a', 10_000));
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsTopNOrderedByCreatedAtDesc()
    {
        await using var dbContext = CreateDbContext();
        dbContext.ShareItems.AddRange(
            new ShareItem { Content = "first", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3), ContentType = ShareContentType.Text },
            new ShareItem { Content = "second", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2), ContentType = ShareContentType.Text },
            new ShareItem { Content = "third", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out _, out _);
        var result = await service.GetRecentAsync(2);

        result.Select(x => x.Content).Should().Equal("third", "second");
    }

    [Fact]
    public async Task SendFileAsync_ImageMimeType_PersistsAsImageAndBroadcasts()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out var clientProxyMock, out _);

        await using var stream = new MemoryStream([1, 2, 3]);
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
    public async Task SendTextAsync_Link_WhenAutoFetchDisabled_ShouldNotTriggerMetadataFetchFailure()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _, out _, autoFetchEnabled: false);

        var dto = await service.SendTextAsync("https://example.com");

        dto.ContentType.Should().Be(ShareContentType.Link);
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_WithOldMessages_DeletesAndReturnsCount()
    {
        await using var dbContext = CreateDbContext();
        dbContext.ShareItems.AddRange(
            new ShareItem { Content = "old", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2), ContentType = ShareContentType.Text },
            new ShareItem { Content = "new", CreatedAt = DateTimeOffset.UtcNow.AddHours(-1), ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out _, out _);
        var count = await service.CleanupOldMessagesAsync(1);

        count.Should().Be(1);
        dbContext.ShareItems.Should().ContainSingle(x => x.Content == "new");
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_WithNoOldMessages_ReturnsZero()
    {
        await using var dbContext = CreateDbContext();
        dbContext.ShareItems.Add(new ShareItem { Content = "new", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Text });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out _, out _);
        var count = await service.CleanupOldMessagesAsync(1);

        count.Should().Be(0);
        dbContext.ShareItems.Should().HaveCount(1);
    }

    [Fact]
    public async Task CleanupOldMessagesAsync_WithOldMessages_BroadcastsShareItemsDeleted()
    {
        await using var dbContext = CreateDbContext();
        var oldItem = new ShareItem { Content = "old", CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2), ContentType = ShareContentType.Text };
        dbContext.ShareItems.Add(oldItem);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out var clientProxyMock, out _);
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
        await using var dbContext = CreateDbContext();
        var item1 = new ShareItem { Content = "a", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Text };
        var item2 = new ShareItem { Content = "b", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Text };
        dbContext.ShareItems.AddRange(item1, item2);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out _, out _);
        // item2.Id 不存在于请求中，只删除 item1
        var nonExistentId = Guid.NewGuid();
        var count = await service.DeleteShareItemsAsync([item1.Id, nonExistentId]);

        count.Should().Be(1);
        dbContext.ShareItems.Should().ContainSingle(x => x.Content == "b");
    }

    [Fact]
    public async Task DeleteShareItemsAsync_WithEmptyIds_ReturnsZero()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _, out _);

        var count = await service.DeleteShareItemsAsync([]);

        count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteShareItemsAsync_WithExistingIds_BroadcastsShareItemsDeleted()
    {
        await using var dbContext = CreateDbContext();
        var item = new ShareItem { Content = "x", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Text };
        dbContext.ShareItems.Add(item);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out var clientProxyMock, out _);
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
        await using var dbContext = CreateDbContext();
        var item = new ShareItem { Content = "files/photo.png", CreatedAt = DateTimeOffset.UtcNow, ContentType = ShareContentType.Image, FileName = "photo.png", MimeType = "image/png" };
        dbContext.ShareItems.Add(item);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, out _, out var fileStorageMock);
        fileStorageMock.Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await service.DeleteShareItemsAsync([item.Id]);

        fileStorageMock.Verify(x => x.DeleteFileAsync("files/photo.png", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-{Guid.NewGuid():N}")
            .Options;

        return new AnyDropDbContext(options);
    }

    private static ShareService CreateService(
        AnyDropDbContext dbContext,
        out Mock<IClientProxy> clientProxyMock,
        out Mock<IFileStorageService> fileStorageServiceMock,
        bool autoFetchEnabled = true)
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
            .Setup(x => x.SaveFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("saved/file.bin");

        // LinkMetadataService: 使用真实实例，但 HttpClientFactory 不会发起外部请求
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var linkMetadataService = new LinkMetadataService(httpClientFactoryMock.Object, NullLogger<LinkMetadataService>.Instance);

        // IServiceScopeFactory: fire-and-forget 中使用，测试中不需要实际调用
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var systemSettingsMock = new Mock<ISystemSettingsService>();
        systemSettingsMock.Setup(x => x.IsAutoFetchLinkPreviewEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(autoFetchEnabled);

        return new ShareService(
            dbContext,
            hubContextMock.Object,
            topicServiceMock.Object,
            fileStorageServiceMock.Object,
            linkMetadataService,
            systemSettingsMock.Object,
            scopeFactoryMock.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<ShareService>>());
    }
}
