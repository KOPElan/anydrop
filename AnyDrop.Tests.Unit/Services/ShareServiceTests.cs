using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

public class ShareServiceTests
{
    [Fact]
    public async Task SendTextAsync_ValidContent_PersistsToDatabase()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _);

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
        var service = CreateService(dbContext, out var clientProxyMock);

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
        var service = CreateService(dbContext, out _);

        var act = () => service.SendTextAsync("   ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendTextAsync_ContentExceeds10000Chars_ThrowsArgumentException()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _);
        var content = new string('a', 10_001);

        var act = () => service.SendTextAsync(content);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendTextAsync_ContentWithin10000CharsAfterTrim_PersistsTrimmedContent()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, out _);
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

        var service = CreateService(dbContext, out _);
        var result = await service.GetRecentAsync(2);

        result.Select(x => x.Content).Should().Equal("third", "second");
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase($"anydrop-{Guid.NewGuid():N}")
            .Options;

        return new AnyDropDbContext(options);
    }

    private static ShareService CreateService(AnyDropDbContext dbContext, out Mock<IClientProxy> clientProxyMock)
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

        return new ShareService(dbContext, hubContextMock.Object, topicServiceMock.Object);
    }
}
