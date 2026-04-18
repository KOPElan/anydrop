using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Models;
using AnyDrop.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AnyDrop.Tests.Unit.Services;

public sealed class ShareServiceTests
{
    [Fact]
    public async Task SendTextAsync_ValidContent_PersistsAndBroadcasts()
    {
        await using var dbContext = CreateDbContext();
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(
                "ReceiveShareItem",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(clients => clients.All).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<ShareHub>>();
        hubContextMock.Setup(context => context.Clients).Returns(clientsMock.Object);

        var service = new ShareService(dbContext, hubContextMock.Object);

        var result = await service.SendTextAsync("hello world");

        result.Content.Should().Be("hello world");
        result.ContentType.Should().Be(ShareContentType.Text);
        dbContext.ShareItems.Should().ContainSingle(item => item.Id == result.Id);
        clientProxyMock.Verify(proxy => proxy.SendCoreAsync(
            "ReceiveShareItem",
            It.Is<object?[]>(args => args.Length == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTextAsync_EmptyContent_ThrowsArgumentException()
    {
        await using var dbContext = CreateDbContext();
        var hubContextMock = CreateHubContext();
        var service = new ShareService(dbContext, hubContextMock.Object);

        var action = async () => await service.SendTextAsync("   ");

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetRecentAsync_MoreThanCount_ReturnsChronologicalSubset()
    {
        await using var dbContext = CreateDbContext();
        var baseTime = DateTimeOffset.UtcNow;

        var seededItems = Enumerable.Range(1, 4)
            .Select(index => new ShareItem
            {
                ContentType = ShareContentType.Text,
                Content = $"msg-{index}",
                CreatedAt = baseTime.AddMinutes(index),
            });

        await dbContext.ShareItems.AddRangeAsync(seededItems);
        await dbContext.SaveChangesAsync();

        var service = new ShareService(dbContext, CreateHubContext().Object);

        var result = await service.GetRecentAsync(3);

        result.Select(item => item.Content).Should().Equal("msg-2", "msg-3", "msg-4");
    }

    [Fact]
    public async Task GetRecentAsync_CountOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        await using var dbContext = CreateDbContext();
        var service = new ShareService(dbContext, CreateHubContext().Object);

        var action = async () => await service.GetRecentAsync(0);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private static AnyDropDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AnyDropDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AnyDropDbContext(options);
    }

    private static Mock<IHubContext<ShareHub>> CreateHubContext()
    {
        var clientProxyMock = new Mock<IClientProxy>();
        clientProxyMock
            .Setup(proxy => proxy.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(clients => clients.All).Returns(clientProxyMock.Object);

        var hubContextMock = new Mock<IHubContext<ShareHub>>();
        hubContextMock.Setup(context => context.Clients).Returns(clientsMock.Object);

        return hubContextMock;
    }
}
